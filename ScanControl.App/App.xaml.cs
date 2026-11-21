using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScanControl.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    internal static bool SafeStart { get; private set; }
    internal static bool SmokeTest { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        SafeStart = e.Args.Contains("--safe-start");
        SmokeTest = e.Args.Contains("--startup-smoke-test") || e.Args.Contains("--layout-smoke-test");
        RenderOptions.ProcessRenderMode = e.Args.Contains("--hardware-rendering") ? RenderMode.Default : RenderMode.SoftwareOnly;
        StartupDiagnostics.Write($"START version={typeof(App).Assembly.GetName().Version} OS={Environment.OSVersion} render={RenderOptions.ProcessRenderMode} safe={SafeStart} smoke={SmokeTest}");
        DispatcherUnhandledException += (_, args) =>
        {
            StartupDiagnostics.Write("UI ERROR " + args.Exception);
            args.Handled = true;
            if (!SmokeTest) MessageBox.Show("ScanControl konnte nicht weiterarbeiten.\n\n" + args.Exception.Message + "\n\nStartprotokoll: " + StartupDiagnostics.LogPath, "ScanControl – Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => StartupDiagnostics.Write("FATAL " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => { StartupDiagnostics.Write("TASK ERROR " + args.Exception); args.SetObserved(); };
        base.OnStartup(e);
        try
        {
            StartupDiagnostics.Write("Constructing window (no hardware initialization)");
            MainWindow = new MainWindow();
            MainWindow.Show();
            StartupDiagnostics.Write("Window shown");
        }
        catch (Exception error)
        {
            StartupDiagnostics.Write("START ERROR " + error);
            if (!SmokeTest) MessageBox.Show(error.Message + "\n\nStartprotokoll: " + StartupDiagnostics.LogPath, "ScanControl – Startfehler");
            Shutdown(1);
        }
    }
}
