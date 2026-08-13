using System.Windows;
using ScreenFind.App.Services;

namespace ScreenFind.App;

public partial class App : Application
{
    private AppOrchestrator? _orchestrator;
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second copy could never register the hotkey; hand over to the running one instead.
        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        // Pin the UI thread to per-monitor v2 before any window exists (see the helper's remarks).
        Core.Interop.Win32.EnsurePerMonitorDpiAwareThread();

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "ScreenFind", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            _orchestrator = new AppOrchestrator(_instance);
            _orchestrator.Start();
        }
        catch (Exception ex)
        {
            // Startup failures used to leave a windowless app that looked like nothing happened.
            MessageBox.Show(Localization.Loc.T("App.StartFailed") + "\n\n" + ex, "ScreenFind",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _orchestrator?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
