namespace ScreenFind.Core.Models;

public enum ExtractionSource
{
    Uia,
    Ocr
}

public static class LanguageTags
{
    public const string Arabic = "ar";
    public const string English = "en";
    public const string Unknown = "unknown";

    /// <summary>Cheap script sniff — enough to tag a word for merge/debug purposes.</summary>
    public static string Detect(string text)
    {
        int arabic = 0, latin = 0;
        foreach (char c in text)
        {
            if (c >= 0x0600 && c <= 0x06FF) arabic++;
            else if (c >= 0x0750 && c <= 0x077F) arabic++;
            else if (c >= 0xFB50 && c <= 0xFEFF) arabic++;
            else if (char.IsLetter(c) && c < 0x0250) latin++;
        }

        if (arabic == 0 && latin == 0) return Unknown;
        return arabic >= latin ? Arabic : English;
    }
}

/// <param name="Text">The word exactly as extracted (not normalized).</param>
/// <param name="ScreenBounds">Physical screen pixels, DPI already accounted for.</param>
public record WordBox(
    string Text,
    Rect ScreenBounds,
    string Language,
    float Confidence,
    ExtractionSource Source);
