using System.Runtime.InteropServices;
using ScreenFind.Core.Interop;
using ScreenFind.Core.Models;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace ScreenFind.Core.Capture;

/// <summary>
/// Windows.Graphics.Capture based screen grab (spec §3): fast, flicker free, no admin rights.
/// The D3D device is created once and reused; a frame pool is created per capture because its
/// size is tied to the captured item.
/// </summary>
public sealed class GraphicsCaptureService : ICaptureService
{
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private readonly TimeSpan _frameTimeout = TimeSpan.FromSeconds(2);
    private readonly object _deviceLock = new();

    private IDirect3DDevice? _device;
    private IntPtr _d3dDevicePtr;
    private IntPtr _d3dContextPtr;
    private bool _disposed;

    public static bool IsSupported
    {
        get
        {
            try { return GraphicsCaptureSession.IsSupported(); }
            catch { return false; }
        }
    }

    public async Task<CaptureResult?> CaptureAsync(IntPtr hwnd, CancellationToken cancellationToken = default)
    {
        var result = await CaptureWindowAsync(hwnd, cancellationToken).ConfigureAwait(false);
        if (result is not null) return result;

        // Spec §5.1: fall back to the whole monitor when the window cannot be captured.
        return await CaptureMonitorForWindowAsync(hwnd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CaptureResult?> CaptureWindowAsync(IntPtr hwnd, CancellationToken cancellationToken = default)
    {
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd)) return null;

        try
        {
            var item = CreateItemForWindow(hwnd);
            if (item is null) return null;

            var bitmap = await CaptureItemAsync(item, cancellationToken).ConfigureAwait(false);
            if (bitmap is null) return null;

            var origin = ResolveWindowOrigin(hwnd, bitmap.PixelWidth, bitmap.PixelHeight);
            var bounds = new Rect(origin.X, origin.Y, bitmap.PixelWidth, bitmap.PixelHeight);
            return new CaptureResult(bitmap, bounds, hwnd, ContentHash.Compute(bitmap));
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<CaptureResult?> CaptureMonitorForWindowAsync(IntPtr hwnd, CancellationToken cancellationToken = default)
    {
        IntPtr monitor = hwnd != IntPtr.Zero && Win32.IsWindow(hwnd)
            ? Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST)
            : Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTONEAREST);

        if (monitor == IntPtr.Zero) return null;

        try
        {
            var item = CreateItemForMonitor(monitor);
            if (item is null) return null;

            var bitmap = await CaptureItemAsync(item, cancellationToken).ConfigureAwait(false);
            if (bitmap is null) return null;

            var monitorBounds = Win32.GetMonitorBounds(monitor);
            var bounds = new Rect(monitorBounds.X, monitorBounds.Y, bitmap.PixelWidth, bitmap.PixelHeight);
            return new CaptureResult(bitmap, bounds, hwnd, ContentHash.Compute(bitmap));
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Windows reports two different rectangles for a window: GetWindowRect includes the
    /// invisible resize border, DWM's extended frame bounds do not. Whichever one matches the
    /// captured texture is the one the frame is aligned to — picking the wrong one shifts every
    /// highlight by several pixels.
    /// </summary>
    private static (double X, double Y) ResolveWindowOrigin(IntPtr hwnd, int capturedWidth, int capturedHeight)
    {
        var windowRect = Win32.GetWindowBounds(hwnd);
        var frameBounds = Win32.GetExtendedFrameBounds(hwnd);

        double windowError = SizeError(windowRect, capturedWidth, capturedHeight);
        double frameError = frameBounds.IsEmpty ? double.MaxValue : SizeError(frameBounds, capturedWidth, capturedHeight);

        return frameError < windowError
            ? (frameBounds.X, frameBounds.Y)
            : (windowRect.X, windowRect.Y);

        static double SizeError(Rect rect, int width, int height)
            => Math.Abs(rect.Width - width) + Math.Abs(rect.Height - height);
    }

    private static GraphicsCaptureItem? CreateItemForWindow(IntPtr hwnd)
    {
        var interop = GetCaptureItemInterop();
        Guid iid = IID_IGraphicsCaptureItem;
        IntPtr itemPtr = interop.CreateForWindow(hwnd, ref iid);
        return FromAbi(itemPtr);
    }

    private static GraphicsCaptureItem? CreateItemForMonitor(IntPtr monitor)
    {
        var interop = GetCaptureItemInterop();
        Guid iid = IID_IGraphicsCaptureItem;
        IntPtr itemPtr = interop.CreateForMonitor(monitor, ref iid);
        return FromAbi(itemPtr);
    }

    private static GraphicsCaptureItem? FromAbi(IntPtr itemPtr)
    {
        if (itemPtr == IntPtr.Zero) return null;
        try
        {
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }

    private static IGraphicsCaptureItemInterop GetCaptureItemInterop()
    {
        IntPtr hstring = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;
        try
        {
            const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, runtimeClass.Length, out hstring));

            Guid iid = typeof(IGraphicsCaptureItemInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, ref iid, out factory));

            return (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
        }
        finally
        {
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
        }
    }

    private async Task<SoftwareBitmap?> CaptureItemAsync(GraphicsCaptureItem item, CancellationToken cancellationToken)
    {
        var device = EnsureDevice();
        var completion = new TaskCompletionSource<SoftwareBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Free-threaded so no DispatcherQueue is required — the core stays UI-framework agnostic.
        var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        GraphicsCaptureSession? session = null;

        void OnFrameArrived(Direct3D11CaptureFramePool pool, object _)
        {
            try
            {
                using var frame = pool.TryGetNextFrame();
                if (frame is null) return;

                var bitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask().GetAwaiter().GetResult();
                completion.TrySetResult(bitmap);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        try
        {
            framePool.FrameArrived += OnFrameArrived;
            session = framePool.CreateCaptureSession(item);
            TryDisableCaptureDecorations(session);
            session.StartCapture();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_frameTimeout);

            return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
            session?.Dispose();
            framePool.Dispose();
        }
    }

    private static void TryDisableCaptureDecorations(GraphicsCaptureSession session)
    {
        try { session.IsCursorCaptureEnabled = false; }
        catch (Exception) { /* older builds */ }
    }

    private IDirect3DDevice EnsureDevice()
    {
        lock (_deviceLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_device is not null) return _device;

            int hr = Win32.D3D11CreateDevice(
                IntPtr.Zero, Win32.D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
                Win32.D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, Win32.D3D11_SDK_VERSION,
                out _d3dDevicePtr, out _, out _d3dContextPtr);

            if (hr < 0)
            {
                // No GPU (RDP, VM, headless) — WARP still works.
                hr = Win32.D3D11CreateDevice(
                    IntPtr.Zero, Win32.D3D_DRIVER_TYPE_WARP, IntPtr.Zero,
                    Win32.D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, Win32.D3D11_SDK_VERSION,
                    out _d3dDevicePtr, out _, out _d3dContextPtr);
            }
            Marshal.ThrowExceptionForHR(hr);

            IntPtr dxgiDevice = IntPtr.Zero;
            IntPtr inspectable = IntPtr.Zero;
            try
            {
                Guid iid = Win32.IID_IDXGIDevice;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(_d3dDevicePtr, ref iid, out dxgiDevice));
                Marshal.ThrowExceptionForHR(Win32.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable));

                _device = WinRT.MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);
                return _device;
            }
            finally
            {
                if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
                if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
            }
        }
    }

    public void Dispose()
    {
        lock (_deviceLock)
        {
            if (_disposed) return;
            _disposed = true;

            _device?.Dispose();
            _device = null;

            if (_d3dContextPtr != IntPtr.Zero)
            {
                Marshal.Release(_d3dContextPtr);
                _d3dContextPtr = IntPtr.Zero;
            }

            if (_d3dDevicePtr != IntPtr.Zero)
            {
                Marshal.Release(_d3dDevicePtr);
                _d3dDevicePtr = IntPtr.Zero;
            }
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }
}
