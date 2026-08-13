using ScreenFind.Core.Models;

namespace ScreenFind.Core.Extraction;

/// <summary>
/// Where the captured image sits on the desktop, and how much it was scaled before OCR.
/// </summary>
/// <param name="SourceBounds">Captured region in physical screen pixels (virtual desktop space).</param>
/// <param name="PreprocessScale">Upscale factor applied before OCR (spec §5.3 uses 2.0).</param>
public readonly record struct CaptureGeometry(Rect SourceBounds, double PreprocessScale)
{
    public static CaptureGeometry Identity => new(new Rect(0, 0, 0, 0), 1.0);
}

/// <summary>
/// Coordinate plumbing (spec §5.3): OCR boxes are relative to the ×2 upscaled image, so they
/// are divided by the scale factor and offset by the capture origin. Everything downstream of
/// this class is in physical screen pixels; the WPF overlay is the only place that converts to
/// device independent pixels.
///
/// Coordinate bugs are the single largest source of defects in this kind of app, so this file
/// stays pure and is covered by unit tests.
/// </summary>
public static class CoordinateMapper
{
    /// <summary>Image space (upscaled capture) -> physical screen pixels.</summary>
    public static Rect ImageToScreen(Rect imageRect, CaptureGeometry geometry)
    {
        double scale = geometry.PreprocessScale <= 0 ? 1.0 : geometry.PreprocessScale;
        return new Rect(
            geometry.SourceBounds.X + imageRect.X / scale,
            geometry.SourceBounds.Y + imageRect.Y / scale,
            imageRect.Width / scale,
            imageRect.Height / scale);
    }

    /// <summary>Physical screen pixels -> WPF device independent pixels for a given monitor scale.</summary>
    public static Rect ScreenToDip(Rect screenRect, double dpiScale)
    {
        if (dpiScale <= 0) dpiScale = 1.0;
        return new Rect(
            screenRect.X / dpiScale,
            screenRect.Y / dpiScale,
            screenRect.Width / dpiScale,
            screenRect.Height / dpiScale);
    }

    /// <summary>DIP -> physical screen pixels (window placement).</summary>
    public static Rect DipToScreen(Rect dipRect, double dpiScale)
    {
        if (dpiScale <= 0) dpiScale = 1.0;
        return new Rect(
            dipRect.X * dpiScale,
            dipRect.Y * dpiScale,
            dipRect.Width * dpiScale,
            dipRect.Height * dpiScale);
    }

    /// <summary>Physical screen pixels -> coordinates local to a monitor's origin.</summary>
    public static Rect ScreenToMonitorLocal(Rect screenRect, Rect monitorBounds)
        => screenRect.Offset(-monitorBounds.X, -monitorBounds.Y);

    /// <summary>DPI scale factor from a raw DPI value (96 DPI = 1.0 = 100%).</summary>
    public static double DpiScale(double dpi) => dpi <= 0 ? 1.0 : dpi / 96.0;
}
