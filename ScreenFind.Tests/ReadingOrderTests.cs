using ScreenFind.Core.Extraction;
using ScreenFind.Core.Models;
using Xunit;

namespace ScreenFind.Tests;

/// <summary>
/// Windows OCR hands back the words of a line in visual order. For Arabic that is the reverse of
/// reading order, and getting it wrong destroys phrase search (measured: 3% vs 81% accuracy).
/// </summary>
public class ReadingOrderTests
{
    private static OcrTextLine Line(params (string Text, double X)[] words)
    {
        var tokens = words
            .Select(w => new OcrToken(w.Text, new Rect(w.X, 100, w.Text.Length * 10, 20)))
            .ToArray();

        return new OcrTextLine(string.Join(' ', tokens.Select(t => t.Text)), tokens);
    }

    [Theory]
    [InlineData("ذهبت الطالبة إلى المكتبة", true)]
    [InlineData("The student walked", false)]
    [InlineData("2024", false)]
    // Mixed lines follow the majority script.
    [InlineData("مدرسة ابتدائية school", true)]
    [InlineData("the school مدرسة", false)]
    public void DetectsDirection(string text, bool expected)
        => Assert.Equal(expected, ReadingOrder.IsRightToLeft(text));

    [Fact]
    public void ArabicLineIsReversedIntoLogicalOrder()
    {
        // As the engine returns it: left to right on screen.
        var line = Line(("الباكر", 283), ("الصباح", 320), ("في", 371), ("الطالبة", 559), ("ذهبت", 613));

        var ordered = ReadingOrder.ToLogicalOrder(line);

        Assert.Equal(new[] { "ذهبت", "الطالبة", "في", "الصباح", "الباكر" },
            ordered.Select(w => w.Text).ToArray());
    }

    [Fact]
    public void EnglishLineKeepsVisualOrder()
    {
        var line = Line(("The", 100), ("student", 140), ("walked", 220));

        var ordered = ReadingOrder.ToLogicalOrder(line);

        Assert.Equal(new[] { "The", "student", "walked" }, ordered.Select(w => w.Text).ToArray());
    }

    [Fact]
    public void SingleWordLineIsUntouched()
    {
        var line = Line(("مدرسة", 500));
        Assert.Single(ReadingOrder.ToLogicalOrder(line));
    }

    [Fact]
    public void LogicalTextJoinsInReadingOrder()
    {
        var line = Line(("الباكر", 283), ("الصباح", 320), ("ذهبت", 613));
        Assert.Equal("ذهبت الصباح الباكر", ReadingOrder.ToLogicalText(line));
    }

    [Fact]
    public void LinesAreSortedTopToBottomThenLeftToRight()
    {
        var top = new Rect(500, 100, 100, 20);
        var topRight = new Rect(700, 102, 100, 20);
        var below = new Rect(100, 140, 100, 20);

        Assert.True(ReadingOrder.CompareLines(top, topRight) < 0);   // same line -> by x
        Assert.True(ReadingOrder.CompareLines(top, below) < 0);      // different lines -> by y
        Assert.True(ReadingOrder.CompareLines(below, topRight) > 0);
    }
}
