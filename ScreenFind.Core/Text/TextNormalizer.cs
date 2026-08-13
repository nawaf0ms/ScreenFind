using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace ScreenFind.Core.Text;

/// <summary>
/// Arabic/English aware normalizer (spec §5.4). The exact same instance method must be
/// applied to extracted text and to the user query, otherwise matching silently degrades.
///
/// Everything is done character by character so an <see cref="IndexMap"/> can be built
/// alongside the output: normalized position -> original position.
/// </summary>
public static class TextNormalizer
{
    private const char Tatweel = 'ـ';
    private const char ArabicAlef = 'ا';
    private const char ArabicHeh = 'ه';
    private const char ArabicYeh = 'ي';
    private const char ArabicWaw = 'و';
    private const char ArabicKaf = 'ك';

    /// <summary>Compatibility expansion cache (presentation forms, ligatures, full-width).</summary>
    private static readonly ConcurrentDictionary<char, string> ExpansionCache = new();

    public static NormalizedText Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return NormalizedText.Empty;

        var builder = new StringBuilder(text.Length);
        var sources = new List<int>(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            foreach (char expanded in Expand(c))
            {
                AppendMapped(builder, sources, expanded, i);
            }
        }

        // Trim the trailing separator, if the text ended with whitespace.
        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
            sources.RemoveAt(sources.Count - 1);
        }

        return new NormalizedText(text, builder.ToString(), new IndexMap(sources.ToArray()));
    }

    /// <summary>Convenience overload for callers that only need the string (e.g. the user query).</summary>
    public static string NormalizeToString(string? text) => Normalize(text).Value;

    private static void AppendMapped(StringBuilder builder, List<int> sources, char c, int sourceIndex)
    {
        // Whitespace is checked first: '\n' and '\t' are Control characters, and they must
        // become separators rather than being dropped as invisible marks.
        if (char.IsWhiteSpace(c))
        {
            // Collapse runs of whitespace and drop leading whitespace.
            if (builder.Length == 0 || builder[^1] == ' ') return;
            builder.Append(' ');
            sources.Add(sourceIndex);
            return;
        }

        if (IsIgnorable(c)) return;

        char mapped = MapChar(c);
        if (mapped == '\0') return;

        builder.Append(mapped);
        sources.Add(sourceIndex);
    }

    /// <summary>
    /// Diacritics, tatweel, and invisible formatting characters carry no search meaning.
    /// Using the Unicode category covers the whole Arabic mark block (including Quranic
    /// marks) as well as stray Latin combining accents.
    /// </summary>
    private static bool IsIgnorable(char c)
    {
        if (c == Tatweel) return true;
        if (c == 'ء') return true; // standalone hamza — dropped per spec §5.4 rule 5

        return CharUnicodeInfo.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.NonSpacingMark => true,
            UnicodeCategory.EnclosingMark => true,
            UnicodeCategory.Format => true,          // RLM/LRM/ALM/ZWJ/ZWNJ, U+FEFF
            UnicodeCategory.Control => true,         // non-whitespace controls (already filtered above)
            _ => false
        };
    }

    private static char MapChar(char c) => c switch
    {
        // 2. alef forms
        'آ' or 'أ' or 'إ' or 'ٱ' or 'ٲ' or 'ٳ' or 'ٵ' => ArabicAlef,
        // 3. teh marbuta
        'ة' => ArabicHeh,
        // heh variants
        'ہ' or 'ۂ' or 'ە' => ArabicHeh,
        // 4. alef maksura + Farsi/Urdu yeh forms
        'ى' or 'ی' or 'ے' or 'ي' => ArabicYeh,
        // 5. hamza carriers
        'ؤ' => ArabicWaw,
        'ئ' => ArabicYeh,
        // Farsi/Urdu kaf
        'ک' or 'ڪ' => ArabicKaf,
        // Farsi/Urdu heh doachashmee is a distinct letter — leave it alone.
        _ => MapDigitsAndCase(c)
    };

    private static char MapDigitsAndCase(char c)
    {
        // 6. Arabic-Indic and extended Arabic-Indic digits
        if (c >= '٠' && c <= '٩') return (char)('0' + (c - '٠'));
        if (c >= '۰' && c <= '۹') return (char)('0' + (c - '۰'));

        // 7. English case folding
        if (c >= 'A' && c <= 'Z') return (char)(c + 32);
        if (c < 128) return c;

        return char.ToLowerInvariant(c);
    }

    /// <summary>
    /// 9. Unicode compatibility composition, applied per character so index tracking survives.
    /// Per-character NFKC is exactly what we want here: it unfolds Arabic presentation forms
    /// (U+FE70..U+FEFF), ligatures such as U+FEFB (lam-alef), and full-width Latin, without
    /// letting neighbouring characters recombine and shift positions.
    /// </summary>
    private static string Expand(char c)
    {
        if (c < 0x0080) return CharToString(c);
        return ExpansionCache.GetOrAdd(c, static ch =>
        {
            string s = ch.ToString();
            try
            {
                return s.IsNormalized(NormalizationForm.FormKC) ? s : s.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                return s; // lone surrogate — leave as-is
            }
        });
    }

    private static readonly string[] AsciiStrings = CreateAsciiStrings();

    private static string CharToString(char c) => AsciiStrings[c];

    private static string[] CreateAsciiStrings()
    {
        var table = new string[128];
        for (int i = 0; i < table.Length; i++) table[i] = ((char)i).ToString();
        return table;
    }
}
