using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFind.Core.Extraction;
using ScreenFind.Core.Input;
using ScreenFind.Core.Interop;
using ScreenFind.Core.Text;

namespace ScreenFind.Core.Configuration;

/// <summary>User configurable hotkey, stored in a form that survives a settings file round trip.</summary>
public sealed record HotkeySettings
{
    public bool Control { get; init; } = true;
    public bool Shift { get; init; } = true;
    public bool Alt { get; init; }
    public bool Windows { get; init; }

    /// <summary>Key name: "A".."Z", "F1".."F12", "Space", "Insert", "Home", "End", "OemPeriod"…</summary>
    public string Key { get; init; } = "F";

    public uint Modifiers
    {
        get
        {
            uint modifiers = Win32.MOD_NOREPEAT;
            if (Control) modifiers |= Win32.MOD_CONTROL;
            if (Shift) modifiers |= Win32.MOD_SHIFT;
            if (Alt) modifiers |= Win32.MOD_ALT;
            if (Windows) modifiers |= Win32.MOD_WIN;
            return modifiers;
        }
    }

    public bool HasModifier => Control || Shift || Alt || Windows;

    public string Display
    {
        get
        {
            var parts = new List<string>(4);
            if (Control) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            if (Windows) parts.Add("Win");
            parts.Add(Key);
            return string.Join('+', parts);
        }
    }

    public HotkeyDefinition ToDefinition() => new(Modifiers, KeyCodes.FromName(Key), Display);
}

public enum UiLanguage
{
    /// <summary>Follow the Windows display language.</summary>
    Auto,
    Arabic,
    English
}

/// <summary>
/// Everything the settings window can change. Persisted as JSON next to the user's profile so a
/// portable copy of the app still finds it, and so nothing needs administrator rights.
/// </summary>
public sealed record ScreenFindSettings
{
    public static readonly ScreenFindSettings Defaults = new();

    public UiLanguage Language { get; init; } = UiLanguage.Auto;

    public HotkeySettings Hotkey { get; init; } = new();

    // ----- appearance (spec §5.6 defaults)
    public string HighlightColor { get; init; } = "#80FFEB3B";
    public string ActiveHighlightColor { get; init; } = "#B0FF9800";
    public string ActiveHighlightBorderColor { get; init; } = "#FFE65100";

    // ----- extraction
    public bool UseUiAutomation { get; init; } = true;
    public List<string> OcrLanguages { get; init; } = new() { "ar", "en" };
    public double PreprocessScale { get; init; } = 2.0;
    public ResampleMode ResampleMode { get; init; } = ResampleMode.Bicubic;
    public bool Grayscale { get; init; } = true;

    // ----- matching
    public bool EnableFuzzy { get; init; } = true;
    public double MinSimilarity { get; init; } = 0.85;
    public int MaxResults { get; init; } = 50;

    // ----- behaviour
    public int SearchDebounceMs { get; init; } = 250;
    public bool StartWithWindows { get; init; }

    /// <summary>False until the settings file has been written once — drives the first run experience.</summary>
    public bool Configured { get; init; }

    [JsonIgnore]
    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenFind");

    [JsonIgnore]
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ScreenFindSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Defaults with { Language = LanguageChosenAtInstall() };

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<ScreenFindSettings>(json, SerializerOptions) ?? Defaults;
        }
        catch (Exception)
        {
            // A corrupt settings file must never stop the app from starting.
            return Defaults;
        }
    }

    /// <summary>
    /// The installer writes the language picked in its first dialog to HKCU. It only seeds the
    /// very first run — after that settings.json is the single source of truth.
    /// </summary>
    private static UiLanguage LanguageChosenAtInstall()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\ScreenFind");
            return (key?.GetValue("Language") as string) switch
            {
                "ar" => UiLanguage.Arabic,
                "en" => UiLanguage.English,
                _ => UiLanguage.Auto
            };
        }
        catch (Exception)
        {
            return UiLanguage.Auto;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this with { Configured = true }, SerializerOptions));
    }

    public OcrExtractionOptions ToOcrOptions() => new()
    {
        Languages = OcrLanguages.Count > 0 ? OcrLanguages.ToArray() : new[] { "ar", "en" },
        Preprocess = new PreprocessOptions(Scale: PreprocessScale, Grayscale: Grayscale, Mode: ResampleMode)
    };

    public MatchOptions ToMatchOptions() => new()
    {
        EnableFuzzy = EnableFuzzy,
        MinSimilarity = MinSimilarity,
        MaxResults = MaxResults
    };
}

/// <summary>Name ⇄ virtual-key mapping for the keys a hotkey may use.</summary>
public static class KeyCodes
{
    public static uint FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return VirtualKeys.F;
        name = name.Trim();

        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= '0' and <= '9') return c;
        }

        if (name.Length >= 2 && (name[0] == 'F' || name[0] == 'f') &&
            int.TryParse(name.AsSpan(1), out int functionKey) && functionKey is >= 1 and <= 24)
        {
            return (uint)(0x70 + functionKey - 1);
        }

        return name.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "insert" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "oemperiod" or "." => 0xBE,
            "oemcomma" or "," => 0xBC,
            "oem2" or "/" => 0xBF,
            _ => VirtualKeys.F
        };
    }
}
