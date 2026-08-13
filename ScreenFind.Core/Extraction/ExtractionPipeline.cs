using System.Diagnostics;
using ScreenFind.Core.Capture;
using ScreenFind.Core.Models;

namespace ScreenFind.Core.Extraction;

public sealed record ExtractionOutcome(
    ExtractedDocument Document,
    string ExtractorName,
    TimeSpan Duration,
    bool FromCache)
{
    public static ExtractionOutcome Empty(TimeSpan duration) =>
        new(ExtractedDocument.Empty, "none", duration, false);

    public bool IsEmpty => Document.IsEmpty;
}

public sealed record ExtractionPipelineOptions
{
    public static readonly ExtractionPipelineOptions Default = new();

    /// <summary>"Reasonable" text for a tier to be trusted: more than 20 non-space characters (spec §5.2).</summary>
    public int MinAcceptableCharacters { get; init; } = 20;

    /// <summary>Reuse the previous extraction when the pixels did not change (spec §5.1).</summary>
    public bool EnableCache { get; init; } = true;
}

/// <summary>
/// Runs the extraction tiers in order and stops at the first one that produces usable text:
/// UI Automation first (exact and fast), OCR as the fallback.
/// </summary>
public sealed class ExtractionPipeline : IDisposable
{
    private readonly ICaptureService _capture;
    private readonly IReadOnlyList<ITextExtractor> _tiers;
    private readonly ExtractionPipelineOptions _options;
    private readonly bool _ownsCapture;

    private IntPtr _cachedWindow;
    private ulong _cachedHash;
    private ExtractedDocument? _cachedDocument;
    private string _cachedExtractor = string.Empty;

    /// <param name="ownsCapture">
    /// False when the capture service outlives the pipeline — the app rebuilds its pipeline
    /// whenever extraction settings change, but keeps one D3D device alive throughout.
    /// </param>
    public ExtractionPipeline(ICaptureService capture, IReadOnlyList<ITextExtractor> tiers,
        ExtractionPipelineOptions? options = null, bool ownsCapture = true)
    {
        _capture = capture;
        _tiers = tiers;
        _options = options ?? ExtractionPipelineOptions.Default;
        _ownsCapture = ownsCapture;
    }

    public async Task<ExtractionOutcome> ExtractAsync(IntPtr window, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var context = new ExtractionContext(window, ct => _capture.CaptureAsync(window, ct));

        ExtractionOutcome? best = null;

        foreach (var tier in _tiers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CaptureResult? capture = null;
            if (tier.RequiresCapture)
            {
                capture = await context.GetCaptureAsync(cancellationToken).ConfigureAwait(false);
                if (capture is null) continue;

                if (TryGetCached(window, capture.ContentHash, out var cached, out string extractorName))
                {
                    return new ExtractionOutcome(cached, extractorName, stopwatch.Elapsed, true);
                }
            }

            ExtractedDocument document;
            try
            {
                document = await tier.ExtractAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                continue; // a broken tier must never take the pipeline down
            }

            var outcome = new ExtractionOutcome(document, tier.Name, stopwatch.Elapsed, false);
            if (best is null || document.Words.Count > best.Document.Words.Count) best = outcome;

            if (IsAcceptable(document))
            {
                if (capture is not null) Cache(window, capture.ContentHash, document, tier.Name);
                return outcome;
            }
        }

        return best ?? ExtractionOutcome.Empty(stopwatch.Elapsed);
    }

    /// <summary>Spec §5.2: a tier counts as successful when it returns more than 20 non-space characters.</summary>
    public bool IsAcceptable(ExtractedDocument document)
    {
        if (document.IsEmpty) return false;

        int count = 0;
        foreach (char c in document.RawText)
        {
            if (!char.IsWhiteSpace(c) && ++count > _options.MinAcceptableCharacters) return true;
        }
        return false;
    }

    private bool TryGetCached(IntPtr window, ulong hash, out ExtractedDocument document, out string extractorName)
    {
        document = ExtractedDocument.Empty;
        extractorName = string.Empty;

        if (!_options.EnableCache || _cachedDocument is null || hash == 0) return false;
        if (_cachedWindow != window || _cachedHash != hash) return false;

        document = _cachedDocument;
        extractorName = _cachedExtractor;
        return true;
    }

    private void Cache(IntPtr window, ulong hash, ExtractedDocument document, string extractorName)
    {
        if (!_options.EnableCache || hash == 0) return;

        _cachedWindow = window;
        _cachedHash = hash;
        _cachedDocument = document;
        _cachedExtractor = extractorName;
    }

    public void InvalidateCache()
    {
        _cachedDocument = null;
        _cachedHash = 0;
        _cachedWindow = IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var tier in _tiers)
        {
            (tier as IDisposable)?.Dispose();
        }

        if (_ownsCapture) _capture.Dispose();
    }
}
