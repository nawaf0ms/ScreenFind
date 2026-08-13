namespace ScreenFind.Core.Text;

/// <summary>
/// Maps every character position in the normalized text back to its position in the
/// original text. Without this, a match can be found but never located on screen
/// (spec §5.4). Normalization is therefore built character by character — never as a
/// chain of string replacements.
/// </summary>
public sealed class IndexMap
{
    private readonly int[] _sourceIndices;

    public IndexMap(int[] sourceIndices) => _sourceIndices = sourceIndices;

    public int Length => _sourceIndices.Length;

    /// <summary>Source index of the character that produced normalized character <paramref name="normalizedIndex"/>.</summary>
    public int ToSource(int normalizedIndex)
    {
        if (_sourceIndices.Length == 0) return 0;
        if (normalizedIndex < 0) return _sourceIndices[0];
        if (normalizedIndex >= _sourceIndices.Length) return _sourceIndices[^1] + 1;
        return _sourceIndices[normalizedIndex];
    }

    /// <summary>
    /// Exclusive source index for a normalized range end. A single source character can
    /// expand into several normalized characters, so the end is derived from the last
    /// consumed source character, not from the normalized offset.
    /// </summary>
    public int ToSourceEnd(int normalizedEndExclusive)
    {
        if (_sourceIndices.Length == 0) return 0;
        int last = Math.Min(normalizedEndExclusive, _sourceIndices.Length) - 1;
        if (last < 0) return _sourceIndices[0];
        return _sourceIndices[last] + 1;
    }
}
