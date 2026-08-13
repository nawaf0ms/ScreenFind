using ScreenFind.Core.Capture;
using ScreenFind.Core.Models;

namespace ScreenFind.Core.Extraction;

/// <summary>
/// Shared state for one extraction attempt. The capture is created lazily so that a successful
/// UI Automation pass never pays for a screen grab (spec §5.2).
/// </summary>
public sealed class ExtractionContext : IDisposable
{
    private readonly Func<CancellationToken, Task<CaptureResult?>> _captureFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CaptureResult? _capture;
    private bool _captureAttempted;

    public ExtractionContext(IntPtr window, Func<CancellationToken, Task<CaptureResult?>> captureFactory)
    {
        Window = window;
        _captureFactory = captureFactory;
    }

    public IntPtr Window { get; }

    public bool HasCapture => _capture is not null;

    public async Task<CaptureResult?> GetCaptureAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_captureAttempted) return _capture;
            _captureAttempted = true;
            _capture = await _captureFactory(cancellationToken).ConfigureAwait(false);
            return _capture;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _capture = null;
        _gate.Dispose();
    }
}

public interface ITextExtractor
{
    string Name { get; }

    /// <summary>True when the extractor needs a bitmap; false for UI Automation.</summary>
    bool RequiresCapture { get; }

    Task<ExtractedDocument> ExtractAsync(ExtractionContext context, CancellationToken cancellationToken = default);
}
