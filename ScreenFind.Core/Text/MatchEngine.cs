using ScreenFind.Core.Models;

namespace ScreenFind.Core.Text;

public sealed record MatchOptions
{
    public static readonly MatchOptions Default = new();

    /// <summary>Fuzzy matching is not a luxury: OCR confusions (ه/ة, rn/m, 0/O) are guaranteed.</summary>
    public bool EnableFuzzy { get; init; } = true;

    /// <summary>Normalized Levenshtein similarity required for a fuzzy hit (spec §5.5).</summary>
    public double MinSimilarity { get; init; } = 0.85;

    /// <summary>Candidate windows may deviate from the query length by this fraction.</summary>
    public double LengthTolerance { get; init; } = 0.30;

    public int MaxResults { get; init; } = 50;

    /// <summary>Vertical overlap above which two word boxes are considered to be on the same line.</summary>
    public double SameLineOverlap { get; init; } = 0.5;

    /// <summary>Queries shorter than this never go through fuzzy matching — too noisy.</summary>
    public int MinFuzzyQueryLength { get; init; } = 3;
}

public sealed class MatchEngine
{
    private readonly MatchOptions _options;

    public MatchEngine(MatchOptions? options = null) => _options = options ?? MatchOptions.Default;

    public IReadOnlyList<Match> Find(SearchableDocument document, string? query)
    {
        if (document.IsEmpty) return Array.Empty<Match>();

        string normalizedQuery = TextNormalizer.NormalizeToString(query);
        if (normalizedQuery.Length == 0) return Array.Empty<Match>();

        var exact = FindExact(document, normalizedQuery);
        if (exact.Count > 0) return Materialize(document, exact);

        if (!_options.EnableFuzzy || normalizedQuery.Length < _options.MinFuzzyQueryLength)
            return Array.Empty<Match>();

        var fuzzy = FindFuzzy(document, normalizedQuery);
        return Materialize(document, fuzzy);
    }

    private readonly record struct Candidate(int Start, int End, float Score);

    private List<Candidate> FindExact(SearchableDocument document, string query)
    {
        var hits = new List<Candidate>();
        string haystack = document.Text.Value;

        int index = haystack.IndexOf(query, StringComparison.Ordinal);
        while (index >= 0)
        {
            hits.Add(new Candidate(index, index + query.Length, 1f));
            if (hits.Count >= _options.MaxResults) break;
            index = haystack.IndexOf(query, index + 1, StringComparison.Ordinal);
        }

        return hits;
    }

    /// <summary>
    /// Sliding window restricted to word boundaries: windows are grown token by token while
    /// their length stays within ±LengthTolerance of the query. This keeps the search linear
    /// in the number of words instead of quadratic in characters, and it lands on the word
    /// edges that the highlighter needs anyway.
    /// </summary>
    private List<Candidate> FindFuzzy(SearchableDocument document, string query)
    {
        string text = document.Text.Value;
        var starts = TokenStarts(text);
        if (starts.Count == 0) return new List<Candidate>();

        int minLength = Math.Max(1, (int)Math.Floor(query.Length * (1 - _options.LengthTolerance)));
        int maxLength = (int)Math.Ceiling(query.Length * (1 + _options.LengthTolerance));

        var candidates = new List<Candidate>();
        var querySpan = query.AsSpan();

        for (int s = 0; s < starts.Count; s++)
        {
            int start = starts[s];
            Candidate best = default;
            bool found = false;

            for (int e = s; e < starts.Count; e++)
            {
                int end = TokenEnd(text, starts, e);
                int length = end - start;
                if (length < minLength) continue;
                if (length > maxLength) break;

                double score = Levenshtein.Similarity(
                    text.AsSpan(start, length), querySpan, _options.MinSimilarity);

                if (score >= _options.MinSimilarity && (!found || score > best.Score))
                {
                    best = new Candidate(start, end, (float)score);
                    found = true;
                }
            }

            if (found) candidates.Add(best);
        }

        // Overlapping windows describe the same hit — keep the strongest one.
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        var kept = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            bool overlaps = kept.Any(k => candidate.Start < k.End && k.Start < candidate.End);
            if (overlaps) continue;
            kept.Add(candidate);
            if (kept.Count >= _options.MaxResults) break;
        }

        kept.Sort((a, b) => a.Start.CompareTo(b.Start));
        return kept;
    }

    private static List<int> TokenStarts(string text)
    {
        var starts = new List<int>();
        bool previousWasSeparator = true;
        for (int i = 0; i < text.Length; i++)
        {
            bool isSeparator = text[i] == ' ';
            if (!isSeparator && previousWasSeparator) starts.Add(i);
            previousWasSeparator = isSeparator;
        }
        return starts;
    }

    private static int TokenEnd(string text, List<int> starts, int tokenIndex)
    {
        if (tokenIndex + 1 < starts.Count) return starts[tokenIndex + 1] - 1; // minus the separator
        return text.Length;
    }

    private IReadOnlyList<Match> Materialize(SearchableDocument document, List<Candidate> candidates)
    {
        if (candidates.Count == 0) return Array.Empty<Match>();

        var matches = new List<Match>(candidates.Count);
        foreach (var candidate in candidates.Take(_options.MaxResults))
        {
            var match = BuildMatch(document, candidate);
            if (match is not null) matches.Add(match);
        }

        matches.Sort((a, b) => a.StartWordIndex.CompareTo(b.StartWordIndex));
        return matches;
    }

    private Match? BuildMatch(SearchableDocument document, Candidate candidate)
    {
        var (rawStart, rawEnd) = document.Text.ToSourceRange(candidate.Start, candidate.End);

        int firstWord = document.Document.WordIndexAtOffset(rawStart);
        int lastWord = document.Document.WordIndexAtOffset(Math.Max(rawStart, rawEnd - 1));
        if (firstWord < 0 || lastWord < 0) return null;
        if (lastWord < firstWord) lastWord = firstWord;

        var bounds = BuildBounds(document.Document, firstWord, lastWord);
        if (bounds.Count == 0) return null;

        return new Match(firstWord, lastWord, bounds, candidate.Score);
    }

    /// <summary>Merges the boxes of the matched words, one rectangle per visual line (spec §5.5.4-5).</summary>
    private List<Rect> BuildBounds(ExtractedDocument document, int firstWord, int lastWord)
    {
        var bounds = new List<Rect>();
        Rect current = Rect.Empty;
        bool hasCurrent = false;

        for (int i = firstWord; i <= lastWord && i < document.Words.Count; i++)
        {
            var box = document.Words[i].ScreenBounds;
            if (box.IsEmpty) continue;

            if (!hasCurrent)
            {
                current = box;
                hasCurrent = true;
                continue;
            }

            if (current.VerticalOverlapRatio(box) >= _options.SameLineOverlap)
            {
                current = current.Union(box);
            }
            else
            {
                bounds.Add(current);
                current = box;
            }
        }

        if (hasCurrent) bounds.Add(current);
        return bounds;
    }
}
