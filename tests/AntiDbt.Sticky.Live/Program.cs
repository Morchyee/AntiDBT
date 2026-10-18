using AntiDbt.Core;
using AntiDbt.Providers;
using AntiDbt.Windows;
using System.Diagnostics;
using System.Text.Json;

// Opt-in only: dotnet run --project tests/AntiDbt.Sticky.Live -- <settings-directory> <output-json>
if (args.FirstOrDefault() == "--sentence-live") return await SentenceLive.RunAsync(args);
if (args.FirstOrDefault() == "--sentence-file") return await SentenceFile.RunAsync(args);
if (args.Length != 2) { Console.WriteLine("Expected settings-directory and output-json. This test calls the configured real MT service."); return 2; }
var frames = new List<object>();
int partials = 0, finals = 0, calls = 0, blanks = 0;
string provider = "", outcome = "failed", error = "";
try
{
    var settingsStore = new SettingsStore(Path.GetFullPath(args[0]));
    var settings = await settingsStore.LoadAsync();
    provider = settings.Translation.Provider;
    if (settingsStore.Warning != null || string.IsNullOrWhiteSpace(provider))
        throw new InvalidOperationException("Configured translation service unavailable");
    var model = Path.GetFullPath("models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17");
    var config = new AsrConfiguration { Provider = "SherpaOnnx", Language = "en", Model = model, Options = new() { ["numThreads"] = "2" } };
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    await using var asr = new SherpaOnnxStreamingAsrProvider().CreateSession(config);
    await asr.ConnectAsync(limit.Token);
    var events = new List<AsrResult>();
    var reader = Task.Run(async () => { await foreach (var result in asr.GetResultsAsync(limit.Token)) events.Add(result); });
    var audio = await File.ReadAllBytesAsync(Path.Combine(model, "test_wavs", "0.wav"), limit.Token);
    for (int offset = 44; offset < audio.Length; offset += 6400)
    {
        await asr.SendAudioAsync(audio.AsMemory(offset, Math.Min(6400, audio.Length - offset)), limit.Token);
        await Task.Delay(20, limit.Token);
    }
    await asr.CompleteAsync(limit.Token);
    await reader;
    partials = events.Count(x => !x.IsFinal);
    finals = events.Count(x => x.IsFinal);
    if (partials < 2 || finals == 0) throw new InvalidOperationException("Missing real partial/final events");
    var store = new SubtitleStore();
    var generation = store.BeginSession();
    var clock = Stopwatch.StartNew();
    bool shown = false;
    void Sample()
    {
        var compact = SubtitleProjection.Compact(store.Snapshot());
        var expanded = SubtitleProjection.Expanded(store.Snapshot());
        var text = SubtitleProjection.JoinTexts(expanded.Select(x => x.Translation));
        if (shown && (compact.Translation.Length == 0 || text.Length == 0)) blanks++;
        shown |= compact.Translation.Length > 0;
        frames.Add(new { ms = clock.ElapsedMilliseconds, source = compact.Source, translation = compact.Translation, expanded = text, pending = compact.TranslationPending });
    }
    store.Changed += Sample;
    var real = ProviderCatalog.CreateTranslator(settings);
    var delayed = new DelayedTranslator(real, () => calls++);
    await using var scheduler = new TranslationScheduler(store, delayed, settings.Translation with { DebounceMilliseconds = 0, TimeoutSeconds = 20 }, settings.TargetLanguage);
    // Replay genuine ASR events deterministically; the second actual MT call is delayed 1.5s for observation.
    scheduler.Schedule(store.ApplySource(generation, events[0])!);
    await scheduler.FlushAsync(limit.Token);
    if (!shown) throw new InvalidOperationException("First real MT response unavailable");
    foreach (var result in events.Skip(1))
    {
        var row = store.ApplySource(generation, result);
        if (row is not null) scheduler.Schedule(row);
        Sample();
        await Task.Delay(80, limit.Token);
    }
    var flush = scheduler.FlushAsync(limit.Token);
    while (!flush.IsCompleted) { Sample(); await Task.Delay(50, limit.Token); }
    await flush;
    var last = store.Snapshot().Last();
    if (blanks != 0 || !last.IsFinal || string.IsNullOrWhiteSpace(last.TranslationText))
        throw new InvalidOperationException("Continuity or final exact translation failed");
    outcome = "passed";
}
catch (Exception ex)
{
    // Provider exception messages can contain endpoint details; report only the class.
    error = ex.GetType().Name;
}
var report = new { outcome, error, provider, asr = "real sherpa-onnx bundled WAV", replay = "genuine ASR events replayed at 80ms; second real MT request delayed 1500ms", partials, finals, realMtCalls = calls, blankFrames = blanks, frames };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { outcome, error, provider, partials, finals, realMtCalls = calls, blankFrames = blanks }));
return outcome == "passed" ? 0 : 1;

sealed class DelayedTranslator(ITranslationProvider inner, Action called) : ITranslationProvider
{
    private int requests;
    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken token)
    {
        if (Interlocked.Increment(ref requests) == 2) await Task.Delay(1500, token);
        called();
        return await inner.TranslateAsync(request, token);
    }
}
