using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AntiDbt.Core;
using AntiDbt.Providers;
namespace AntiDbt.Desktop;
internal static class DesktopSmoke
{
    public static async Task RunAsync(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<object>();
        void Check(string name, bool pass)
        {
            results.Add(new
            {
                name,
                pass
            });
            if (!pass)
                throw new InvalidOperationException(name);
        }
        try
        {
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var store = window.TestStore;
            var generation = store.BeginSession();
            store.ApplySource(generation, new("1", 1, "Live subtitles show the original immediately.", false, TimeSpan.Zero, TimeSpan.FromSeconds(2), "en"));
            await Task.Delay(150);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check("source visible before translation", window.TestSource.Contains("original immediately"));
            Check("pending translation placeholder", window.TestTarget == "等待译文…");
            Capture(window, Path.Combine(directory, "desktop-source-pending.png"));
            store.ApplyTranslation(generation, "1", 1, "实时字幕立即显示原文。");
            await Task.Delay(100);
            Check("translation visible", window.TestTarget.Contains("立即显示"));
            store.ApplySource(generation, new("1", 2, "Live subtitles revise the original immediately.", true, TimeSpan.Zero, TimeSpan.FromSeconds(3), "en"));
            await Task.Delay(100);
            Check("revision replaces source and labels retained draft", window.TestSource.Contains("revise") && window.TestTarget.Contains("待更新"));
            store.ApplyTranslation(generation, "1", 2, "实时字幕立即修正原文。");
            await Task.Delay(100);
            Capture(window, Path.Combine(directory, "desktop-compact.png"));
            store.ApplySource(generation, new("1", 3, string.Join(" ", Enumerable.Repeat("Latest words remain visible in a long sentence", 12)) + ".", true, TimeSpan.Zero, TimeSpan.FromSeconds(3), "en"));
            await Task.Delay(150);
            Check("compact long sentence follows its last two lines", window.TestCompactOffset > 0);
            Capture(window, Path.Combine(directory, "desktop-compact-long.png"));
            store.ApplySource(generation, new("1", 4, "Live subtitles revise the original immediately.", true, TimeSpan.Zero, TimeSpan.FromSeconds(3), "en"));
            store.ApplyTranslation(generation, "1", 4, "实时字幕立即修正原文。");
            await Task.Delay(100);
            for (int i = 2; i < 50; i++)
            {
                store.ApplySource(generation, new(i.ToString(), 1, $"History sentence {i}.", true, TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 1), "en"));
                store.ApplyTranslation(generation, i.ToString(), 1, $"历史字幕第 {i} 句。");
            }
            window.SetExpanded(true);
            await Task.Delay(150);
            Check("expanded history bound", window.TestHistoryCount == 49);
            Capture(window, Path.Combine(directory, "desktop-expanded.png"));
            var scroller = window.TestScroller ?? throw new InvalidOperationException("history scroll viewer missing");
            scroller.ScrollToTop();
            await Task.Delay(150);
            Check("manual upscroll suspends follow", !window.TestFollowing);
            store.ApplySource(generation, new("50", 1, "New sentence while reading history.", true, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(51), "en"));
            await Task.Delay(150);
            Check("new subtitle preserves manual history position", !window.TestFollowing && scroller.VerticalOffset < 3);
            scroller.ScrollToBottom();
            await Task.Delay(150);
            Check("returning to bottom resumes follow", window.TestFollowing);
            var settings = new SettingsWindow(ProviderCatalog.DefaultSettings()) { Owner = window };
            settings.Show();
            await Task.Delay(150);
            Check("settings window renders", settings.ActualHeight > 0 && settings.ActualWidth > 0);
            Capture(settings, Path.Combine(directory, "desktop-settings.png"));
            var asrEditor = settings.TestAsr; var mtEditor = settings.TestTranslation;
            asrEditor.Set("ApiKey", "synthetic-asr");
            mtEditor.Select("DeepL"); mtEditor.Set("ApiKey", "synthetic-mt");
            asrEditor.Select("GroqWhisper");
            Check("provider selectors independent and credentials isolated", mtEditor.Id == "DeepL" && asrEditor.Get("ApiKey") == "");
            Check("Groq Whisper final-only billing capability", asrEditor.Capability.Contains("仅 Final") && asrEditor.Capability.Contains("10秒"));
            asrEditor.Select("VolcengineAsr2");
            Check("ASR profile restores edited key", asrEditor.Get("ApiKey") == "synthetic-asr");
            asrEditor.ClearSecrets(); asrEditor.Select("AzureSpeech"); asrEditor.Select("VolcengineAsr2");
            Check("cleared ASR secrets stay cleared without changing MT", asrEditor.Get("ApiKey") == "" && mtEditor.Get("ApiKey") == "synthetic-mt");
            asrEditor.Select("GoogleCloudSpeech");
            Check("service account JSON uses password control", asrEditor.IsSecret("serviceAccountJson"));
            asrEditor.Set("serviceAccountJson", "synthetic-json"); asrEditor.Select("AzureSpeech"); asrEditor.Select("GoogleCloudSpeech");
            Check("service account secret restores independently", asrEditor.Get("serviceAccountJson") == "synthetic-json");
            asrEditor.ClearSecrets();
            Check("service account secret deletion", asrEditor.Get("serviceAccountJson") == "");
            Check("SDK streaming capability displayed", asrEditor.Capability.Contains("StreamingRecognize"));
            mtEditor.Select("GroqLlm"); mtEditor.Set("Model", "synthetic-model"); mtEditor.Select("DeepL"); mtEditor.Select("GroqLlm");
            Check("translation model profile restores", mtEditor.Get("Model") == "synthetic-model" && mtEditor.Get("ApiKey") == "");
            mtEditor.Select("OpenAiCompatible"); mtEditor.Set("Endpoint", "https://vendor-a.example/v1"); mtEditor.Set("ApiKey", "synthetic-host-key"); mtEditor.Snapshot();
            mtEditor.Set("Endpoint", "https://vendor-a.example/other-path"); mtEditor.Snapshot();
            Check("same authority path edit retains credential", mtEditor.Get("ApiKey") == "synthetic-host-key");
            mtEditor.Set("Endpoint", "https://vendor-b.example/v1"); mtEditor.Select("DeepL"); mtEditor.Select("OpenAiCompatible");
            Check("custom authority edit clears old credential", mtEditor.Get("ApiKey") == "");
            mtEditor.Select("DeepL");
            Check("custom authority edit preserves other provider credential", mtEditor.Get("ApiKey") == "synthetic-mt");
            mtEditor.Select("OpenAiCompatible");
            settings.TestSave();
            Check("saved result preserves destination and cleared credential", settings.Result.Translation.Endpoint == "https://vendor-b.example/v1" && settings.Result.Translation.ApiKey == "" && settings.Result.TranslationProfiles["DeepL"].ApiKey == "synthetic-mt");
            var restored = new SettingsWindow(settings.Result);
            Check("saved profile reopening retains credential deletion", restored.TestTranslation.Get("ApiKey") == "" && restored.TestTranslation.Get("Endpoint") == "https://vendor-b.example/v1");
            restored.Close();
            var dialogToken = settings.TestLifetimeToken;
            settings.Close();
            Check("closing settings cancels model request lifetime", dialogToken.IsCancellationRequested);
            window.SetExpanded(false);
            await window.StartDemoAsync();
            await Task.Delay(2800);
            Check("demo pipeline updates source", window.TestSource.Contains("Live subtitles"));
        }
        finally { await File.WriteAllTextAsync(Path.Combine(directory, "desktop-smoke.json"), JsonSerializer.Serialize(new { at = DateTimeOffset.Now, results }, new JsonSerializerOptions { WriteIndented = true })); window.Close(); }
    }
    static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
