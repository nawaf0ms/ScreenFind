using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenFind.App.Localization;
using ScreenFind.App.Services;
using ScreenFind.Core.Configuration;
using ScreenFind.Core.Extraction;
using ScreenFind.Core.Interop;

namespace ScreenFind.App.Windows;

/// <summary>
/// Phase 5 settings screen: language, hotkey, colours, enabled languages, preprocessing and
/// matching. Everything is applied immediately on save — nothing requires a restart.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly IReadOnlyList<string> _availableLanguages;
    private readonly List<CheckBox> _languageBoxes = new();

    /// <summary>The size the layout was designed for, in DIPs, captured before WPF touches it.</summary>
    private readonly double _designWidth;
    private readonly double _designHeight;

    private HotkeySettings _hotkey;

    public SettingsWindow(ScreenFindSettings settings, IReadOnlyList<string> availableLanguages)
    {
        InitializeComponent();

        _designWidth = Width;
        _designHeight = Height;

        FlowDirection = Loc.FlowDirection;
        Icon = TrayIcon.LoadApplicationImage();
        CreditRun.Text = Loc.T("Settings.Credit", AppInfo.Version) + " ";
        // WPF applies its own frame size during Show, after Loaded — so the correction has to run
        // once more when the first render is complete, otherwise it is overwritten.
        Loaded += (_, _) => ApplyPhysicalPlacement();
        ContentRendered += (_, _) =>
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(ApplyPhysicalPlacement));

        _availableLanguages = availableLanguages;
        _hotkey = settings.Hotkey;

        PopulateChoices();
        Load(settings);
    }

    /// <summary>The saved settings, or null when the user cancelled.</summary>
    public ScreenFindSettings? Result { get; private set; }

    /// <summary>
    /// Sizes and centres the window in physical pixels, after WPF has done its own (untrustworthy)
    /// sizing: the tray icon's WinForms initialisation can leave the thread on a 96 DPI context,
    /// which makes WPF build a frame for an unscaled display while rendering scaled content into
    /// it. Also guarantees the dialog never grows past the monitor's work area.
    /// </summary>
    private void ApplyPhysicalPlacement()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        IntPtr monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        var work = Win32.GetMonitorWorkArea(monitor);
        if (work.IsEmpty) return;

        double scale = Win32.GetMonitorDpiScale(monitor);

        int width = (int)Math.Min(_designWidth * scale, work.Width - 40);
        int height = (int)Math.Min(_designHeight * scale, work.Height - 40);
        int x = (int)(work.X + (work.Width - width) / 2);
        int y = (int)(work.Y + (work.Height - height) / 2);

        // Clearing the DIP sizes stops WPF from re-applying them on top of this placement with
        // its own (wrong) DPI factor.
        Width = double.NaN;
        Height = double.NaN;

        Win32.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    private void PopulateChoices()
    {
        Add(LanguageBox, Loc.T("Settings.Language.Auto"), UiLanguage.Auto);
        Add(LanguageBox, Loc.T("Settings.Language.Arabic"), UiLanguage.Arabic);
        Add(LanguageBox, Loc.T("Settings.Language.English"), UiLanguage.English);

        foreach (double scale in new[] { 1.0, 1.5, 2.0, 3.0 })
            Add(ScaleBox, "×" + scale.ToString("0.0", CultureInfo.InvariantCulture), scale);

        foreach (var mode in Enum.GetValues<ResampleMode>())
            Add(ResampleBox, mode.ToString(), mode);

        foreach (int max in new[] { 20, 50, 100, 200 })
            Add(MaxResultsBox, max.ToString(CultureInfo.InvariantCulture), max);

        foreach (int debounce in new[] { 100, 150, 250, 400, 600 })
            Add(DebounceBox, debounce.ToString(CultureInfo.InvariantCulture), debounce);

        // One checkbox per recognizer language actually installed on this machine.
        var panel = new StackPanel();
        var installed = _availableLanguages.Count > 0 ? _availableLanguages : new[] { "ar", "en" };
        foreach (string tag in installed)
        {
            var box = new CheckBox
            {
                Content = $"{tag}  ({DescribeLanguage(tag)})",
                Tag = ShortTag(tag),
                Margin = new Thickness(0, 0, 0, 6)
            };
            _languageBoxes.Add(box);
            panel.Children.Add(box);
        }
        LanguageList.Content = panel;

        LanguageHint.Text = _availableLanguages.Count == 0
            ? Loc.T("Settings.NoOcrEngines", OcrLanguageHelp.InstallInstructions)
            : Loc.T("Settings.OcrHint", OcrLanguageHelp.InstallInstructions);
    }

    private static void Add(ComboBox box, string label, object tag)
        => box.Items.Add(new ComboBoxItem { Content = label, Tag = tag });

    private static string DescribeLanguage(string tag) => ShortTag(tag).ToLowerInvariant() switch
    {
        "ar" => Loc.T("Settings.Language.Arabic"),
        "en" => Loc.T("Settings.Language.English"),
        _ => tag
    };

    private static string ShortTag(string languageTag)
    {
        int dash = languageTag.IndexOf('-');
        return dash > 0 ? languageTag[..dash] : languageTag;
    }

    private void Load(ScreenFindSettings settings)
    {
        _hotkey = settings.Hotkey;
        HotkeyBox.Text = _hotkey.Display;

        UseUiaBox.IsChecked = settings.UseUiAutomation;
        GrayscaleBox.IsChecked = settings.Grayscale;
        FuzzyBox.IsChecked = settings.EnableFuzzy;
        StartupBox.IsChecked = settings.StartWithWindows;

        foreach (var box in _languageBoxes)
            box.IsChecked = settings.OcrLanguages.Contains((string)box.Tag, StringComparer.OrdinalIgnoreCase);

        Select(LanguageBox, settings.Language);
        Select(ScaleBox, settings.PreprocessScale);
        Select(ResampleBox, settings.ResampleMode);
        Select(MaxResultsBox, settings.MaxResults);
        Select(DebounceBox, settings.SearchDebounceMs);

        SimilaritySlider.Value = settings.MinSimilarity;
        SimilarityText.Text = settings.MinSimilarity.ToString("0.00", CultureInfo.InvariantCulture);
        SimilaritySlider.IsEnabled = settings.EnableFuzzy;

        HighlightColorBox.Text = settings.HighlightColor;
        ActiveColorBox.Text = settings.ActiveHighlightColor;
        BorderColorBox.Text = settings.ActiveHighlightBorderColor;

        StatusText.Text = Loc.T("Settings.File", ScreenFindSettings.FilePath);
        StatusText.ToolTip = ScreenFindSettings.FilePath;
    }

    private static void Select(ComboBox box, object value)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (Equals(item.Tag, value))
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private static T Selected<T>(ComboBox box, T fallback)
        => box.SelectedItem is ComboBoxItem { Tag: T value } ? value : fallback;

    /// <summary>Opens the project page in the default browser — only ever on the user's click.</summary>
    private void OnOpenLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // No browser association — nothing worth interrupting the user for.
        }
        e.Handled = true;
    }

    private void OnHotkeyFocus(object sender, RoutedEventArgs e) => HotkeyBox.SelectAll();

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            HotkeyBox.Text = "…";
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var candidate = new HotkeySettings
        {
            Control = modifiers.HasFlag(ModifierKeys.Control),
            Shift = modifiers.HasFlag(ModifierKeys.Shift),
            Alt = modifiers.HasFlag(ModifierKeys.Alt),
            Windows = modifiers.HasFlag(ModifierKeys.Windows),
            Key = key.ToString()
        };

        if (!candidate.HasModifier)
        {
            HotkeyBox.Text = _hotkey.Display;
            StatusText.Text = Loc.T("Settings.Hotkey.NeedModifier");
            return;
        }

        _hotkey = candidate;
        HotkeyBox.Text = candidate.Display;
        StatusText.Text = string.Empty;
    }

    private void OnSimilarityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SimilarityText is not null)
            SimilarityText.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void OnFuzzyToggled(object sender, RoutedEventArgs e)
    {
        if (SimilaritySlider is not null) SimilaritySlider.IsEnabled = FuzzyBox.IsChecked == true;
    }

    private void OnColorChanged(object sender, TextChangedEventArgs e)
    {
        Preview(HighlightColorBox, HighlightPreview);
        Preview(ActiveColorBox, ActivePreview);
        Preview(BorderColorBox, BorderPreview);
    }

    private static void Preview(TextBox source, Border target)
    {
        if (target is null) return;
        target.Background = TryParseColor(source.Text, out var brush) ? brush : Brushes.Transparent;
    }

    private static bool TryParseColor(string text, out SolidColorBrush brush)
    {
        brush = Brushes.Transparent;
        try
        {
            if (ColorConverter.ConvertFromString(text) is Color color)
            {
                brush = new SolidColorBrush(color);
                return true;
            }
        }
        catch (Exception)
        {
            // invalid while the user is still typing
        }
        return false;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        foreach (var (box, label) in new[]
                 {
                     (HighlightColorBox, Loc.T("Settings.ColorAll")),
                     (ActiveColorBox, Loc.T("Settings.ColorActive")),
                     (BorderColorBox, Loc.T("Settings.ColorBorder"))
                 })
        {
            if (!TryParseColor(box.Text, out _))
            {
                StatusText.Text = Loc.T("Settings.ColorInvalid", label);
                box.Focus();
                return;
            }
        }

        var languages = _languageBoxes
            .Where(box => box.IsChecked == true)
            .Select(box => (string)box.Tag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (languages.Count == 0)
        {
            StatusText.Text = Loc.T("Settings.PickOneLanguage");
            return;
        }

        Result = new ScreenFindSettings
        {
            Language = Selected(LanguageBox, UiLanguage.Auto),
            Hotkey = _hotkey,
            UseUiAutomation = UseUiaBox.IsChecked == true,
            OcrLanguages = languages,
            PreprocessScale = Selected(ScaleBox, 2.0),
            ResampleMode = Selected(ResampleBox, ResampleMode.Bicubic),
            Grayscale = GrayscaleBox.IsChecked == true,
            EnableFuzzy = FuzzyBox.IsChecked == true,
            MinSimilarity = Math.Round(SimilaritySlider.Value, 2),
            MaxResults = Selected(MaxResultsBox, 50),
            SearchDebounceMs = Selected(DebounceBox, 250),
            StartWithWindows = StartupBox.IsChecked == true,
            HighlightColor = HighlightColorBox.Text.Trim(),
            ActiveHighlightColor = ActiveColorBox.Text.Trim(),
            ActiveHighlightBorderColor = BorderColorBox.Text.Trim(),
            Configured = true
        };

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
        Close();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        Load(ScreenFindSettings.Defaults);
        StatusText.Text = Loc.T("Settings.ResetDone");
    }
}
