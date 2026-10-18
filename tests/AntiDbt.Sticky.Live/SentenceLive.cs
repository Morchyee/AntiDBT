using AntiDbt.Core;
using AntiDbt.Providers;
using AntiDbt.Windows;
using System.Diagnostics;
using System.Text.Json;

internal static class SentenceLive
{
    // --sentence-live <seconds> <existing-settings-directory> <output-directory>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[1], out int seconds) || seconds is < 10 or > 600) return 2;
        string directory = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(directory);
        CaptionDiagnostics.EnableFile(Path.Combine(directory, "caption-trace.jsonl"));
        var configured = await new SettingsStore(Path.GetFullPath(args[2])).LoadAsync();
        var asrConfig = new AsrConfiguration { Provider = "SherpaOnnx", Language = "en",
            Model = Path.GetFullPath("models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17") };
        var source = new LimitedAudioSource(seconds);
        var observedAsr = new ObservedAsr();
        var translator = new ObservedTranslator(ProviderCatalog.CreateTranslator(configured));
        var store = new SubtitleStore();
        var confirmed = new Dictionary<string, string>();
        var frames = new List<object>();
        var errors = new List<string>();
        bool appeared = false;
        int blankFrames = 0, overwritten = 0;
        var process = Process.GetCurrentProcess();
        long initialMemory = process.PrivateMemorySize64;
        long peakMemory = initialMemory;
        var clock = Stopwatch.StartNew();
        store.Changed += () =>
        {
            var rows = store.Snapshot();
            foreach (var pair in confirmed)
                if (!rows.Any(x => x.SegmentId == pair.Key && x.SourceText == pair.Value)) overwritten++;
            foreach (var row in rows.Where(x => x.IsFinal)) confirmed.TryAdd(row.SegmentId, row.SourceText);
            var compact = SubtitleProjection.Compact(rows);
            if (appeared && compact.Source.Length == 0) blankFrames++;
            appeared |= compact.Source.Length > 0;
            frames.Add(new { ms = clock.ElapsedMilliseconds, rows = rows.Count, finals = rows.Count(x => x.IsFinal), sourceLength = compact.Source.Length, translationLength = compact.Translation.Length });
        };
        await using var pipeline = new CaptionPipeline(source, observedAsr, translator,
            configured with { Asr = asrConfig }, store);
        pipeline.StatusChanged += status => { if (status.State == PipelineState.Error) errors.Add("pipeline-error"); };
        string outcome = "failed", error = "";
        try
        {
            await pipeline.StartAsync();
            for (int elapsed = 0; elapsed < seconds; elapsed++)
            {
                await Task.Delay(1000);
                process.Refresh();
                peakMemory = Math.Max(peakMemory, process.PrivateMemorySize64);
                if (pipeline.Completion.IsCompleted) break;
            }
            await pipeline.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var rows = store.Snapshot();
            if (observedAsr.Last is null || observedAsr.Last.AcceptedSamples != observedAsr.Last.ProcessedSamples ||
                rows.Count < 2 || rows.Any(x => !x.IsFinal || (x.AudioEnd - x.AudioStart).TotalSeconds > 9.11) ||
                blankFrames != 0 || overwritten != 0 || pipeline.DroppedAudioChunks != 0 || errors.Count > 0)
                throw new InvalidOperationException("Live continuity assertion failed");
            outcome = "passed";
        }
        catch (Exception ex) { error = ex.GetType().Name; }
        var finalRows = store.Snapshot();
        process.Refresh();
        var report = new { outcome, error, requestedSeconds = seconds, elapsedSeconds = clock.Elapsed.TotalSeconds,
            audio = "current user video via real WASAPI loopback; sherpa English bundled model", asr = "real-time, no replay",
            partials = observedAsr.Partials, finalEvents = observedAsr.Finals, segments = finalRows.Count,
            maxSegmentSeconds = finalRows.Select(x => (x.AudioEnd - x.AudioStart).TotalSeconds).DefaultIfEmpty().Max(),
            acceptedSamples = observedAsr.Last?.AcceptedSamples, processedSamples = observedAsr.Last?.ProcessedSamples,
            pipelineDroppedChunks = pipeline.DroppedAudioChunks, blankSourceFrames = blankFrames, overwrittenFinals = overwritten,
            realMtCalls = translator.Calls, successfulMtCalls = translator.Successes, translationErrors = finalRows.Count(x => x.TranslationError is not null),
            exactTranslatedSegments = finalRows.Count(x => x.TranslationText.Length > 0), initialPrivateBytes = initialMemory,
            peakPrivateBytes = peakMemory, finalPrivateBytes = process.PrivateMemorySize64, frames };
        await File.WriteAllTextAsync(Path.Combine(directory, "live-video.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(directory, "captions.json"), JsonSerializer.Serialize(finalRows, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { outcome, error, segments = finalRows.Count, partials = observedAsr.Partials, finals = observedAsr.Finals, blankFrames, overwritten, mtCalls = translator.Calls, mtSuccesses = translator.Successes }));
        return outcome == "passed" ? 0 : 1;
    }

    private sealed class ObservedAsr : IAsrProvider
    {
        public SherpaOnnxStreamingAsrProvider.Session? Last;
        public int Partials, Finals;
        public IAsrSession CreateSession(AsrConfiguration config)
        {
            Last = (SherpaOnnxStreamingAsrProvider.Session)new SherpaOnnxStreamingAsrProvider().CreateSession(config);
            return new Session(Last, this);
        }
        private sealed class Session(IAsrSession inner, ObservedAsr owner) : IAsrSession
        {
            public Task ConnectAsync(CancellationToken token) => inner.ConnectAsync(token);
            public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) => inner.SendAudioAsync(pcm, token);
            public Task CompleteAsync(CancellationToken token) => inner.CompleteAsync(token);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
            public async IAsyncEnumerable<AsrResult> GetResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
            {
                await foreach (var result in inner.GetResultsAsync(token))
                {
                    if (result.IsFinal) owner.Finals++; else owner.Partials++;
                    yield return result;
                }
            }
        }
    }
    private sealed class LimitedAudioSource(int seconds) : IAudioSource
    {
        private readonly WasapiAudioSource inner = new();
        private readonly CancellationTokenSource deadline = new();
        private CancellationTokenSource? linked;
        public string Name => inner.Name;
        public async Task StartAsync(CancellationToken token)
        {
            // Reserve half a second for the native StopRecording callback; independent of ASR/MT waits.
            deadline.CancelAfter(TimeSpan.FromSeconds(seconds - 0.5));
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            await inner.StartAsync(linked.Token);
        }
        public async IAsyncEnumerable<AudioChunk> ReadChunksAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            long remaining = seconds * 32000L;
            await foreach (var chunk in inner.ReadChunksAsync(token))
            {
                int length = (int)Math.Min(chunk.Pcm.Length, remaining);
                if (length == 0) break;
                remaining -= length;
                yield return chunk with { Pcm = chunk.Pcm[..length], AudioDuration = TimeSpan.FromSeconds(length / 32000d) };
                if (remaining == 0) { deadline.Cancel(); break; }
            }
        }
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public async ValueTask DisposeAsync() { await inner.DisposeAsync(); linked?.Dispose(); deadline.Dispose(); }
    }
    private sealed class ObservedTranslator(ITranslationProvider inner) : ITranslationProvider
    {
        public int Calls, Successes;
        public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            var result = await inner.TranslateAsync(request, token);
            Interlocked.Increment(ref Successes);
            return result;
        }
    }
}
