using System.Collections.Concurrent;
using ScreenFind.Core.Capture;
using ScreenFind.Core.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ScreenFind.Core.Extraction;

/// <summary>
/// <see cref="IOcrEngine"/> on top of Windows.Media.Ocr — free, offline, built into the OS.
/// This is the only file in the solution allowed to reference Windows.Media.Ocr (spec §10.2).
/// One <see cref="OcrEngine"/> instance is cached per language because each instance is
/// single-language.
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly ConcurrentDictionary<string, OcrEngine?> _engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<IReadOnlyList<string>> _available;

    public WindowsOcrEngine()
    {
        _available = new Lazy<IReadOnlyList<string>>(() =>
        {
            try
            {
                return OcrEngine.AvailableRecognizerLanguages
                    .Select(language => language.LanguageTag)
                    .ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        });
    }

    public IReadOnlyList<string> AvailableLanguages => _available.Value;

    public bool IsLanguageAvailable(string languageTag) => ResolveTag(languageTag) is not null;

    /// <summary>Maps a short tag such as "ar" onto an installed recognizer such as "ar-SA".</summary>
    public string? ResolveTag(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return null;

        foreach (string available in AvailableLanguages)
        {
            if (string.Equals(available, languageTag, StringComparison.OrdinalIgnoreCase)) return available;
        }

        foreach (string available in AvailableLanguages)
        {
            if (available.StartsWith(languageTag + "-", StringComparison.OrdinalIgnoreCase)) return available;
            if (languageTag.StartsWith(available + "-", StringComparison.OrdinalIgnoreCase)) return available;
        }

        return null;
    }

    public async Task<OcrOutput> RecognizeAsync(SoftwareBitmap bitmap, string languageTag,
        CancellationToken cancellationToken = default)
    {
        var engine = GetEngine(languageTag);
        if (engine is null) return OcrOutput.Empty(languageTag);

        cancellationToken.ThrowIfCancellationRequested();

        SoftwareBitmap input = BitmapPixels.EnsureBgra8(bitmap, out bool converted);
        try
        {
            var result = await engine.RecognizeAsync(input).AsTask(cancellationToken).ConfigureAwait(false);
            return Convert(result, languageTag);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OcrOutput.Empty(languageTag);
        }
        finally
        {
            if (converted) input.Dispose();
        }
    }

    /// <summary>Largest image side the engine accepts; anything bigger must be downscaled first.</summary>
    public static int MaxImageDimension
    {
        get
        {
            try { return (int)OcrEngine.MaxImageDimension; }
            catch (Exception) { return 10_000; }
        }
    }

    private OcrEngine? GetEngine(string languageTag) => _engines.GetOrAdd(languageTag, tag =>
    {
        string? resolved = ResolveTag(tag);
        if (resolved is null) return null;

        try
        {
            return OcrEngine.TryCreateFromLanguage(new Language(resolved));
        }
        catch (Exception)
        {
            return null;
        }
    });

    private static OcrOutput Convert(OcrResult result, string languageTag)
    {
        var lines = new List<OcrTextLine>(result.Lines.Count);

        foreach (var line in result.Lines)
        {
            var words = new List<OcrToken>(line.Words.Count);
            foreach (var word in line.Words)
            {
                var rect = word.BoundingRect;
                words.Add(new OcrToken(word.Text, new Rect(rect.X, rect.Y, rect.Width, rect.Height)));
            }

            if (words.Count > 0) lines.Add(new OcrTextLine(line.Text, words));
        }

        return new OcrOutput(languageTag, lines);
    }

    public void Dispose() => _engines.Clear();
}
