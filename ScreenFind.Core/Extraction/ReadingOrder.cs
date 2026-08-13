using ScreenFind.Core.Models;

namespace ScreenFind.Core.Extraction;

/// <summary>
/// Windows.Media.Ocr returns the words of a line in *visual* order (left to right by x), which
/// is the reverse of logical order for Arabic. Phrase search, clipboard output and RawText all
/// depend on logical order, so every OCR line is re-ordered here before it becomes WordBoxes.
///
/// Measured on the phase 0 samples: without this, Arabic word-error-rate accuracy is ~3% while
/// bag-of-words recall is ~65% — the words are right, only their order is wrong.
/// </summary>
public static class ReadingOrder
{
    /// <summary>True when the text is predominantly right-to-left.</summary>
    public static bool IsRightToLeft(string text)
    {
        int rtl = 0, ltr = 0;
        foreach (char c in text)
        {
            if ((c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFEFF)) rtl++;
            else if (c < 0x0250 && char.IsLetter(c)) ltr++;
        }
        return rtl > ltr;
    }

    /// <summary>Words of a line in logical reading order.</summary>
    public static IReadOnlyList<OcrToken> ToLogicalOrder(OcrTextLine line)
    {
        if (line.Words.Count <= 1) return line.Words;

        bool rightToLeft = IsRightToLeft(line.Text);
        var ordered = line.Words.ToList();

        ordered.Sort((a, b) => rightToLeft
            ? b.Bounds.X.CompareTo(a.Bounds.X)
            : a.Bounds.X.CompareTo(b.Bounds.X));

        return ordered;
    }

    public static string ToLogicalText(OcrTextLine line)
        => string.Join(' ', ToLogicalOrder(line).Select(w => w.Text));

    /// <summary>Sorts whole lines into page reading order: top to bottom, then by x.</summary>
    public static int CompareLines(Rect a, Rect b)
    {
        if (a.VerticalOverlapRatio(b) >= 0.5) return a.X.CompareTo(b.X);
        return a.Y.CompareTo(b.Y);
    }
}
