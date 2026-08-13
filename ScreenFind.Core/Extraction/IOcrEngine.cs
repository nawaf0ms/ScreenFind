using ScreenFind.Core.Models;
using Windows.Graphics.Imaging;

namespace ScreenFind.Core.Extraction;

/// <param name="Bounds">Box in image coordinates (i.e. in the preprocessed bitmap's space).</param>
public sealed record OcrToken(string Text, Rect Bounds);

public sealed record OcrTextLine(string Text, IReadOnlyList<OcrToken> Words);

public sealed record OcrOutput(string LanguageTag, IReadOnlyList<OcrTextLine> Lines)
{
    public static OcrOutput Empty(string languageTag) => new(languageTag, Array.Empty<OcrTextLine>());

    public int WordCount => Lines.Sum(l => l.Words.Count);
}

/// <summary>
/// The one and only OCR seam (spec §3): swapping Windows OCR for PaddleOCR or Tesseract must
/// cost no more than adding one class. Nothing from Windows.Media.Ocr may cross this interface.
/// </summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>BCP-47 tags the engine can actually recognise right now.</summary>
    IReadOnlyList<string> AvailableLanguages { get; }

    bool IsLanguageAvailable(string languageTag);

    /// <summary>Runs recognition. Returns an empty result if the language is unavailable.</summary>
    Task<OcrOutput> RecognizeAsync(SoftwareBitmap bitmap, string languageTag, CancellationToken cancellationToken = default);
}

public static class OcrLanguageHelp
{
    /// <summary>Shown when a language pack is missing (spec §5.3).</summary>
    public const string InstallInstructions =
        "Settings → Time & Language → Language & region → <language> → Options → Basic typing / OCR";

    public static string MissingLanguageMessage(string languageTag) =>
        $"OCR language '{languageTag}' is not installed. Install it from: {InstallInstructions}";
}
