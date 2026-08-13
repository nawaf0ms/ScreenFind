using ScreenFind.Core.Text;
using Xunit;

namespace ScreenFind.Tests;

public class NormalizerTests
{
    /// <summary>
    /// Guards the whole Arabic suite: if these files were ever read with the wrong encoding,
    /// the literals below would stop being the code points the rules are written against.
    /// </summary>
    [Fact]
    public void SourceFileEncodingIsIntact()
    {
        Assert.Equal(5, "مدرسة".Length);
        Assert.Equal((char)0x0645, "مدرسة"[0]);   // meem
        Assert.Equal((char)0x0629, "مدرسة"[4]);   // teh marbuta
        Assert.Equal((char)0x0623, 'أ');          // alef with hamza above
        Assert.Equal((char)0x0640, 'ـ');          // tatweel
        Assert.Equal((char)0x064B, 'ً');          // fathatan
        Assert.Equal((char)0x0660, '٠');          // arabic-indic zero
        Assert.Equal((char)0xFEFB, 'ﻻ');          // lam-alef isolated form
    }

    [Theory]
    // مُدَرِّسَة -> مدرسه   (diacritics dropped, ta marbuta folded)
    [InlineData("مُدَرِّسَة", "مدرسه")]
    // مـــدرسة -> مدرسه   (tatweel dropped)
    [InlineData("مـــدرسة", "مدرسه")]
    // أحمد -> احمد
    [InlineData("أحمد", "احمد")]
    // إبراهيم -> ابراهيم
    [InlineData("إبراهيم", "ابراهيم")]
    // آمال -> امال
    [InlineData("آمال", "امال")]
    // ٱلله -> الله
    [InlineData("ٱلله", "الله")]
    // مصطفى -> مصطفي
    [InlineData("مصطفى", "مصطفي")]
    // مؤمن -> مومن
    [InlineData("مؤمن", "مومن")]
    // قائل -> قايل
    [InlineData("قائل", "قايل")]
    // ماء -> ما
    [InlineData("ماء", "ما")]
    // Farsi yeh/kaf folded to Arabic
    [InlineData("کتابی", "كتابي")]
    public void NormalizesArabicForms(string input, string expected)
        => Assert.Equal(expected, TextNormalizer.NormalizeToString(input));

    [Theory]
    [InlineData("٠١٢٣٤٥٦٧٨٩", "0123456789")]
    [InlineData("۰۱۲۳۴۵۶۷۸۹", "0123456789")]
    [InlineData("٢٠٢٤", "2024")]
    public void NormalizesDigits(string input, string expected)
        => Assert.Equal(expected, TextNormalizer.NormalizeToString(input));

    [Theory]
    [InlineData("Hello WORLD", "hello world")]
    [InlineData("MiXeD CaSe", "mixed case")]
    [InlineData("Ｈｅｌｌｏ", "hello")] // full-width Latin via NFKC
    public void NormalizesEnglish(string input, string expected)
        => Assert.Equal(expected, TextNormalizer.NormalizeToString(input));

    [Theory]
    [InlineData("   a \n\t b   ", "a b")]
    [InlineData("a\r\n\r\nb", "a b")]
    [InlineData("   ", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void CollapsesWhitespace(string? input, string expected)
        => Assert.Equal(expected, TextNormalizer.NormalizeToString(input));

    [Fact]
    public void ExpandsPresentationForms()
    {
        // U+FEFB LAM WITH ALEF ISOLATED FORM -> لا
        Assert.Equal("لا", TextNormalizer.NormalizeToString("ﻻ"));
        // U+FEF7 LAM WITH ALEF WITH HAMZA ABOVE -> لأ -> لا
        Assert.Equal("لا", TextNormalizer.NormalizeToString("ﻷ"));
        // U+FEDF LAM INITIAL FORM -> ل
        Assert.Equal("ل", TextNormalizer.NormalizeToString("ﻟ"));
    }

    [Fact]
    public void DropsBidiControlCharacters()
    {
        // RLM + text + LRM
        string input = "‏مدرسة‎";
        Assert.Equal("مدرسه", TextNormalizer.NormalizeToString(input));
    }

    [Fact]
    public void IsIdempotent()
    {
        string input = "مُدَرِّسَة Hello ٢٠٢٤";
        string once = TextNormalizer.NormalizeToString(input);
        Assert.Equal(once, TextNormalizer.NormalizeToString(once));
    }

    [Fact]
    public void QueryAndTextNormalizeIdentically()
    {
        // الطالبة (query, fully formed) vs الطالبه (OCR output with heh)
        Assert.Equal(
            TextNormalizer.NormalizeToString("الطالبة"),
            TextNormalizer.NormalizeToString("الطالبه"));
    }
}
