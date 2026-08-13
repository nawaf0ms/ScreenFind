using System.Windows.Media;
using ScreenFind.Core.Configuration;

namespace ScreenFind.App.Services;

/// <summary>Overlay colours, resolved from the settings file with the spec §5.6 values as fallback.</summary>
public sealed record HighlightTheme(Brush Fill, Brush ActiveFill, Brush ActiveStroke)
{
    public static HighlightTheme Default { get; } = FromSettings(ScreenFindSettings.Defaults);

    public static HighlightTheme FromSettings(ScreenFindSettings settings) => new(
        Parse(settings.HighlightColor, "#80FFEB3B"),
        Parse(settings.ActiveHighlightColor, "#B0FF9800"),
        Parse(settings.ActiveHighlightBorderColor, "#FFE65100"));

    private static Brush Parse(string value, string fallback)
    {
        SolidColorBrush? parsed = TryParse(value) ?? TryParse(fallback);
        if (parsed is null) return Brushes.Yellow;

        parsed.Freeze(); // shared across overlay windows
        return parsed;
    }

    private static SolidColorBrush? TryParse(string value)
    {
        try
        {
            return ColorConverter.ConvertFromString(value) is Color color ? new SolidColorBrush(color) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
