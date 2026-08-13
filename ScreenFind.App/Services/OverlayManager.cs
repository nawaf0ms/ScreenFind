using ScreenFind.App.Windows;
using ScreenFind.Core.Interop;
using ScreenFind.Core.Models;

namespace ScreenFind.App.Services;

/// <summary>
/// Keeps one <see cref="OverlayWindow"/> per monitor alive (spec §6: multi-monitor) and feeds
/// each one the highlights that land on it.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly Dictionary<IntPtr, OverlayWindow> _overlays = new();

    private HighlightTheme _theme = HighlightTheme.Default;

    /// <summary>Applies new colours to existing and future overlays.</summary>
    public void SetTheme(HighlightTheme theme)
    {
        _theme = theme;
        foreach (var overlay in _overlays.Values) overlay.Theme = theme;
    }

    public void Show(IReadOnlyList<Match> matches, int activeIndex)
    {
        EnsureOverlays();

        var highlights = new List<HighlightRect>();
        for (int i = 0; i < matches.Count; i++)
        {
            bool active = i == activeIndex;
            foreach (var bounds in matches[i].Bounds) highlights.Add(new HighlightRect(bounds, active));
        }

        foreach (var overlay in _overlays.Values)
        {
            if (!overlay.IsVisible) overlay.Show();
            overlay.Render(highlights);
        }
    }

    public void Clear()
    {
        foreach (var overlay in _overlays.Values) overlay.Clear();
    }

    public void HideAll()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Clear();
            overlay.Hide();
        }
    }

    private void EnsureOverlays()
    {
        var monitors = Win32.GetAllMonitors();

        foreach (IntPtr monitor in monitors)
        {
            if (_overlays.ContainsKey(monitor)) continue;

            var overlay = new OverlayWindow(monitor, _theme);
            _overlays[monitor] = overlay;
            overlay.Show();   // realises the handle so the capture exclusion is applied
            overlay.Clear();
        }

        // Monitors can be unplugged between invocations.
        foreach (IntPtr stale in _overlays.Keys.Where(handle => !monitors.Contains(handle)).ToList())
        {
            _overlays[stale].Close();
            _overlays.Remove(stale);
        }
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays.Values) overlay.Close();
        _overlays.Clear();
    }
}
