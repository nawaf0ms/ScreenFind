using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using ScreenFind.App.Localization;

namespace ScreenFind.App.Services;

/// <summary>
/// The app has no main window, so the tray icon is the only way to reach it: search, settings,
/// quit. Uses WinForms' NotifyIcon, which is the only tray API available to a WPF app.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private NotifyIcon? _icon;

    public event Action? Search;
    public event Action? Settings;
    public event Action? Exit;

    public void Show(string tooltip)
    {
        _icon = new NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = tooltip.Length > 63 ? tooltip[..63] : tooltip,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _icon.DoubleClick += (_, _) => Search?.Invoke();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { RightToLeft = Loc.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No };
        menu.Items.Add(Loc.T("Tray.Search"), null, (_, _) => Search?.Invoke());
        menu.Items.Add(Loc.T("Tray.Settings"), null, (_, _) => Settings?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("Tray.Exit"), null, (_, _) => Exit?.Invoke());
        return menu;
    }

    public void SetTooltip(string tooltip)
    {
        if (_icon is not null) _icon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    /// <summary>Rebuilds the menu after a language change, keeping the same tray icon.</summary>
    public void Relocalize()
    {
        if (_icon is null) return;

        var old = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = BuildMenu();
        old?.Dispose();
    }

    public void Notify(string message, string title = "ScreenFind")
    {
        if (_icon is null) return;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(8000);
    }

    /// <summary>The icon compiled into the executable, with a system fallback.</summary>
    public static Icon LoadApplicationIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null) return icon;
            }
        }
        catch (Exception)
        {
            // fall through
        }

        return SystemIcons.Application;
    }

    /// <summary>Same icon as a WPF <see cref="ImageSource"/> for window title bars.</summary>
    public static ImageSource? LoadApplicationImage()
    {
        try
        {
            using var icon = LoadApplicationIcon();
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}
