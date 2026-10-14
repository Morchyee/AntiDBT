using System.IO;
using System.Windows;
namespace AntiDbt.Desktop;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(testMode: e.Args.Contains("--smoke-test"));
        MainWindow = window;
        if (e.Args.Contains("--smoke-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                await DesktopSmoke.RunAsync(window, e.Args.SkipWhile(x => x != "--smoke-test").Skip(1).FirstOrDefault() ?? "artifacts");
                Shutdown(0);
            }
            catch { Shutdown(1); }
        }
        else
            window.Show();
    }
}
