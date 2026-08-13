using ScreenFind.Core.Models;
using Windows.Graphics.Imaging;

namespace ScreenFind.Core.Extraction;

public sealed record OcrExtractionOptions
{
    public static readonly OcrExtractionOptions Default = new();

    /// <summary>Arabic and English matter equally (spec §2), so both engines always run.</summary>
    public IReadOnlyList<string> Languages { get; init; } = new[] { "ar", "en" };

    public PreprocessOptions Preprocess { get; init; } = PreprocessOptions.Default;

    /// <summary>Two detections of the same region overlapping by more than this are duplicates.</summary>
    public double DuplicateOverlap { get; init; } = 0.5;
}

/// <summary>
/// Tier 2 (spec §5.3): preprocess, run one engine per language in parallel, then merge.
///
/// Deviation from the letter of the spec, on purpose: de-duplication happens per *line* rather
/// than per word. Windows OCR returns words in reading order within a line, and Arabic is
/// right-to-left — merging individual words from two engines would scramble that order. Picking
/// the better line (the engine whose script matches the line's content) keeps reading order
/// intact while achieving the same goal: one detection per screen region.
/// </summary>
public sealed class OcrTextExtractor : ITextExtractor
{
    private readonly IOcrEngine _engine;
    private readonly OcrExtractionOptions _options;

    public OcrTextExtractor(IOcrEngine engine, OcrExtractionOptions? options = null)
    {
        _engine = engine;
        _options = options ?? OcrExtractionOptions.Default;
    }

    public string Name => "OCR";

    public bool RequiresCapture => true;

    /// <summary>Languages that were requested but are not installed (used for the UI hint).</summary>
    public IReadOnlyList<string> MissingLanguages =>
        _options.Languages.Where(tag => !_engine.IsLanguageAvailable(tag)).ToArray();

    public async Task<ExtractedDocument> ExtractAsync(ExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        var capture = await context.GetCaptureAsync(cancellationToken).ConfigureAwait(false);
        if (capture is null) return ExtractedDocument.Empty;

        using var prepared = ImagePreprocessor.Prepare(capture.Bitmap, _options.Preprocess);
        var geometry = new CaptureGeometry(capture.SourceBounds, prepared.Scale);

        var outputs = await RecognizeAllAsync(prepared.Bitmap, cancellationToken).ConfigureAwait(false);
        return Merge(outputs, geometry);
    }

    /// <summary>Runs the language engines concurrently (spec §5.3).</summary>
    private async Task<IReadOnlyList<OcrOutput>> RecognizeAllAsync(SoftwareBitmap bitmap,
        CancellationToken cancellationToken)
    {
        var tasks = _options.Languages
            .Where(_engine.IsLanguageAvailable)
            .Select(tag => _engine.RecognizeAsync(bitmap, tag, cancellationToken))
            .ToArray();

        if (tasks.Length == 0) return Array.Empty<OcrOutput>();

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private ExtractedDocument Merge(IReadOnlyList<OcrOutput> outputs, CaptureGeometry geometry)
    {
        var candidates = new List<ScoredLine>();

        foreach (var output in outputs)
        {
            string script = ScriptOf(output.LanguageTag);
            foreach (var line in output.Lines)
            {
                if (line.Words.Count == 0) continue;

                var bounds = Rect.Empty;
                foreach (var word in line.Words) bounds = bounds.Union(word.Bounds);
                if (bounds.IsEmpty) continue;

                var (weight, ratio) = ScriptMatch(line.Text, script);
                candidates.Add(new ScoredLine(line, bounds, weight, ratio, output.LanguageTag));
            }
        }

        // Best-scoring line wins its region; overlapping detections from the other engine are dropped.
        candidates.Sort((a, b) =>
        {
            int byWeight = b.Weight.CompareTo(a.Weight);
            if (byWeight != 0) return byWeight;
            int byRatio = b.Ratio.CompareTo(a.Ratio);
            return byRatio != 0 ? byRatio : b.Line.Text.Length.CompareTo(a.Line.Text.Length);
        });

        var accepted = new List<ScoredLine>();
        foreach (var candidate in candidates)
        {
            bool duplicate = accepted.Any(a => a.Bounds.OverlapRatio(candidate.Bounds) > _options.DuplicateOverlap);
            if (!duplicate) accepted.Add(candidate);
        }

        // Page reading order: top to bottom, then left to right.
        accepted.Sort((a, b) => ReadingOrder.CompareLines(a.Bounds, b.Bounds));

        var lines = new List<IReadOnlyList<WordBox>>(accepted.Count);
        foreach (var line in accepted)
        {
            // Logical (not visual) word order — mandatory for Arabic.
            var orderedWords = ReadingOrder.ToLogicalOrder(line.Line);
            var words = new List<WordBox>(orderedWords.Count);
            foreach (var word in orderedWords)
            {
                if (string.IsNullOrWhiteSpace(word.Text)) continue;
                words.Add(new WordBox(
                    word.Text,
                    CoordinateMapper.ImageToScreen(word.Bounds, geometry),
                    LanguageTags.Detect(word.Text),
                    (float)line.Ratio,
                    ExtractionSource.Ocr));
            }
            if (words.Count > 0) lines.Add(words);
        }

        return ExtractedDocument.FromLines(lines);
    }

    private static string ScriptOf(string languageTag)
        => languageTag.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            ? LanguageTags.Arabic
            : LanguageTags.English;

    /// <summary>
    /// How much real text an engine found in its own script.
    ///
    /// The ratio alone is not enough: running the English engine over an Arabic line yields short
    /// Latin gibberish ("QuxoJI r O") whose ratio is a perfect 1.0. Weighting the ratio by the
    /// number of matching letters makes the engine that actually read the region win, because it
    /// returns far more characters.
    /// </summary>
    private static (double Weight, double Ratio) ScriptMatch(string text, string script)
    {
        int matching = 0, letters = 0;
        foreach (char c in text)
        {
            bool arabic = (c >= 0x0600 && c <= 0x06FF) || (c >= 0xFB50 && c <= 0xFEFF);
            bool latin = c < 0x0250 && char.IsLetter(c);
            if (!arabic && !latin) continue;

            letters++;
            if ((script == LanguageTags.Arabic && arabic) || (script == LanguageTags.English && latin)) matching++;
        }

        if (letters == 0) return (text.Trim().Length * 0.5, 0.5); // digits/punctuation only — no preference

        double ratio = matching / (double)letters;
        return (matching * ratio, ratio);
    }

    private readonly record struct ScoredLine(
        OcrTextLine Line, Rect Bounds, double Weight, double Ratio, string LanguageTag);
}
