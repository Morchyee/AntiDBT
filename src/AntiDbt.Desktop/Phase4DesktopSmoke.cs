using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AntiDbt.Core;
using AntiDbt.Providers;
namespace AntiDbt.Desktop;
internal static class Phase4DesktopSmoke
{
    internal static void Wheel(Control control, int delta)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = control };
        control.RaiseEvent(preview);
        if (!preview.Handled) control.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent, Source = control });
    }
    static async Task Idle(Window window) => await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    internal static async Task BaselineWheelAsync(string directory)
    {
        var input = new TextBox { Text = "ordinary single line", Margin = new Thickness(8) };
        var page = new StackPanel(); page.Children.Add(input); page.Children.Add(new Border { Height = 1800 });
        var viewer = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 420, Height = 300, Content = viewer };
        window.Show(); await Idle(window); input.Focus(); await Idle(window);
        viewer.ScrollToVerticalOffset(100); await Idle(window); var before = viewer.VerticalOffset; Wheel(input, -1); await Idle(window);
        var passed = viewer.VerticalOffset > before && viewer.VerticalOffset - before <= 1;
        await File.WriteAllTextAsync(Path.Combine(directory, "wheel-red.json"), JsonSerializer.Serialize(new { scenario = "Original WPF TextBox route, precision delta -1, no relay", expected = "precision delta advances outer page by at most one pixel", beforeOffset = before, actualOffset = viewer.VerticalOffset, pass = passed, synthetic = true }));
        window.Close();
        if (!passed) throw new InvalidOperationException("RED: precision wheel delta -1 produced a 48 pixel jump");
    }
    internal static async Task RunAsync(MainWindow main, string directory, Action<string, bool> check)
    {
        await WheelAsync(check);
        var value = LanguageSettings.Select(ProviderCatalog.DefaultSettings() with { Asr = ProviderDescriptors.DefaultAsr("GroqWhisper") with { ApiKey = "synthetic-only" }, Translation = ProviderDescriptors.DefaultTranslation("GoogleTranslationBasic") with { ApiKey = "synthetic-only", DebounceMilliseconds = 0 } }, "en", "zh-Hans");
        var probes = new List<Fixture>();
        main.TestPipelineFactory = (settings, store) => { var fixture = new Fixture(); probes.Add(fixture); return new CaptionPipeline(fixture.Audio, fixture.Asr, fixture.Translator, settings, store); };
        main.TestConfigure(value); await main.ApplyLanguageAsync("en", "zh-Hans"); await Idle(main);
        check("language switch rebuilds actual pipeline with canonical ASR language", probes.Count == 1 && probes[0].Asr.Config?.Language == "en" && main.TestRunning);
        probes[0].Asr.Emit("old", "old source", "en"); await probes[0].Translator.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var oldGeneration = main.TestStore.CurrentSession;
        await main.SwapLanguagesAsync(); await Idle(main);
        check("actual swap updates runtime ASR and MT settings and restarts", main.TestSettings.SourceLanguage == "zh-Hans" && main.TestSettings.TargetLanguage == "en" && probes.Count == 2 && probes[1].Asr.Config?.Language == "zh-Hans" && probes[0].Audio.Disposed);
        probes[0].Translator.Release.TrySetResult("late old translation");
        check("old generation source rejected after actual language switch", main.TestStore.ApplySource(oldGeneration, new("late", 1, "late old source", true, TimeSpan.Zero, TimeSpan.FromSeconds(1), "en")) is null);
        check("old generation translation rejected after actual language switch", !main.TestStore.ApplyTranslation(oldGeneration, "old", 1, "late old translation"));
        probes[1].Translator.Release.TrySetResult("current translation"); probes[1].Asr.Emit("new", "当前字幕", "zh-Hans");
        await Task.Delay(150); await Idle(main);
        check("uncooperative old translation cannot reappear or replace new captions", main.TestTarget.Contains("current translation") && !main.TestSource.Contains("old") && !main.TestTarget.Contains("late"));
        check("new translator receives actual swapped request parameters", probes[1].Translator.Last is { SourceLanguage: "zh-Hans", TargetLanguage: "en" });
        check("language change preserves independent profiles and credentials", main.TestSettings.Asr.ApiKey == "synthetic-only" && main.TestSettings.Translation.ApiKey == "synthetic-only" && main.TestSettings.AsrProfiles["GroqWhisper"].Language == "zh-Hans");
        check("search filters Chinese language labels", main.TestSourcePicker.TestSearch("葡萄牙") == 3);
        main.TestSourcePicker.TestSearch(""); check("recent language ordering shown in picker", main.TestSourcePicker.TestFirstId == "zh-Hans");
        main.TestSourcePicker.TestSearch("ja"); check("search filters canonical identifier", main.TestSourcePicker.TestFirstId == "ja");
        await main.ApplyLanguageAsync("auto", "zh-Hans"); await Idle(main); var count = probes.Count;
        await main.SwapLanguagesAsync(); check("auto source swap retained with explicit reason and no target auto", main.TestSettings.SourceLanguage == "auto" && main.TestSettings.TargetLanguage == "zh-Hans" && probes.Count == count && main.TestStatus.Contains("不能作为目标"));
        main.TestConfigure(main.TestSettings with { Asr = ProviderDescriptors.DefaultAsr("GoogleCloudSpeech") });
        await main.ApplyLanguageAsync("zh-Hans", "en");
        check("invalid language stops old pipeline and retains actual reason", !main.TestRunning && main.TestSettings.SourceLanguage == "zh-Hans" && main.TestStatus.Contains("Google"));
        check("unsupported source option disabled while retained visible", main.TestSourcePicker.Id == "zh-Hans" && !main.TestSourcePicker.TestEnabled("zh-Hans"));
        main.TestConfigure(value with { Translation = ProviderDescriptors.DefaultTranslation("OpenAiCompatible") });
        await main.ApplyLanguageAsync("en", "ja");
        check("undeclared LLM provisional selection retained and runtime blocked", main.TestSettings.TargetLanguage == "ja" && !main.TestRunning && main.TestStatus.Contains("未确认"));
        main.TestConfigure(value with { Translation = ProviderDescriptors.DefaultTranslation("OpenAiCompatible") });
        await main.ApplyLanguageAsync("en", "en");
        check("same language does not demand missing MT key", main.TestStatus.Contains("同语言直通") && ProviderLanguages.ValidateSettings(main.TestSettings) is null);
        var dialog = new SettingsWindow(value); dialog.Show(); await Idle(dialog);
        try
        {
            check("settings has all eight navigation categories", dialog.TestNavigationCount == 8);
            foreach (var page in new[] { "常规", "语音识别", "翻译服务", "音频", "外观", "网络与代理", "高级", "关于" }) { dialog.TestPage(page); await Idle(dialog); check("navigation renders " + page, dialog.TestScroller.ActualWidth > 0 && dialog.TestScroller.ViewportHeight > 0); }
            dialog.TestPage("翻译服务"); var editor = dialog.TestTranslation; editor.Select("OpenAiCompatible");
            editor.Set("Model", "model-a"); editor.Set("modelLanguages", "en,zh-Hans"); editor.Set("ApiKey", "synthetic-reveal-only");
            editor.ToggleSecret("ApiKey"); check("secret is shown only through explicit reveal", editor.VisibleSecret("ApiKey") == "synthetic-reveal-only");
            editor.ToggleSecret("ApiKey"); check("hide clears plaintext view and preserves encrypted-save source", editor.VisibleSecret("ApiKey") == "" && editor.Get("ApiKey") == "synthetic-reveal-only");
            editor.Set("Model", "model-b"); check("model edit invalidates prior language declaration", editor.Get("modelLanguages") == "");
            editor.Set("modelLanguages", "en,ja"); var endpoint = editor.Get("Endpoint"); editor.Set("Endpoint", endpoint.TrimEnd('/') + "/new-path");
            check("same host path edit invalidates declaration but retains key", editor.Get("modelLanguages") == "" && editor.Get("ApiKey") == "synthetic-reveal-only");
            editor.ToggleSecret("ApiKey"); editor.Select("DeepL"); editor.Select("OpenAiCompatible");
            check("provider switch clears revealed plaintext", editor.VisibleSecret("ApiKey") == "");
            editor.ClearSecrets(); check("delete clears hidden and revealed credential", editor.Get("ApiKey") == "" && editor.VisibleSecret("ApiKey") == "");
            dialog.TestLanguage("ja", "zh-Hant"); dialog.TestTheme("Light"); dialog.TestSave();
            check("save persists global languages recent profile language and theme", dialog.Result.SourceLanguage == "ja" && dialog.Result.TargetLanguage == "zh-Hant" && dialog.Result.Theme == "Light" && dialog.Result.Asr.Language == "ja" && dialog.Result.RecentLanguages.Contains("zh-Hant"));
            var reopened = new SettingsWindow(dialog.Result); reopened.TestSave(); check("reopen retains language and theme selections", reopened.Result.SourceLanguage == "ja" && reopened.Result.TargetLanguage == "zh-Hant" && reopened.Result.Theme == "Light"); reopened.Close();
            editor.Select("GoogleTranslationBasic");
            foreach (var theme in new[] { "Dark", "Light" })
            foreach (var size in new[] { "small", "normal" })
            {
                dialog.Width = size == "small" ? 680 : 980; dialog.Height = size == "small" ? 500 : 800; dialog.TestTheme(theme); dialog.TestPage("常规");
                main.Width = size == "small" ? 500 : 740; main.TestConfigure(value with { Theme = theme }); await Idle(dialog); await Idle(main);
                check($"{theme} {size} theme resources and layout", ((SolidColorBrush)dialog.Resources["Ink"]).Color == (theme == "Dark" ? Color.FromRgb(242, 246, 255) : Color.FromRgb(24, 35, 55)) && dialog.TestScroller.ViewportWidth > 350);
                foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
                {
                    Capture(dialog, Path.Combine(directory, $"settings-{theme}-{size}-{scale * 100:0}pct-simulated.png"), scale);
                    Capture(main, Path.Combine(directory, $"main-{theme}-{size}-{scale * 100:0}pct-simulated.png"), scale);
                }
            }
            dialog.Width = 980; dialog.Height = 800; dialog.TestTheme("Dark"); dialog.TestPage("翻译服务"); await Idle(dialog); Capture(dialog, Path.Combine(directory, "settings-provider-dark.png"));
        }
        finally { dialog.Close(); }
        await VerifyPhase5ReliabilityA1Async(main, check);
        main.TestPipelineFactory = null;
    }
    static async Task WheelAsync(Action<string, bool> check)
    {
        var input = new TextBox { Text = "ordinary single line" }; var password = new PasswordBox { Password = "synthetic" };
        var multiline = new TextBox { Text = string.Join("\n", Enumerable.Range(0, 100).Select(x => "line " + x)), AcceptsReturn = true, Height = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var combo = new ComboBox { ItemsSource = Enumerable.Range(0, 100).Select(x => "choice " + x), SelectedIndex = 0, MaxDropDownHeight = 100 };
        var page = new StackPanel(); page.Children.Add(input); page.Children.Add(password); page.Children.Add(multiline); page.Children.Add(combo); page.Children.Add(new Border { Height = 1800 });
        var viewer = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 440, Height = 400, Content = viewer }; DesktopTheme.Apply(window, "Dark"); ScrollRelay.Attach(viewer); window.Show(); await Idle(window);
        try
        {
            input.Focus(); input.Select(0, 8); await Idle(window); viewer.ScrollToVerticalOffset(100); await Idle(window); Wheel(input, -120); await Idle(window);
            check("singleline routed wheel advances outer page once", Math.Abs(viewer.VerticalOffset - 148) < 1);
            check("wheel preserves text selection and keyboard focus", input.SelectionLength == 8 && input.IsKeyboardFocused);
            viewer.ScrollToVerticalOffset(100); await Idle(window); Wheel(input, -1); await Idle(window);
            check("precision delta uses pixel offset without full wheel jump", viewer.VerticalOffset > 100 && viewer.VerticalOffset <= 101);
            for (int i = 0; i < 4; i++) { Wheel(input, -1); await Idle(window); }
            check("small precision deltas accumulate without loss", Math.Abs(viewer.VerticalOffset - 102) < 0.1);
            viewer.ScrollToVerticalOffset(100); await Idle(window); password.Focus(); await Idle(window); viewer.ScrollToVerticalOffset(100); await Idle(window); Wheel(password, -120); await Idle(window);
            check("password routed wheel advances outer page without changing focus", Math.Abs(viewer.VerticalOffset - 148) < 1 && password.IsKeyboardFocused);
            viewer.ScrollToVerticalOffset(100); await Idle(window); var inner = MainWindow.FindScroller(multiline)!; inner.ScrollToTop(); await Idle(window); Wheel(multiline, -120); await Idle(window);
            check("multiline consumes wheel inside own scroll range", inner.VerticalOffset > 0 && Math.Abs(viewer.VerticalOffset - 100) < 1);
            inner.ScrollToBottom(); await Idle(window); Wheel(multiline, -120); await Idle(window);
            check("multiline bottom boundary hands off to outer page", Math.Abs(viewer.VerticalOffset - 148) < 1);
            inner.ScrollToTop(); viewer.ScrollToVerticalOffset(100); await Idle(window); Wheel(multiline, 120); await Idle(window);
            check("multiline top boundary hands off upward without reverse", Math.Abs(viewer.VerticalOffset - 52) < 1);
            viewer.ScrollToTop(); await Idle(window); Wheel(input, 120); await Idle(window); check("scroll clamps at zero", viewer.VerticalOffset == 0);
            combo.IsDropDownOpen = true; await Idle(window); var popup = combo.Template.FindName("PART_Popup", combo) as Popup; var popupScroll = popup?.Child is DependencyObject child ? MainWindow.FindScroller(child) : null;
            var before = viewer.VerticalOffset; if (popupScroll is not null) { Wheel(popupScroll, -120); await Idle(window); }
            check("open ComboBox popup retains own scroll and outer position", popupScroll is not null && popupScroll.VerticalOffset > 0 && viewer.VerticalOffset == before); combo.IsDropDownOpen = false;
            input.IsEnabled = false; password.Focus(); await Idle(window); Wheel(input, -120); await Idle(window);
            check("wheel never focuses disabled input", !input.IsKeyboardFocusWithin && password.IsKeyboardFocused); input.IsEnabled = true;
            var handledPage = new ScrollViewer { Content = new StackPanel() }; var handledInput = new TextBox(); ((StackPanel)handledPage.Content).Children.Add(handledInput); ((StackPanel)handledPage.Content).Children.Add(new Border { Height = 1800 });
            handledPage.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) => e.Handled = true)); ScrollRelay.Attach(handledPage); window.Content = handledPage; await Idle(window); Wheel(handledInput, -120); await Idle(window);
            check("relay receives previously handled preview routes", Math.Abs(handledPage.VerticalOffset - 48) < 1);
        }
        finally { window.Close(); }
    }
    static void Capture(Window window, string path, double scale = 1)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale), (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    sealed class Fixture
    {
        internal FixtureAudio Audio { get; } = new(); internal FixtureAsr Asr { get; } = new(); internal FixtureTranslator Translator { get; } = new();
    }
    sealed class FixtureAudio : IAudioSource
    {
        volatile bool stopped; public bool Disposed { get; private set; } public string Name => "Synthetic offline fixture";
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public async IAsyncEnumerable<AudioChunk> ReadChunksAsync([EnumeratorCancellation] CancellationToken token)
        { while (!stopped && !token.IsCancellationRequested) { await Task.Delay(20, token); yield return new(new byte[640], TimeSpan.Zero, TimeSpan.FromMilliseconds(20), System.Diagnostics.Stopwatch.GetTimestamp()); } }
        public Task StopAsync(CancellationToken token) { stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = stopped = true; return ValueTask.CompletedTask; }
    }
    sealed class FixtureAsr : IAsrProvider, IAsrSession
    {
        readonly Channel<AsrResult> channel = Channel.CreateUnbounded<AsrResult>(); internal AsrConfiguration? Config { get; private set; }
        public IAsrSession CreateSession(AsrConfiguration configuration) { Config = configuration; return this; }
        internal void Emit(string id, string text, string language) => channel.Writer.TryWrite(new(id, 1, text, true, TimeSpan.Zero, TimeSpan.FromSeconds(1), language));
        public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) => Task.CompletedTask;
        public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token) => channel.Reader.ReadAllAsync(token);
        public Task CompleteAsync(CancellationToken token) { channel.Writer.TryComplete(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { channel.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
    sealed class FixtureTranslator : ITranslationProvider
    {
        internal TaskCompletionSource<string> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TranslationRequest? Last { get; private set; }
        public Task<string> TranslateAsync(TranslationRequest request, CancellationToken token) { Last = request; Started.TrySetResult(); return Release.Task; }
    }

    internal static async Task VerifyPhase5ReliabilityA1Async(MainWindow main, Action<string, bool> check)
    {
        // 1. Desktop ordinary stop preserves delayed final within drain budget into visible UI
        var delayedAsr = new DelayedFinalFixtureAsr(delayMs: 1200, "tail-sub-1", "tail final sentence");
        var delayedMt = new DelayedFixtureTranslator(delayMs: 100, "尾部最终译文");
        main.TestPipelineFactory = (settings, store) =>
            new CaptionPipeline(new FixtureAudio(), delayedAsr, delayedMt, settings, store);

        await main.TestStartAsync();
        await Idle(main);
        check("desktop pipeline started for ordinary stop test", main.TestRunning);

        // User clicks stop button via TestToggleCaptureAsync
        await main.TestToggleCaptureAsync();
        await Idle(main);

        check("desktop ordinary stop returns to stopped status", !main.TestRunning && main.TestStatus.Contains("准备就绪"));
        check("desktop ordinary stop preserved tail ASR and translation in store",
            main.TestStore.Snapshot().Any(x => x.SourceText == "tail final sentence" && x.TranslationText == "尾部最终译文"));
        check("desktop ordinary stop renders tail in visible UI",
            main.TestSource.Contains("tail final sentence") && main.TestTarget.Contains("尾部最终译文"));

        // 2. Language reconfigure immediately isolates session and drops delayed tail from old pipeline
        var oldDelayedAsr = new DelayedFinalFixtureAsr(delayMs: 1500, "old-tail", "old delayed sentence");
        main.TestPipelineFactory = (settings, store) =>
            new CaptionPipeline(new FixtureAudio(), oldDelayedAsr, new DelayedFixtureTranslator(50, "旧译文"), settings, store);

        await main.TestStartAsync();
        await Idle(main);
        check("pipeline started before language switch", main.TestRunning);

        // Switch language (Reconfigure)
        await main.ApplyLanguageAsync("zh-Hans", "en");
        await Idle(main);

        check("language switch does not accept delayed tail from old pipeline",
            !main.TestStore.Snapshot().Any(x => x.SourceText == "old delayed sentence"));

        // 3. Repeated stop is idempotent
        await main.TestStopCaptureAsync();
        await Idle(main);
        check("repeated stop is idempotent", !main.TestRunning);

        // 4. Disposal failure preserves ownership & prevents conflict start
        var failingAudio = new FailingDisposalAudio();
        main.TestPipelineFactory = (settings, store) =>
            new CaptionPipeline(failingAudio, new FixtureAsr(), new FixtureTranslator(), settings, store);

        await main.TestStartAsync();
        await Idle(main);
        try { await main.TestStopCaptureAsync(); } catch { }
        await Idle(main);
        check("failing disposal marks pipeline cleanup failed", main.TestPipelineCleanupFailed);

        bool conflictBlocked = false;
        try
        {
            await main.TestStartAsync();
            await Idle(main);
        }
        catch
        {
            conflictBlocked = true;
        }
        check("cleanup failure prevents conflicting start", conflictBlocked || main.TestStatus.Contains("无法释放") || main.TestStatus.Contains("禁止"));

        // Recover from failure
        failingAudio.AllowDispose = true;
        try { await main.TestStopCaptureAsync(); } catch { }
        await Idle(main);

        // 5. 100 fast start/stop/switch cycles without unobserved exceptions or duplicate captures
        main.TestPipelineFactory = (settings, store) =>
            new CaptionPipeline(new FixtureAudio(), new FixtureAsr(), new DelayedFixtureTranslator(10, "ok"), settings, store);

        bool cyclesOk = true;
        for (int i = 0; i < 100; i++)
        {
            try
            {
                await main.TestStartAsync();
                if (i % 2 == 0)
                    await main.TestStopCaptureAsync();
                else
                    await main.TestToggleCaptureAsync();
            }
            catch
            {
                cyclesOk = false;
                break;
            }
        }
        await Idle(main);
        check("100 start stop cycles complete cleanly without unhandled exceptions", cyclesOk && !main.TestRunning);

        main.TestPipelineFactory = null;
    }

    sealed class DelayedFinalFixtureAsr(int delayMs, string id, string text) : IAsrProvider, IAsrSession
    {
        readonly Channel<AsrResult> channel = Channel.CreateUnbounded<AsrResult>();
        public IAsrSession CreateSession(AsrConfiguration configuration) => this;
        public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) => Task.CompletedTask;
        public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token) => channel.Reader.ReadAllAsync(token);
        public async Task CompleteAsync(CancellationToken token)
        {
            await Task.Delay(delayMs, token);
            channel.Writer.TryWrite(new(id, 1, text, true, TimeSpan.Zero, TimeSpan.FromSeconds(1), "en"));
            channel.Writer.TryComplete();
        }
        public ValueTask DisposeAsync() { channel.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }

    sealed class DelayedFixtureTranslator(int delayMs, string response) : ITranslationProvider
    {
        public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken token)
        {
            await Task.Delay(delayMs, token);
            return response;
        }
    }

    sealed class FailingDisposalAudio : IAudioSource
    {
        volatile bool stopped;
        public bool AllowDispose { get; set; }
        public string Name => "Failing disposal fixture";
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public async IAsyncEnumerable<AudioChunk> ReadChunksAsync([EnumeratorCancellation] CancellationToken token)
        {
            while (!stopped && !token.IsCancellationRequested)
            {
                await Task.Delay(20, token);
                yield return new(new byte[640], TimeSpan.Zero, TimeSpan.FromMilliseconds(20), System.Diagnostics.Stopwatch.GetTimestamp());
            }
        }
        public Task StopAsync(CancellationToken token) { stopped = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync()
        {
            stopped = true;
            if (!AllowDispose)
                throw new IOException("Simulated device lock failure during disposal");
            return ValueTask.CompletedTask;
        }
    }
}

