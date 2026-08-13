namespace ScreenFind.App.Services;

public static class AppEnvironment
{
    /// <summary>
    /// ScreenFind's own windows are excluded from screen capture (spec §5.1) — which also hides
    /// them from any screenshot tool, so a screenshot can never show the highlights. Setting
    /// SCREENFIND_ALLOW_CAPTURE=1 lifts the exclusion so the UI can be captured while testing.
    /// Never set it in normal use: the app would start reading its own overlay.
    /// </summary>
    public static bool AllowSelfCapture { get; } =
        Environment.GetEnvironmentVariable("SCREENFIND_ALLOW_CAPTURE") == "1";
}
