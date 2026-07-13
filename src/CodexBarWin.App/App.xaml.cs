using System.Windows;

namespace CodexBarWin.App;

/// <summary>
/// No main window is created (ShutdownMode=OnExplicitShutdown, no StartupUri).
/// The app lives entirely in the notification area via <see cref="TrayIconManager"/>.
/// </summary>
public partial class App : System.Windows.Application
{
    private TrayIconManager? _trayIconManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _trayIconManager = new TrayIconManager();
        _trayIconManager.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();
        base.OnExit(e);
    }
}
