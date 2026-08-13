using ScreenFind.Core.Models;
using Windows.Graphics.Imaging;

namespace ScreenFind.Core.Capture;

/// <param name="Bitmap">BGRA8 copy of the captured frame. The caller owns it.</param>
/// <param name="SourceBounds">Where the frame sits on the desktop, in physical pixels.</param>
/// <param name="ContentHash">Hash of the pixels, used to skip re-running OCR (spec §5.1).</param>
public sealed record CaptureResult(
    SoftwareBitmap Bitmap,
    Rect SourceBounds,
    IntPtr SourceWindow,
    ulong ContentHash) : IDisposable
{
    public int PixelWidth => Bitmap.PixelWidth;
    public int PixelHeight => Bitmap.PixelHeight;

    public void Dispose() => Bitmap.Dispose();
}

public interface ICaptureService : IDisposable
{
    /// <summary>Captures a single window. Returns null when the window cannot be captured.</summary>
    Task<CaptureResult?> CaptureWindowAsync(IntPtr hwnd, CancellationToken cancellationToken = default);

    /// <summary>Captures the monitor that hosts <paramref name="hwnd"/> (fallback path, spec §5.1).</summary>
    Task<CaptureResult?> CaptureMonitorForWindowAsync(IntPtr hwnd, CancellationToken cancellationToken = default);

    /// <summary>Window capture with an automatic fallback to the whole monitor.</summary>
    Task<CaptureResult?> CaptureAsync(IntPtr hwnd, CancellationToken cancellationToken = default);
}
