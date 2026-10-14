using System.IO;
using System.Windows;

namespace UhOptimizer.App;

public partial class App : Application
{
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] is "--begin-update-probe" or "--finish-update-probe")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (e.Args[0] == "--begin-update-probe") _ = UpdateProbe.BeginAsync(e.Args[1]); else _ = UpdateProbe.FinishAsync(e.Args[1]);
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--install-update")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = UpdateInstaller.RunWorkerAsync(e.Args[1]); return;
        }
        bool smoke = e.Args.Length >= 2 && e.Args[0] == "--smoke-test";
        // Named mutex prevents two normal instances from racing a saved backup.
        _instance = new Mutex(true, smoke ? "Local\\UhOptimizerSmoke" : "Local\\UhFiveMOptimizer", out bool first);
        if (!first) { MessageBox.Show("Uh FiveM Optimizer is already running.", "Uh FiveM Optimizer"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message + "\n\nAny existing backup has been kept.", "Uh FiveM Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        string data = smoke ? Path.Combine(Path.GetFullPath(e.Args[1]), "smoke-data") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UhFiveMOptimizer");
        var window = new MainWindow(data, smoke ? Path.GetFullPath(e.Args[1]) : null);
        MainWindow = window;
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
}
