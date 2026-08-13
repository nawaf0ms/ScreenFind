namespace ScreenFind.Core.Models;

/// <param name="Words">All words, in reading order.</param>
/// <param name="RawText">Continuous text built from <paramref name="Words"/> (words joined by
/// spaces, lines joined by '\n'). This is what gets copied to the clipboard.</param>
/// <param name="WordStartOffsets">Start offset of every word inside <paramref name="RawText"/>.</param>
public record ExtractedDocument(
    IReadOnlyList<WordBox> Words,
    string RawText,
    int[] WordStartOffsets)
{
    public static readonly ExtractedDocument Empty =
        new(Array.Empty<WordBox>(), string.Empty, Array.Empty<int>());

    public bool IsEmpty => Words.Count == 0;

    public ExtractionSource? Source => Words.Count == 0 ? null : Words[0].Source;

    /// <summary>Builds the document from lines of words, keeping line breaks in <see cref="RawText"/>.</summary>
    public static ExtractedDocument FromLines(IEnumerable<IReadOnlyList<WordBox>> lines)
    {
        var words = new List<WordBox>();
        var offsets = new List<int>();
        var text = new System.Text.StringBuilder();

        bool firstLine = true;
        foreach (var line in lines)
        {
            if (line.Count == 0) continue;
            if (!firstLine) text.Append('\n');
            firstLine = false;

            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0) text.Append(' ');
                offsets.Add(text.Length);
                text.Append(line[i].Text);
                words.Add(line[i]);
            }
        }

        return new ExtractedDocument(words, text.ToString(), offsets.ToArray());
    }

    /// <summary>Index of the word containing (or immediately preceding) a raw-text offset.</summary>
    public int WordIndexAtOffset(int rawOffset)
    {
        if (WordStartOffsets.Length == 0) return -1;

        int index = Array.BinarySearch(WordStartOffsets, rawOffset);
        if (index >= 0) return index;

        index = ~index - 1;
        return Math.Max(0, Math.Min(index, WordStartOffsets.Length - 1));
    }
}
