using ScreenFind.Core.Models;
using ScreenFind.Core.Text;
using Xunit;

namespace ScreenFind.Tests;

public class MatchEngineTests
{
    private const double LineHeight = 20;
    private const double LineGap = 30;
    private const double CharWidth = 10;

    /// <summary>Builds a document with synthetic but geometrically consistent word boxes.</summary>
    private static SearchableDocument Build(params string[][] lines)
    {
        var built = new List<IReadOnlyList<WordBox>>();
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            double x = 100;
            double y = 100 + lineIndex * LineGap;
            var words = new List<WordBox>();
            foreach (string word in lines[lineIndex])
            {
                double width = word.Length * CharWidth;
                words.Add(new WordBox(
                    word,
                    new Rect(x, y, width, LineHeight),
                    LanguageTags.Detect(word),
                    0.9f,
                    ExtractionSource.Ocr));
                x += width + CharWidth;
            }
            built.Add(words);
        }

        return SearchableDocument.Create(ExtractedDocument.FromLines(built));
    }

    private static readonly MatchEngine Engine = new();

    [Fact]
    public void FindsExactWord()
    {
        var doc = Build(new[] { "hello", "world", "again" });

        var matches = Engine.Find(doc, "world");

        var match = Assert.Single(matches);
        Assert.Equal(1, match.StartWordIndex);
        Assert.Equal(1, match.EndWordIndex);
        Assert.Equal(1f, match.Score);
        Assert.Equal(doc.Document.Words[1].ScreenBounds, Assert.Single(match.Bounds));
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        var doc = Build(new[] { "Hello", "WORLD" });
        Assert.Single(Engine.Find(doc, "world"));
        Assert.Single(Engine.Find(doc, "hELLo"));
    }

    [Fact]
    public void FindsAllOccurrences()
    {
        var doc = Build(new[] { "cat", "dog", "cat" }, new[] { "cat" });
        Assert.Equal(3, Engine.Find(doc, "cat").Count);
    }

    [Fact]
    public void FindsPhraseAcrossWords()
    {
        var doc = Build(new[] { "the", "quick", "brown", "fox" });

        var match = Assert.Single(Engine.Find(doc, "quick brown"));

        Assert.Equal(1, match.StartWordIndex);
        Assert.Equal(2, match.EndWordIndex);
        Assert.Single(match.Bounds); // same line -> one rectangle
    }

    [Fact]
    public void ReturnsOneRectanglePerLineWhenMatchWraps()
    {
        var doc = Build(new[] { "hello" }, new[] { "world" });

        var match = Assert.Single(Engine.Find(doc, "hello world"));

        Assert.Equal(2, match.Bounds.Count);
        Assert.Equal(doc.Document.Words[0].ScreenBounds, match.Bounds[0]);
        Assert.Equal(doc.Document.Words[1].ScreenBounds, match.Bounds[1]);
    }

    [Fact]
    public void ArabicQueryWithTaMarbutaMatchesHehInText()
    {
        // Spec §7 phase 3 acceptance: searching «الطالبة» must find «الطالبه».
        var doc = Build(new[] { "ذهبت", "الطالبه", "الي", "المكتبه" });

        var match = Assert.Single(Engine.Find(doc, "الطالبة"));
        Assert.Equal(1, match.StartWordIndex);
    }

    [Fact]
    public void ArabicQueryMatchesTextWithDiacritics()
    {
        // Spec §7 phase 3 acceptance: «مدرسة» must be found although the source is vocalized.
        var doc = Build(new[] { "ذهب", "الي", "المَدْرَسَةِ", "مبكرا" });

        var match = Assert.Single(Engine.Find(doc, "مدرسة"));
        Assert.Equal(2, match.StartWordIndex);
        Assert.Equal(1f, match.Score);
    }

    [Fact]
    public void ArabicAlefVariantsMatch()
    {
        var doc = Build(new[] { "احمد", "ابراهيم" });
        Assert.Single(Engine.Find(doc, "أحمد"));
        Assert.Single(Engine.Find(doc, "إبراهيم"));
    }

    [Fact]
    public void FuzzyMatchRecoversFromOcrConfusion()
    {
        // OCR read "university" as "universlty" (i/l confusion).
        var doc = Build(new[] { "the", "universlty", "campus" });

        var match = Assert.Single(Engine.Find(doc, "university"));

        Assert.Equal(1, match.StartWordIndex);
        Assert.InRange(match.Score, 0.85f, 0.999f);
    }

    [Fact]
    public void FuzzyMatchRecoversFromArabicOcrConfusion()
    {
        // OCR read «الجامعة» as «الحامعه» (ج/ح confusion).
        var doc = Build(new[] { "في", "الحامعه", "الكبيرة" });

        var matches = Engine.Find(doc, "الجامعة");

        Assert.NotEmpty(matches);
        Assert.Equal(1, matches[0].StartWordIndex);
        Assert.InRange(matches[0].Score, 0.85f, 0.999f);
    }

    [Fact]
    public void ExactMatchWinsOverFuzzy()
    {
        var doc = Build(new[] { "universlty", "university" });

        var match = Assert.Single(Engine.Find(doc, "university"));

        Assert.Equal(1, match.StartWordIndex);
        Assert.Equal(1f, match.Score);
    }

    [Fact]
    public void ReturnsNothingForUnrelatedQuery()
    {
        var doc = Build(new[] { "hello", "world" });
        Assert.Empty(Engine.Find(doc, "zebra"));
    }

    [Fact]
    public void ReturnsNothingForEmptyQuery()
    {
        var doc = Build(new[] { "hello", "world" });
        Assert.Empty(Engine.Find(doc, ""));
        Assert.Empty(Engine.Find(doc, "   "));
        Assert.Empty(Engine.Find(doc, null));
    }

    [Fact]
    public void HonoursMaxResults()
    {
        var line = Enumerable.Repeat("cat", 80).ToArray();
        var doc = Build(line);

        var engine = new MatchEngine(new MatchOptions { MaxResults = 50 });
        Assert.Equal(50, engine.Find(doc, "cat").Count);
    }

    [Fact]
    public void MatchesAreOrderedByPosition()
    {
        var doc = Build(new[] { "alpha", "beta" }, new[] { "alpha", "gamma" }, new[] { "alpha" });

        var matches = Engine.Find(doc, "alpha");

        Assert.Equal(3, matches.Count);
        Assert.True(matches[0].StartWordIndex < matches[1].StartWordIndex);
        Assert.True(matches[1].StartWordIndex < matches[2].StartWordIndex);
    }

    [Fact]
    public void FuzzyCanBeDisabled()
    {
        var doc = Build(new[] { "universlty" });
        var engine = new MatchEngine(new MatchOptions { EnableFuzzy = false });
        Assert.Empty(engine.Find(doc, "university"));
    }

    [Fact]
    public void EmptyDocumentIsHandled()
        => Assert.Empty(Engine.Find(SearchableDocument.Empty, "anything"));
}
