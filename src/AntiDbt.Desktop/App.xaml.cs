using System.IO;
using System.Windows;
namespace AntiDbt.Desktop;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool isSmoke = e.Args.Contains("--smoke-test") || e.Args.Contains("--smoke");
        var window = new MainWindow(testMode: isSmoke);
        MainWindow = window;
        if (isSmoke)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var dir = e.Args.SkipWhile(x => x != "--smoke-test" && x != "--smoke").Skip(1).FirstOrDefault() ?? "artifacts";
            try
            {
                await DesktopSmoke.RunAsync(window, dir);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "desktop-smoke-error.txt"), ex.ToString());
                }
                catch { }
                Console.Error.WriteLine($"[DesktopSmoke FAIL] {ex}");
                Shutdown(1);
            }
        }
        else
            window.Show();
    }
}
