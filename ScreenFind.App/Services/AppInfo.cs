using System.Reflection;

namespace ScreenFind.App.Services;

/// <summary>Product identity, read from the assembly metadata stamped in Directory.Build.props.</summary>
public static class AppInfo
{
    public const string Author = "nawaf0ms";
    public const string ProjectUrl = "https://github.com/nawaf0ms";

    public static string Version { get; } = ReadVersion();

    public static string Copyright { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? "Copyright © 2026 nawaf0ms";

    private static string ReadVersion()
    {
        string? informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            // Strip the build metadata suffix the SDK appends (e.g. "1.0.0+abc123").
            int plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
