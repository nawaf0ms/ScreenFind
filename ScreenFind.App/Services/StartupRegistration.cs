using Microsoft.Win32;

namespace ScreenFind.App.Services;

/// <summary>
/// "Start with Windows" — a per-user Run entry. Per-user only: no elevation, and it never
/// touches anything outside HKCU. Driven exclusively by the checkbox in the settings window.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenFind";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Returns null on success, or a message describing why it failed.</summary>
    public static string? Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return "تعذّر فتح مفتاح التشغيل التلقائي.";

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return null;
            }

            string? path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return "تعذّر تحديد مسار البرنامج.";

            key.SetValue(ValueName, $"\"{path}\"");
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
