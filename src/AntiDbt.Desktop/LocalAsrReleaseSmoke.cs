using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AntiDbt.Core;
using AntiDbt.Providers;
using AntiDbt.Windows;

namespace AntiDbt.Desktop;

// Runs inside the self-contained EXE. Input audio and reports stay outside the bundle.
internal static class LocalAsrReleaseSmoke
{
    internal static async Task RunAsync(MainWindow window, string[] args, string directory)
    {
        if (args.Length < 3) throw new ArgumentException("--local-asr-smoke <English WAV> <report directory> [--mt-settings <existing data directory>]");
        Directory.CreateDirectory(directory);
        var settings = new AppSettings { SourceLanguage = "en", TargetLanguage = "en",
            Asr = new() { Provider = "SherpaOnnx", Language = "en", Model = "models/sherpa-onnx" } };
        int mtIndex = Array.IndexOf(args, "--mt-settings");
        if (mtIndex >= 0)
        {
            var saved = new SettingsStore(args[mtIndex + 1]);
            var loaded = await saved.LoadAsync();
            if (saved.Warning is not null || string.IsNullOrWhiteSpace(loaded.Translation.Provider))
                throw new InvalidOperationException("Saved MT configuration unavailable");
            settings = settings with { TargetLanguage = "zh", Translation = loaded.Translation };
        }
        var model = SherpaOnnxStreamingAsrProvider.ResolveModelDirectory(settings.Asr);
        if (!Path.GetFullPath(model).StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Model resolved outside the portable directory");
        // Exercise the same settings, WASAPI, pipeline and renderer as the Start button.
        window.TestConfigure(settings);
        window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        int renderedFrames = 0;
        EventHandler render = (_, _) => {
            if (SentenceDesktopSmoke.TextBoxes(window.TestHistory).Any(x => x.Text.Length > 0)) renderedFrames++;
        };
        CompositionTarget.Rendering += render;
        var process = Process.GetCurrentProcess();
        var initialCpu = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        string outcome = "failed";
        try
        {
            await window.TestStartAsync();
            if (!window.TestRunning) throw new InvalidOperationException("Local ASR failed to start");
            using var player = new SoundPlayer(Path.GetFullPath(args[1]));
            player.Load();
            for (int i = 0; i < 3; i++)
            {
                await Task.Run(player.PlaySync).WaitAsync(TimeSpan.FromSeconds(30));
                await Task.Delay(800);
            }
            await Task.Delay(1800);
            await window.TestStopCaptureAsync().WaitAsync(TimeSpan.FromSeconds(45));
            await Task.Delay(500);
            var rows = window.TestStore.Snapshot();
            if (rows.Count < 2 || rows.Any(x => !x.IsFinal) || renderedFrames == 0)
                throw new InvalidOperationException("Missing continuous final English subtitles or rendered frames");
            if (mtIndex >= 0 && rows.Any(x => x.TranslationError is not null || x.TranslationText.Length == 0))
                throw new InvalidOperationException("Real MT did not translate every final segment");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var image = File.Create(Path.Combine(directory, "live-subtitles.png"))) png.Save(image);
            outcome = "passed";
        }
        finally
        {
            CompositionTarget.Rendering -= render;
            await window.TestStopCaptureAsync().WaitAsync(TimeSpan.FromSeconds(45));
            process.Refresh();
            var rows = window.TestStore.Snapshot();
            var cpu = (process.TotalProcessorTime - initialCpu).TotalSeconds;
            var report = new { outcome, audio = "English WAV played three times through Windows default output and captured with real WASAPI loopback",
                mt = mtIndex >= 0 ? settings.Translation.Provider : "en -> en passthrough; no API key or network",
                modelRelativeToExe = Path.GetRelativePath(AppContext.BaseDirectory, model),
                elapsedSeconds = clock.Elapsed.TotalSeconds, cpuSeconds = cpu,
                averageCpuPercentOfMachine = 100 * cpu / clock.Elapsed.TotalSeconds / Environment.ProcessorCount,
                renderedFrames, segments = rows.Count, translationErrors = rows.Count(x => x.TranslationError is not null),
                captions = rows.Select(x => new { x.SourceText, x.TranslationText, x.IsFinal }) };
            await File.WriteAllTextAsync(Path.Combine(directory, "local-asr-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
