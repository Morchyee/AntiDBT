using System.IO;
using System.Windows;
namespace AntiDbt.Desktop;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool localAsrSmoke = e.Args.Contains("--local-asr-smoke");
        bool isSmoke = localAsrSmoke || e.Args.Contains("--smoke-test") || e.Args.Contains("--smoke");
        if (!isSmoke && Environment.GetEnvironmentVariable("ANTIDBT_CAPTION_TRACE") == "1")
            AntiDbt.Core.CaptionDiagnostics.EnableFile(Path.Combine(AppContext.BaseDirectory, "data", "diagnostics", "captions.jsonl"));
        var window = new MainWindow(testMode: isSmoke);
        MainWindow = window;
        if (isSmoke)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var dir = localAsrSmoke ? e.Args.ElementAtOrDefault(2) ?? "artifacts" : e.Args.SkipWhile(x => x != "--smoke-test" && x != "--smoke").Skip(1).FirstOrDefault() ?? "artifacts";
            try
            {
                if (localAsrSmoke) await LocalAsrReleaseSmoke.RunAsync(window, e.Args, dir);
                else await DesktopSmoke.RunAsync(window, dir);
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
