using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using ScreenFind.Core.Configuration;

namespace ScreenFind.App.Localization;

/// <summary>
/// Plain dictionary localization — no resx, no satellite assemblies. That keeps every string in
/// one reviewable file and keeps single-file publishing free of resource-probing surprises.
/// Windows are recreated when the language changes, so lookups can happen at load time.
/// </summary>
public static class Loc
{
    /// <summary>The language actually in use (never <see cref="UiLanguage.Auto"/>).</summary>
    public static UiLanguage Current { get; private set; } = Resolve(UiLanguage.Auto);

    public static bool IsRightToLeft => Current == UiLanguage.Arabic;

    public static FlowDirection FlowDirection =>
        IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public static void Use(UiLanguage language) => Current = Resolve(language);

    private static UiLanguage Resolve(UiLanguage language)
    {
        if (language != UiLanguage.Auto) return language;

        string ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return ui.Equals("ar", StringComparison.OrdinalIgnoreCase) ? UiLanguage.Arabic : UiLanguage.English;
    }

    public static string T(string key)
    {
        var table = Current == UiLanguage.Arabic ? Arabic : English;
        if (table.TryGetValue(key, out string? value)) return value;
        return English.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    /// <summary>Formats a localized string that contains {0}, {1}… placeholders.</summary>
    public static string T(string key, params object[] arguments)
        => string.Format(CultureInfo.CurrentCulture, T(key), arguments);

    private static readonly Dictionary<string, string> English = new()
    {
        ["App.Name"] = "ScreenFind",
        ["App.ReadyTitle"] = "ScreenFind is running",
        ["App.Ready"] = "Ready — press {0} over any window.",
        ["App.StartedTitle"] = "Installed and running",
        ["App.FirstRun"] = "ScreenFind lives in the notification area. Press {0} over any window to search inside it.",
        ["App.StartFailed"] = "ScreenFind could not start:",
        ["App.HotkeyFailedTitle"] = "Hotkey not registered",
        ["App.HotkeySuffix"] = " Change it in Settings.",
        ["App.MissingOcrTitle"] = "OCR language missing",
        ["App.StartupFailed"] = "Could not change the startup setting: {0}",

        ["Tray.Search"] = "Search the screen",
        ["Tray.Settings"] = "Settings…",
        ["Tray.Exit"] = "Exit",

        ["Bar.Reading"] = "Reading the screen…",
        ["Bar.NoText"] = "No text found in this window.",
        ["Bar.Status"] = "{0} words · {1} · {2} ms",
        ["Bar.Cached"] = " (cached)",
        ["Bar.NoResults"] = "No results.",
        ["Bar.Results"] = "{0} result(s).",
        ["Bar.ResultsFuzzy"] = "{0} result(s) — includes approximate matches.",
        ["Bar.ExtractFailed"] = "Extraction failed: {0}",
        ["Bar.Copied"] = "Copied {0} characters to the clipboard.",
        ["Bar.CopyFailed"] = "Copy failed: {0}",
        ["Bar.NothingToCopy"] = "Nothing to copy yet.",
        ["Bar.Placeholder"] = "Search the screen…",
        ["Bar.Previous"] = "Previous (Shift+Enter)",
        ["Bar.Next"] = "Next (Enter / F3)",
        ["Bar.Close"] = "Close (Esc)",

        ["Settings.Title"] = "ScreenFind settings",
        ["Settings.Subtitle"] = "Applied immediately — no restart needed.",
        ["Settings.Credit"] = "Version {0} · © 2026 nawaf0ms ·",
        ["Settings.Section.Language"] = "Language",
        ["Settings.Section.Hotkey"] = "Global hotkey",
        ["Settings.Section.Extraction"] = "Extraction",
        ["Settings.Section.Matching"] = "Matching",
        ["Settings.Section.Colors"] = "Highlight colours",
        ["Settings.Section.Behaviour"] = "Behaviour",

        ["Settings.Language.Label"] = "Interface language",
        ["Settings.Language.Auto"] = "Follow Windows",
        ["Settings.Language.Arabic"] = "العربية",
        ["Settings.Language.English"] = "English",
        ["Settings.Language.Changed"] = "The language will apply to the search bar right away; reopen this window to see it here.",

        ["Settings.Hotkey.Hint"] = "Click the box and press the combination. At least one modifier is required (Ctrl / Shift / Alt / Win).",
        ["Settings.Hotkey.NeedModifier"] = "The key needs at least one modifier.",

        ["Settings.UseUia"] = "Try UI Automation first (exact and much faster where supported)",
        ["Settings.OcrLanguages"] = "Enabled OCR languages",
        ["Settings.OcrHint"] = "To add a language: {0}",
        ["Settings.NoOcrEngines"] = "No OCR engine is installed. {0}",
        ["Settings.PickOneLanguage"] = "Select at least one OCR language.",
        ["Settings.Scale"] = "Upscale before OCR",
        ["Settings.Resample"] = "Resampling",
        ["Settings.Grayscale"] = "Convert to grayscale before recognition",
        ["Settings.PreprocessHint"] = "×2 with bicubic measured best overall: ×3 gains very little accuracy for twice the time.",

        ["Settings.Fuzzy"] = "Enable fuzzy matching (tolerates OCR mistakes)",
        ["Settings.MinSimilarity"] = "Minimum similarity",
        ["Settings.MaxResults"] = "Maximum results",

        ["Settings.ColorAll"] = "All results",
        ["Settings.ColorActive"] = "Active result",
        ["Settings.ColorBorder"] = "Active result border",
        ["Settings.ColorHint"] = "Format #AARRGGBB — the first two digits are the opacity.",
        ["Settings.ColorInvalid"] = "{0} is not a valid colour — use a value such as #80FFEB3B.",

        ["Settings.Debounce"] = "Search delay while typing (ms)",
        ["Settings.StartWithWindows"] = "Start ScreenFind with Windows",

        ["Settings.Save"] = "Save",
        ["Settings.Cancel"] = "Cancel",
        ["Settings.Reset"] = "Restore defaults",
        ["Settings.ResetDone"] = "Defaults restored — press Save to apply.",
        ["Settings.File"] = "Settings file: {0}"
    };

    private static readonly Dictionary<string, string> Arabic = new()
    {
        ["App.Name"] = "ScreenFind",
        ["App.ReadyTitle"] = "ScreenFind يعمل",
        ["App.Ready"] = "جاهز — اضغط {0} فوق أي نافذة.",
        ["App.StartedTitle"] = "تم التشغيل",
        ["App.FirstRun"] = "ScreenFind يعمل في شريط النظام. اضغط {0} فوق أي نافذة للبحث فيها.",
        ["App.StartFailed"] = "تعذّر تشغيل ScreenFind:",
        ["App.HotkeyFailedTitle"] = "تعذّر تسجيل الاختصار",
        ["App.HotkeySuffix"] = " غيّر الاختصار من الإعدادات.",
        ["App.MissingOcrTitle"] = "لغة OCR ناقصة",
        ["App.StartupFailed"] = "تعذّر ضبط التشغيل التلقائي: {0}",

        ["Tray.Search"] = "بحث في الشاشة",
        ["Tray.Settings"] = "الإعدادات…",
        ["Tray.Exit"] = "إنهاء",

        ["Bar.Reading"] = "جارٍ قراءة الشاشة…",
        ["Bar.NoText"] = "لم يُعثر على نص في هذه النافذة.",
        ["Bar.Status"] = "{0} كلمة · {1} · {2}ms",
        ["Bar.Cached"] = " (مخزَّن)",
        ["Bar.NoResults"] = "لا نتائج.",
        ["Bar.Results"] = "{0} نتيجة.",
        ["Bar.ResultsFuzzy"] = "{0} نتيجة — تشمل مطابقات تقريبية.",
        ["Bar.ExtractFailed"] = "فشل الاستخراج: {0}",
        ["Bar.Copied"] = "نُسخ {0} حرفاً إلى الحافظة.",
        ["Bar.CopyFailed"] = "فشل النسخ: {0}",
        ["Bar.NothingToCopy"] = "لا يوجد نص لنسخه بعد.",
        ["Bar.Placeholder"] = "ابحث في الشاشة…",
        ["Bar.Previous"] = "السابق (Shift+Enter)",
        ["Bar.Next"] = "التالي (Enter / F3)",
        ["Bar.Close"] = "إغلاق (Esc)",

        ["Settings.Title"] = "إعدادات ScreenFind",
        ["Settings.Subtitle"] = "كل تغيير يُطبَّق فوراً بلا إعادة تشغيل.",
        ["Settings.Credit"] = "الإصدار {0} · جميع الحقوق محفوظة © 2026 nawaf0ms ·",
        ["Settings.Section.Language"] = "اللغة",
        ["Settings.Section.Hotkey"] = "الاختصار العام",
        ["Settings.Section.Extraction"] = "الاستخراج",
        ["Settings.Section.Matching"] = "المطابقة",
        ["Settings.Section.Colors"] = "ألوان التظليل",
        ["Settings.Section.Behaviour"] = "السلوك",

        ["Settings.Language.Label"] = "لغة الواجهة",
        ["Settings.Language.Auto"] = "حسب لغة ويندوز",
        ["Settings.Language.Arabic"] = "العربية",
        ["Settings.Language.English"] = "English",
        ["Settings.Language.Changed"] = "ستُطبَّق اللغة على صندوق البحث فوراً؛ أعِد فتح هذه النافذة لرؤيتها هنا.",

        ["Settings.Hotkey.Hint"] = "انقر الحقل ثم اضغط التركيبة المطلوبة. لا بد من مُعدِّل واحد على الأقل (Ctrl / Shift / Alt / Win).",
        ["Settings.Hotkey.NeedModifier"] = "لا بد من مُعدِّل واحد على الأقل مع المفتاح.",

        ["Settings.UseUia"] = "جرّب UI Automation أولاً (أدق وأسرع بكثير عند دعمه)",
        ["Settings.OcrLanguages"] = "لغات الـ OCR المفعّلة",
        ["Settings.OcrHint"] = "لتثبيت لغة إضافية: {0}",
        ["Settings.NoOcrEngines"] = "لم يُعثر على أي محرك OCR مثبّت. {0}",
        ["Settings.PickOneLanguage"] = "اختر لغة OCR واحدة على الأقل.",
        ["Settings.Scale"] = "معامل التكبير قبل OCR",
        ["Settings.Resample"] = "طريقة إعادة التحجيم",
        ["Settings.Grayscale"] = "تحويل إلى تدرّج رمادي قبل التعرّف",
        ["Settings.PreprocessHint"] = "‏×2 مع bicubic هو الأفضل توازناً بالقياس: ×3 يحسّن الدقة بنسبة ضئيلة مقابل ضعف الزمن.",

        ["Settings.Fuzzy"] = "تفعيل المطابقة الضبابية (تتجاوز أخطاء OCR)",
        ["Settings.MinSimilarity"] = "أدنى تشابه",
        ["Settings.MaxResults"] = "أقصى عدد نتائج",

        ["Settings.ColorAll"] = "كل النتائج",
        ["Settings.ColorActive"] = "النتيجة النشطة",
        ["Settings.ColorBorder"] = "إطار النتيجة النشطة",
        ["Settings.ColorHint"] = "الصيغة ‎#AARRGGBB‎ — أول بايتين هما الشفافية.",
        ["Settings.ColorInvalid"] = "{0} ليس لوناً صالحاً — استخدم صيغة مثل ‎#80FFEB3B‎.",

        ["Settings.Debounce"] = "مهلة البحث أثناء الكتابة (ms)",
        ["Settings.StartWithWindows"] = "تشغيل ScreenFind مع بدء ويندوز",

        ["Settings.Save"] = "حفظ",
        ["Settings.Cancel"] = "إلغاء",
        ["Settings.Reset"] = "استعادة الافتراضي",
        ["Settings.ResetDone"] = "أُعيدت القيم الافتراضية — اضغط حفظ للتطبيق.",
        ["Settings.File"] = "ملف الإعدادات: {0}"
    };
}

/// <summary>XAML helper: <c>Text="{loc:T Settings.Section.Hotkey}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
