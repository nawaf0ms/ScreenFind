using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenFind.App.Localization;
using ScreenFind.App.Windows;
using ScreenFind.Core.Capture;
using ScreenFind.Core.Configuration;
using ScreenFind.Core.Extraction;
using ScreenFind.Core.Input;
using ScreenFind.Core.Interop;
using ScreenFind.Core.Models;
using ScreenFind.Core.Text;

namespace ScreenFind.App.Services;

/// <summary>
/// Wires the hotkey, the extraction pipeline, the match engine and the windows together.
/// This is the only place that knows the whole flow of spec §4.
/// </summary>
public sealed class AppOrchestrator : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly GraphicsCaptureService _capture = new();
    private readonly WindowsOcrEngine _ocrEngine = new();
    private readonly OverlayManager _overlays = new();
    private readonly TrayIcon _tray = new();
    private readonly SingleInstance? _instance;

    private ScreenFindSettings _settings = ScreenFindSettings.Defaults;
    private OcrTextExtractor _ocrExtractor;
    private ExtractionPipeline _pipeline;
    private MatchEngine _matcher;
    private HotkeyListener? _hotkey;

    private SearchBarWindow? _bar;
    private SettingsWindow? _settingsWindow;
    private IntPtr _targetWindow;
    private SearchableDocument? _document;
    private IReadOnlyList<Match> _matches = Array.Empty<Match>();
    private int _activeIndex;
    private CancellationTokenSource? _extraction;
    private string _pendingQuery = string.Empty;
    private bool _extracting;

    public AppOrchestrator(SingleInstance? instance = null)
    {
        _instance = instance;
        _settings = ScreenFindSettings.Load();
        Loc.Use(_settings.Language);
        _ocrExtractor = new OcrTextExtractor(_ocrEngine, _settings.ToOcrOptions());
        _pipeline = BuildPipeline(_settings, _ocrExtractor);
        _matcher = new MatchEngine(_settings.ToMatchOptions());
    }

    private ExtractionPipeline BuildPipeline(ScreenFindSettings settings, OcrTextExtractor ocr)
    {
        var tiers = new List<ITextExtractor>();
        if (settings.UseUiAutomation) tiers.Add(new UiaTextExtractor());
        tiers.Add(ocr);

        // The capture service (and its D3D device) is shared across pipeline rebuilds.
        return new ExtractionPipeline(_capture, tiers, options: null, ownsCapture: false);
    }

    public void Start()
    {
        CreateSearchBar();
        _overlays.SetTheme(HighlightTheme.FromSettings(_settings));

        _tray.Search += () => _dispatcher.BeginInvoke(() => Toggle(Win32.GetForegroundWindow()));
        _tray.Settings += () => _dispatcher.BeginInvoke(ShowSettings);
        _tray.Exit += () => _dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        _tray.Show(Tooltip());

        if (_instance is not null)
        {
            // A second launch surfaces this instance instead of starting a rival one.
            _instance.ShowRequested += () => _dispatcher.BeginInvoke(() => Toggle(Win32.GetForegroundWindow()));
        }

        StartHotkey();

        // The app is windowless: without this, a first run looks exactly like a failed launch.
        if (!_settings.Configured)
        {
            _tray.Notify(Loc.T("App.FirstRun", _settings.Hotkey.Display), Loc.T("App.StartedTitle"));
            ShowSettings();
        }
        else
        {
            _tray.Notify(Loc.T("App.Ready", _settings.Hotkey.Display), Loc.T("App.ReadyTitle"));
        }

        foreach (string missing in _ocrExtractor.MissingLanguages)
        {
            _tray.Notify(OcrLanguageHelp.MissingLanguageMessage(missing), Loc.T("App.MissingOcrTitle"));
        }
    }

    private string Tooltip() => $"ScreenFind — {_settings.Hotkey.Display}";

    private void StartHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = new HotkeyListener(_settings.Hotkey.ToDefinition());

        _hotkey.Failed += message => _dispatcher.BeginInvoke(() =>
        {
            _tray.Notify(message + Loc.T("App.HotkeySuffix"), Loc.T("App.HotkeyFailedTitle"));
            ShowSettings();
        });

        // The foreground window must be read before any of our UI appears (spec §4, §6).
        _hotkey.Pressed += () =>
        {
            IntPtr foreground = Win32.GetForegroundWindow();
            _dispatcher.BeginInvoke(() => Toggle(foreground));
        };

        _hotkey.Start();
    }

    private void CreateSearchBar()
    {
        Win32.EnsurePerMonitorDpiAwareThread();

        _bar = new SearchBarWindow { DebounceMilliseconds = _settings.SearchDebounceMs };
        _bar.QueryCommitted += query => Search(query);
        _bar.NextRequested += () => Navigate(1);
        _bar.PreviousRequested += () => Navigate(-1);
        _bar.CloseRequested += Hide;
        _bar.CopyRequested += CopyExtractedText;

        // Realise the handle now so the capture exclusion is in place before the first show,
        // and so the bar can appear in well under 100 ms (spec §9).
        new WindowInteropHelper(_bar).EnsureHandle();
    }

    // ------------------------------------------------------------------ settings

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        // The tray icon's WinForms initialisation can leave this thread on a different DPI
        // context; without this the window frame is sized for 96 DPI and clips its own content.
        Win32.EnsurePerMonitorDpiAwareThread();

        var window = new SettingsWindow(_settings with { StartWithWindows = StartupRegistration.IsEnabled() },
            _ocrEngine.AvailableLanguages);
        _settingsWindow = window;

        try
        {
            window.ShowDialog();
            if (window.Result is not null) ApplySettings(window.Result);
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    private void ApplySettings(ScreenFindSettings settings)
    {
        bool hotkeyChanged = settings.Hotkey != _settings.Hotkey;
        bool languageChanged = settings.Language != _settings.Language;
        bool extractionChanged =
            settings.UseUiAutomation != _settings.UseUiAutomation ||
            settings.PreprocessScale != _settings.PreprocessScale ||
            settings.ResampleMode != _settings.ResampleMode ||
            settings.Grayscale != _settings.Grayscale ||
            !settings.OcrLanguages.SequenceEqual(_settings.OcrLanguages, StringComparer.OrdinalIgnoreCase);

        _settings = settings;
        _settings.Save();

        if (languageChanged)
        {
            Loc.Use(settings.Language);
            _tray.Relocalize();

            // Windows resolve their strings and mirroring when they are built, so the search bar
            // is rebuilt rather than patched.
            _bar?.Close();
            CreateSearchBar();
        }

        string? startupError = StartupRegistration.Apply(settings.StartWithWindows);
        if (startupError is not null) _tray.Notify(Loc.T("App.StartupFailed", startupError));

        _matcher = new MatchEngine(settings.ToMatchOptions());
        _overlays.SetTheme(HighlightTheme.FromSettings(settings));
        if (_bar is not null) _bar.DebounceMilliseconds = settings.SearchDebounceMs;

        if (extractionChanged)
        {
            var oldPipeline = _pipeline;
            _ocrExtractor = new OcrTextExtractor(_ocrEngine, settings.ToOcrOptions());
            _pipeline = BuildPipeline(settings, _ocrExtractor);
            oldPipeline.Dispose();   // does not own the capture service
            _document = null;        // the previous extraction used different settings
        }

        if (hotkeyChanged) StartHotkey();

        _tray.SetTooltip(Tooltip());
        _overlays.Show(_matches, _activeIndex);
    }

    // ------------------------------------------------------------------ search flow

    private void Toggle(IntPtr foreground)
    {
        if (_bar is null) return;

        if (_bar.IsVisible)
        {
            Hide();
            return;
        }

        if (IsOwnWindow(foreground)) return;

        _targetWindow = foreground;
        _document = null;
        _matches = Array.Empty<Match>();
        _activeIndex = 0;
        _pendingQuery = string.Empty;

        _overlays.Clear();
        _bar.ClearQuery();
        _bar.SetCounter(0, 0);
        _bar.SetStatus(Loc.T("Bar.Reading"));
        _bar.Show();
        _bar.PositionOver(_targetWindow);
        _bar.FocusQuery();

        _ = ExtractAsync();
    }

    private void Hide()
    {
        _extraction?.Cancel();
        _overlays.HideAll();
        _bar?.Hide();

        // Give the keyboard back to the window the user was reading.
        if (_targetWindow != IntPtr.Zero && Win32.IsWindow(_targetWindow))
            Win32.SetForegroundWindow(_targetWindow);
    }

    private async Task ExtractAsync()
    {
        _extraction?.Cancel();
        _extraction = new CancellationTokenSource();
        var token = _extraction.Token;
        _extracting = true;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var outcome = await _pipeline.ExtractAsync(_targetWindow, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            _document = SearchableDocument.Create(outcome.Document);

            string source = outcome.ExtractorName + (outcome.FromCache ? Loc.T("Bar.Cached") : string.Empty);
            _bar?.SetStatus(outcome.Document.IsEmpty
                ? Loc.T("Bar.NoText")
                : Loc.T("Bar.Status", outcome.Document.Words.Count, source, stopwatch.ElapsedMilliseconds));

            if (!string.IsNullOrEmpty(_pendingQuery)) Search(_pendingQuery);
            else if (!string.IsNullOrEmpty(_bar?.Query)) Search(_bar.Query);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer invocation.
        }
        catch (Exception ex)
        {
            _bar?.SetStatus(Loc.T("Bar.ExtractFailed", ex.Message));
        }
        finally
        {
            _extracting = false;
        }
    }

    private void Search(string query)
    {
        if (_bar is null) return;

        if (_document is null)
        {
            _pendingQuery = query; // extraction still running — replay it when the document lands
            return;
        }

        _pendingQuery = string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            _matches = Array.Empty<Match>();
            _activeIndex = 0;
            _overlays.Clear();
            _bar.SetCounter(0, 0);
            return;
        }

        _matches = _matcher.Find(_document, query);
        _activeIndex = 0;

        _bar.SetCounter(_activeIndex, _matches.Count);
        _overlays.Show(_matches, _activeIndex);

        if (_matches.Count == 0)
        {
            _bar.SetStatus(Loc.T(_extracting ? "Bar.Reading" : "Bar.NoResults"));
        }
        else
        {
            bool fuzzy = _matches.Any(m => !m.IsExact);
            _bar.SetStatus(Loc.T(fuzzy ? "Bar.ResultsFuzzy" : "Bar.Results", _matches.Count));
        }
    }

    private void Navigate(int direction)
    {
        if (_bar is null || _matches.Count == 0) return;

        _activeIndex = (_activeIndex + direction + _matches.Count) % _matches.Count;
        _bar.SetCounter(_activeIndex, _matches.Count);
        _overlays.Show(_matches, _activeIndex);
    }

    private void CopyExtractedText()
    {
        if (_document is null || _document.Document.IsEmpty)
        {
            _bar?.SetStatus(Loc.T("Bar.NothingToCopy"));
            return;
        }

        try
        {
            Clipboard.SetText(_document.Document.RawText);
            _bar?.SetStatus(Loc.T("Bar.Copied", _document.Document.RawText.Length));
        }
        catch (Exception ex)
        {
            _bar?.SetStatus(Loc.T("Bar.CopyFailed", ex.Message));
        }
    }

    private bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        foreach (Window window in Application.Current.Windows)
        {
            if (new WindowInteropHelper(window).Handle == hwnd) return true;
        }
        return false;
    }

    public void Dispose()
    {
        _extraction?.Cancel();
        _hotkey?.Dispose();
        _tray.Dispose();
        _overlays.Dispose();
        _pipeline.Dispose();
        _capture.Dispose();
        _ocrEngine.Dispose();
        _bar?.Close();
    }
}
