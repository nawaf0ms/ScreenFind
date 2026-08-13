using ScreenFind.Core.Text;
using Xunit;

namespace ScreenFind.Tests;

/// <summary>
/// The index map is what turns "found it in the normalized text" into "highlight this word".
/// Every normalization rule must keep it accurate.
/// </summary>
public class IndexMapTests
{
    [Fact]
    public void MapsThroughDroppedDiacritics()
    {
        //            0 1 2 3 4 5 6 7 8 9
        // source:    م ُ د َ ر ّ ِ س َ ة
        var result = TextNormalizer.Normalize("مُدَرِّسَة");

        Assert.Equal("مدرسه", result.Value);
        Assert.Equal(0, result.Map.ToSource(0)); // م
        Assert.Equal(2, result.Map.ToSource(1)); // د
        Assert.Equal(4, result.Map.ToSource(2)); // ر
        Assert.Equal(7, result.Map.ToSource(3)); // س
        Assert.Equal(9, result.Map.ToSource(4)); // ة -> ه
        Assert.Equal(10, result.Map.ToSourceEnd(5));
    }

    [Fact]
    public void MapsThroughTatweel()
    {
        // م ـ ـ ـ د ر س ة
        var result = TextNormalizer.Normalize("مـــدرسة");

        Assert.Equal("مدرسه", result.Value);
        Assert.Equal(0, result.Map.ToSource(0));
        Assert.Equal(4, result.Map.ToSource(1)); // د after three tatweels
        Assert.Equal(7, result.Map.ToSource(4)); // ة
    }

    [Fact]
    public void MapsLigatureExpansionBackToOneSourceCharacter()
    {
        // A single U+FEF7 expands to two normalized characters.
        var result = TextNormalizer.Normalize("ﻷ");

        Assert.Equal("لا", result.Value);
        Assert.Equal(0, result.Map.ToSource(0));
        Assert.Equal(0, result.Map.ToSource(1));
        Assert.Equal(1, result.Map.ToSourceEnd(2));
    }

    [Fact]
    public void MapsThroughCollapsedWhitespace()
    {
        //          0 1 2 3  4  5 6 7
        // source: ' ',' ','a',' ','\n',' ','b',' '
        var result = TextNormalizer.Normalize("  a \n b ");

        Assert.Equal("a b", result.Value);
        Assert.Equal(2, result.Map.ToSource(0)); // a
        Assert.Equal(3, result.Map.ToSource(1)); // the surviving separator
        Assert.Equal(6, result.Map.ToSource(2)); // b
        Assert.Equal(7, result.Map.ToSourceEnd(3));
    }

    [Fact]
    public void FindsSubstringPositionInOriginalText()
    {
        string source = "قال المُدَرِّس للطلاب";
        var normalized = TextNormalizer.Normalize(source);

        int at = normalized.Value.IndexOf(TextNormalizer.NormalizeToString("المدرس"), StringComparison.Ordinal);
        Assert.True(at >= 0);

        var (start, end) = normalized.ToSourceRange(at, at + TextNormalizer.NormalizeToString("المدرس").Length);
        Assert.Equal("المُدَرِّس", source[start..end]);
    }

    [Fact]
    public void HandlesEmptyInput()
    {
        var result = TextNormalizer.Normalize("");
        Assert.Equal(0, result.Map.Length);
        Assert.Equal(0, result.Map.ToSource(0));
        Assert.Equal(0, result.Map.ToSourceEnd(0));
    }

    [Fact]
    public void MapIsMonotonic()
    {
        var result = TextNormalizer.Normalize("Hello مُدَرِّسَة ٢٠٢٤  world");
        int previous = -1;
        for (int i = 0; i < result.Length; i++)
        {
            int source = result.Map.ToSource(i);
            Assert.True(source >= previous, $"index map went backwards at {i}");
            previous = source;
        }
    }
}
