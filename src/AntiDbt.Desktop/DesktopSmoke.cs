using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
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
            if (directory.Contains("ui-red", StringComparison.OrdinalIgnoreCase)) await Phase4DesktopSmoke.BaselineWheelAsync(directory);
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await SentenceDesktopSmoke.RunAsync(window, directory, Check);
            var store = window.TestStore;
            var generation = store.BeginSession();
            store.ApplySource(generation, new("1", 1, "Live subtitles show the original immediately.", false, TimeSpan.Zero, TimeSpan.FromSeconds(2), "en"));
            await Task.Delay(150);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check("source visible before translation", window.TestSource.Contains("original immediately"));
            Check("pending translation placeholder removed", window.TestTarget == "" || !window.TestTarget.Contains("等待译文"));
            Capture(window, Path.Combine(directory, "desktop-source-pending.png"));
            store.ApplyTranslation(generation, "1", 1, "实时字幕立即显示原文。");
            await Task.Delay(100);
            Check("translation visible", window.TestTarget.Contains("立即显示"));
            store.ApplySource(generation, new("1", 2, "Live subtitles show the original immediately and clearly.", true, TimeSpan.Zero, TimeSpan.FromSeconds(3), "en"));
            await Task.Delay(100);
            Check("prefix extension replaces source and retains draft without placeholder", window.TestSource.Contains("clearly") && window.TestTarget.Contains("立即显示") && !window.TestTarget.Contains("待更新"));
            store.ApplyTranslation(generation, "1", 2, "实时字幕立即且清晰显示原文。");
            await Task.Delay(100);

            // Verify incompatible semantic revision retains display fallback
            store.ApplySource(generation, new("1", 3, "Live subtitles do not show the original immediately.", true, TimeSpan.Zero, TimeSpan.FromSeconds(3), "en"));
            await Task.Delay(100);
            Check("incompatible revision retains visible translation", window.TestTarget.Contains("立即"));

            var stickyFrames = new List<string>();
            var stickyParagraph = window.TestVisibleParagraphs.Single();
            EventHandler sampleStickyFrame = (_, _) => stickyFrames.Add(window.TestVisibleParagraphs.Single().Translation);
            CompositionTarget.Rendering += sampleStickyFrame;
            try
            {
                await Task.Delay(750);
                window.SetExpanded(true);
                await Task.Delay(750);
                Check("sticky translation sampled actual WPF frames", stickyFrames.Count > 0);
                Check("sticky translation has no blank WPF frames across compact and expanded", stickyFrames.All(x => x == "实时字幕立即且清晰显示原文。"));
                Check("revision retains same paragraph instance", ReferenceEquals(stickyParagraph, window.TestVisibleParagraphs.Single()));
                store.ApplyTranslation(generation, "1", 3, "实时字幕未立即显示原文。");
                await Task.Delay(100);
                Check("sticky translation atomically replaced in same paragraph", ReferenceEquals(stickyParagraph, window.TestVisibleParagraphs.Single()) && stickyParagraph.Translation == "实时字幕未立即显示原文。");
            }
            finally { CompositionTarget.Rendering -= sampleStickyFrame; }
            File.WriteAllText(Path.Combine(directory, "sticky-wpf-frames.json"), JsonSerializer.Serialize(new { delayMilliseconds = 1500, frames = stickyFrames }, new JsonSerializerOptions { WriteIndented = true }));
            window.SetExpanded(false);

            Capture(window, Path.Combine(directory, "desktop-compact.png"));
            store.ApplySource(generation, new("1", 4, string.Join(" ", Enumerable.Repeat("Latest words remain visible in a long sentence", 12)) + ".", true, TimeSpan.Zero, TimeSpan.FromSeconds(3), "en"));
            await Task.Delay(150);
            Check("compact long sentence follows its last two lines", window.TestCompactOffset > 0);
            Capture(window, Path.Combine(directory, "desktop-compact-long.png"));
            store.ApplySource(generation, new("1", 5, "Live subtitles show the original immediately.", true, TimeSpan.Zero, TimeSpan.FromSeconds(3), "en"));
            store.ApplyTranslation(generation, "1", 5, "实时字幕立即显示原文。");
            await Task.Delay(100);

            // Verify continuous speech merges into single paragraph without forced newline
            var contStore = new SubtitleStore();
            var contGen = contStore.BeginSession();
            contStore.ApplySource(contGen, new("c1", 1, "Hello, how are you?", true, TimeSpan.Zero, TimeSpan.FromSeconds(1), "en"));
            contStore.ApplyTranslation(contGen, "c1", 1, "你好，你好吗？");
            contStore.ApplySource(contGen, new("c2", 1, "I'm fine, thank you.", true, TimeSpan.FromMilliseconds(1200), TimeSpan.FromSeconds(2), "en"));
            contStore.ApplyTranslation(contGen, "c2", 1, "我很好，谢谢你。");
            var contParagraphs = SubtitleProjection.Expanded(contStore.Snapshot());
            Check("continuous speech merged into single paragraph without forced newline", contParagraphs.Count == 1 && contParagraphs[0].Source == "Hello, how are you? I'm fine, thank you." && contParagraphs[0].Translation == "你好，你好吗？我很好，谢谢你。");

            for (int i = 2; i < 50; i++)
            {
                store.ApplySource(generation, new(i.ToString(), 1, $"History sentence {i}.", true, TimeSpan.FromSeconds(i * 4), TimeSpan.FromSeconds(i * 4 + 1), "en"));
                store.ApplyTranslation(generation, i.ToString(), 1, $"历史字幕第 {i} 句。");
            }
            window.SetExpanded(true);
            await Task.Delay(150);
            Check("expanded history bound", window.TestHistoryCount == 49);
            Check("resize mode is clean CanResize without grip", window.ResizeMode == ResizeMode.CanResize);
            Check("scrollbar style registered in window resources", window.TryFindResource(typeof(ScrollBar)) is not null);
            Capture(window, Path.Combine(directory, "desktop-expanded.png"));
            var scroller = window.TestScroller ?? throw new InvalidOperationException("history scroll viewer missing");
            scroller.ScrollToTop();
            await Task.Delay(150);
            Check("manual upscroll suspends follow", !window.TestFollowing);
            store.ApplySource(generation, new("50", 1, "New sentence while reading history.", true, TimeSpan.FromSeconds(50 * 4), TimeSpan.FromSeconds(50 * 4 + 1), "en"));
            await Task.Delay(150);
            Check("new subtitle preserves manual history position", !window.TestFollowing && scroller.VerticalOffset < 3);
            scroller.ScrollToBottom();
            await Task.Delay(150);
            Check("returning to bottom resumes follow", window.TestFollowing);

            // Verify compact mode scrolling works and does not resize window
            window.SetExpanded(false);
            await Task.Delay(150);
            Check("compact mode scroller active", window.TestScroller is not null);
            var compScroller = window.TestScroller!;
            compScroller.ScrollToTop();
            await Task.Delay(150);
            Check("compact mode upscroll suspends follow", !window.TestFollowing);
            compScroller.ScrollToBottom();
            await Task.Delay(150);
            Check("compact mode returning to bottom resumes follow", window.TestFollowing);

            // Phase 5.2: Test adaptive window resizing and history space utilization
            window.SetExpanded(true);
            window.TestSetUserResized(true);
            window.Height = 650;
            window.Width = 900;
            await Task.Delay(150);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check("history stretches to fill available window height without empty void", window.TestHistory.ActualHeight > 500);
            Check("bottom button overlays at bottom-right", window.TestBottomButton.VerticalAlignment == VerticalAlignment.Bottom && window.TestBottomButton.HorizontalAlignment == HorizontalAlignment.Right);
            Capture(window, Path.Combine(directory, "desktop-stretched-tall.png"));

            // Test window stretched wide
            window.Width = 1100;
            window.Height = 450;
            await Task.Delay(150);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check("window adapts to wide layout", window.TestHistory.ActualWidth > 1000);
            Capture(window, Path.Combine(directory, "desktop-stretched-wide.png"));

            // Reset to default size
            window.Width = 680;
            window.SetExpanded(false);

            var settings = new SettingsWindow(ProviderCatalog.DefaultSettings()) { Owner = window };
            settings.Show();
            await Task.Delay(150);
            Check("settings window renders", settings.ActualHeight > 0 && settings.ActualWidth > 0);
            Capture(settings, Path.Combine(directory, "desktop-settings.png"));

            // Phase 5.2: Test background opacity slider and real-time live preview
            Check("opacity slider present", settings.TestOpacitySlider is not null);
            var initialOpacity = settings.TestOpacitySlider!.Value;
            Check("default opacity is 80%", Math.Abs(initialOpacity - 80) < 0.1);

            // Test live opacity preview
            settings.TestOpacity(50);
            await Task.Delay(50);
            var surfaceBrush50 = (SolidColorBrush)window.Resources["Surface"];
            Check("live opacity 50% reflects in surface alpha", Math.Abs(surfaceBrush50.Color.A - 128) <= 2);
            Capture(window, Path.Combine(directory, "desktop-opacity-50pct.png"));

            settings.TestOpacity(20);
            await Task.Delay(50);
            var surfaceBrush20 = (SolidColorBrush)window.Resources["Surface"];
            Check("live opacity 20% reflects in surface alpha", Math.Abs(surfaceBrush20.Color.A - 51) <= 2);
            Capture(window, Path.Combine(directory, "desktop-opacity-20pct.png"));

            // Test opacity 0% clamping to alpha 1 (hit-testing safe, no click-through)
            settings.TestOpacity(0);
            await Task.Delay(50);
            var surfaceBrush0 = (SolidColorBrush)window.Resources["Surface"];
            Check("opacity 0% maintains minimum alpha 1 to prevent click-through", surfaceBrush0.Color.A == 1);

            // Restore to 80% and save
            settings.TestOpacity(80);
            await Task.Delay(50);
            Capture(window, Path.Combine(directory, "desktop-opacity-80pct.png"));

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
            Check("saved settings includes background opacity", Math.Abs(settings.Result.BackgroundOpacity - 0.80) < 0.01);
            var restored = new SettingsWindow(settings.Result);
            Check("saved profile reopening retains credential deletion", restored.TestTranslation.Get("ApiKey") == "" && restored.TestTranslation.Get("Endpoint") == "https://vendor-b.example/v1");
            Check("reopened settings retains background opacity", Math.Abs(restored.TestOpacitySlider!.Value - 80) < 0.1);
            restored.Close();
            var dialogToken = settings.TestLifetimeToken;
            settings.Close();
            Check("closing settings cancels model request lifetime", dialogToken.IsCancellationRequested);
            var beforeLanguageSwitch = window.TestSettings;
            await window.TestApplySettingsResultAsync(beforeLanguageSwitch with { TargetLanguage = "ja" });
            Check("settings target-language switch clears old translations and fallback", store.Snapshot().Count == 0);
            await window.TestApplySettingsResultAsync(beforeLanguageSwitch);
            window.SetExpanded(false);
            await window.StartDemoAsync();
            await Task.Delay(2800);
            Check("demo pipeline updates source", window.TestSource.Contains("Live subtitles"));
            await Phase4DesktopSmoke.RunAsync(window, directory, Check);
        }
        finally { await File.WriteAllTextAsync(Path.Combine(directory, "desktop-smoke.json"), JsonSerializer.Serialize(new { at = DateTimeOffset.Now, results }, new JsonSerializerOptions { WriteIndented = true })); await File.WriteAllTextAsync(Path.Combine(directory, "caption-trace.json"), JsonSerializer.Serialize(CaptionDiagnostics.Snapshot())); await window.CloseAsync(); }
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


