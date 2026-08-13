using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenFind.App.Services;
using ScreenFind.Core.Extraction;
using ScreenFind.Core.Interop;
using CoreRect = ScreenFind.Core.Models.Rect;

namespace ScreenFind.App.Windows;

/// <summary>
/// One transparent, click-through window per monitor (spec §5.6). All coordinates arriving here
/// are physical screen pixels; the conversion to WPF device independent pixels happens once,
/// using this monitor's scale factor.
/// </summary>
public partial class OverlayWindow : Window
{
    public OverlayWindow(IntPtr monitor, HighlightTheme theme)
    {
        InitializeComponent();

        Monitor = monitor;
        MonitorBounds = Win32.GetMonitorBounds(monitor);
        DpiScale = Win32.GetMonitorDpiScale(monitor);
        Theme = theme;
    }

    /// <summary>Colours come from the settings file and can change while the app runs.</summary>
    public HighlightTheme Theme { get; set; }

    public IntPtr Monitor { get; }

    public CoreRect MonitorBounds { get; private set; }

    public double DpiScale { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        // Mandatory (spec §5.1): otherwise ScreenFind captures its own highlights and OCRs them.
        if (!AppEnvironment.AllowSelfCapture) Win32.ExcludeFromCapture(hwnd);
        // Mouse clicks must reach the application underneath.
        Win32.MakeClickThrough(hwnd);

        PlaceOverMonitor(hwnd);
    }

    private void PlaceOverMonitor(IntPtr hwnd)
    {
        MonitorBounds = Win32.GetMonitorBounds(Monitor);
        DpiScale = Win32.GetMonitorDpiScale(Monitor);

        // Positioned in physical pixels: WPF's Left/Top would be interpreted in the DIPs of
        // whichever monitor the window currently believes it is on.
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST,
            (int)MonitorBounds.X, (int)MonitorBounds.Y,
            (int)MonitorBounds.Width, (int)MonitorBounds.Height,
            Win32.SWP_NOACTIVATE);
    }

    /// <summary>Draws the highlights whose rectangles fall on this monitor.</summary>
    public void Render(IReadOnlyList<HighlightRect> highlights)
    {
        Surface.Children.Clear();

        foreach (var highlight in highlights)
        {
            var onScreen = highlight.Bounds;
            if (onScreen.Intersect(MonitorBounds).IsEmpty) continue;

            var local = CoordinateMapper.ScreenToMonitorLocal(onScreen, MonitorBounds);
            var dip = CoordinateMapper.ScreenToDip(local, DpiScale);

            var rectangle = new Rectangle
            {
                Width = Math.Max(1, dip.Width),
                Height = Math.Max(1, dip.Height),
                Fill = highlight.IsActive ? Theme.ActiveFill : Theme.Fill,
                RadiusX = 2,
                RadiusY = 2
            };

            if (highlight.IsActive)
            {
                rectangle.Stroke = Theme.ActiveStroke;
                rectangle.StrokeThickness = 1.5;
            }

            Canvas.SetLeft(rectangle, dip.X);
            Canvas.SetTop(rectangle, dip.Y);
            Surface.Children.Add(rectangle);
        }

        Visibility = Surface.Children.Count > 0 ? Visibility.Visible : Visibility.Hidden;
    }

    public void Clear()
    {
        Surface.Children.Clear();
        Visibility = Visibility.Hidden;
    }
}

public readonly record struct HighlightRect(CoreRect Bounds, bool IsActive);
