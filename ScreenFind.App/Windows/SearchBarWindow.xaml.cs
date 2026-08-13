using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenFind.App.Localization;
using ScreenFind.App.Services;
using ScreenFind.Core.Extraction;
using ScreenFind.Core.Interop;
using CoreRect = ScreenFind.Core.Models.Rect;

namespace ScreenFind.App.Windows;

/// <summary>
/// The floating search bar (spec §5.7). Search is incremental with a 250 ms debounce.
/// </summary>
public partial class SearchBarWindow : Window
{
    private readonly DispatcherTimer _debounce;

    public SearchBarWindow()
    {
        InitializeComponent();

        // The bar mirrors with the interface language; the query box itself stays neutral so
        // Arabic and English queries both read naturally as they are typed.
        FlowDirection = Loc.FlowDirection;
        QueryBox.FlowDirection = Loc.FlowDirection;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            QueryCommitted?.Invoke(QueryBox.Text);
        };

        PreviewKeyDown += OnPreviewKeyDown;
    }

    public event Action<string>? QueryCommitted;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action? CloseRequested;
    public event Action? CopyRequested;

    public string Query => QueryBox.Text;

    /// <summary>Incremental-search debounce, configurable in the settings window.</summary>
    public int DebounceMilliseconds
    {
        get => (int)_debounce.Interval.TotalMilliseconds;
        set => _debounce.Interval = TimeSpan.FromMilliseconds(Math.Clamp(value, 50, 2000));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        // Spec §5.1/§6: the search bar must be invisible to our own capture.
        if (!AppEnvironment.AllowSelfCapture) Win32.ExcludeFromCapture(hwnd);
    }

    /// <summary>Places the bar at the top centre of the monitor the target window is on.</summary>
    public void PositionOver(IntPtr targetWindow)
    {
        IntPtr monitor = targetWindow != IntPtr.Zero && Win32.IsWindow(targetWindow)
            ? Win32.MonitorFromWindow(targetWindow, Win32.MONITOR_DEFAULTTONEAREST)
            : Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTONEAREST);

        var bounds = Win32.GetMonitorBounds(monitor);
        if (bounds.IsEmpty) return;

        double scale = Win32.GetMonitorDpiScale(monitor);
        var sizeDip = new CoreRect(0, 0, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : 64);
        var sizePhysical = CoordinateMapper.DipToScreen(sizeDip, scale);

        int x = (int)(bounds.X + (bounds.Width - sizePhysical.Width) / 2);
        int y = (int)(bounds.Y + 60 * scale);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    public void FocusQuery()
    {
        // The bar has to hold keyboard focus to be typed into; the target window was captured
        // before this window was ever shown, so nothing is lost by activating now.
        Activate();
        QueryBox.Focus();
        QueryBox.SelectAll();
        Keyboard.Focus(QueryBox);
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public void SetCounter(int current, int total)
    {
        CounterText.Text = total == 0 ? string.Empty : $"{current + 1} / {total}";
        NextButton.IsEnabled = total > 0;
        PreviousButton.IsEnabled = total > 0;
    }

    public void ClearQuery()
    {
        _debounce.Stop();
        QueryBox.Clear();
        CounterText.Text = string.Empty;
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseRequested?.Invoke();
                e.Handled = true;
                break;

            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                CommitNow();
                PreviousRequested?.Invoke();
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.F3 when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                CommitNow();
                NextRequested?.Invoke();
                e.Handled = true;
                break;

            case Key.F3:
                CommitNow();
                PreviousRequested?.Invoke();
                e.Handled = true;
                break;

            case Key.C when Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && QueryBox.SelectionLength == 0:
                CopyRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Flushes a pending debounce so Enter never navigates a stale result set.</summary>
    private void CommitNow()
    {
        if (!_debounce.IsEnabled) return;
        _debounce.Stop();
        QueryCommitted?.Invoke(QueryBox.Text);
    }

    private void OnNext(object sender, RoutedEventArgs e) => NextRequested?.Invoke();

    private void OnPrevious(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke();

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && e.OriginalSource is not TextBox) DragMove();
    }
}
