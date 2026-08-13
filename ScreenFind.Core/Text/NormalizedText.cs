namespace ScreenFind.Core.Text;

/// <summary>Result of <see cref="TextNormalizer.Normalize"/>: the normalized string plus its index map.</summary>
public sealed class NormalizedText
{
    public static readonly NormalizedText Empty =
        new(string.Empty, string.Empty, new IndexMap(Array.Empty<int>()));

    public NormalizedText(string source, string value, IndexMap map)
    {
        Source = source;
        Value = value;
        Map = map;
    }

    /// <summary>The text exactly as it came in.</summary>
    public string Source { get; }

    /// <summary>The normalized text — this is what matching runs against.</summary>
    public string Value { get; }

    public IndexMap Map { get; }

    public int Length => Value.Length;

    public bool IsEmpty => Value.Length == 0;

    /// <summary>Converts a range in normalized space to a range in source space.</summary>
    public (int Start, int End) ToSourceRange(int start, int endExclusive)
        => (Map.ToSource(start), Map.ToSourceEnd(endExclusive));

    public override string ToString() => Value;
}
