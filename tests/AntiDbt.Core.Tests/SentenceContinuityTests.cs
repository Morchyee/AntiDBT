using AntiDbt.Core;

internal static class SentenceContinuityTests
{
    public static async Task RunAsync()
    {
        SentencePolicyTests.Run();
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var store = new SubtitleStore();
        var session = store.BeginSession();
        store.ApplySource(session, new("confirmed", 1, "Keep this confirmed sentence", true, TimeSpan.Zero, TimeSpan.FromSeconds(5), "en"));
        store.ApplyTranslation(session, "confirmed", 1, "已确认字幕");
        Check(store.ApplySource(session, new("confirmed", 2, "Next sentence reused wrong ID", false, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "en")) is null,
            "a next Partial overwrote a confirmed segment");
        Check(store.ApplySource(session, new("confirmed", 3, "", true, TimeSpan.Zero, TimeSpan.FromSeconds(5), "en", IsDeleted: true)) is null,
            "provider deletion erased confirmed history");
        Check(store.Snapshot().Single().SourceText == "Keep this confirmed sentence", "confirmed history missing");
        var provider = new CountingTranslator();
        await using var scheduler = new TranslationScheduler(store, provider, new() { DebounceMilliseconds = 0 }, "zh");
        scheduler.Schedule(store.Snapshot().Single());
        await scheduler.FlushAsync(CancellationToken.None);
        Check(provider.Calls == 0, "completed Revision translated twice");

        var startupAudio = new StartupAudio();
        var startupAsr = new StartupAsr();
        await using var startup = new CaptionPipeline(startupAudio, startupAsr, new CountingTranslator(), new() { Translation = new() { DebounceMilliseconds = 0 } });
        await startup.StartAsync();
        await startupAsr.Connecting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(!startupAudio.Started, "capture started before slow model warmup; valid startup audio can expire in the two-second queue");
        startupAsr.Ready.SetResult();
        await startup.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Check(startupAudio.Started && startup.DroppedAudioChunks == 0, "warmup handoff dropped queued speech");
    }

    private sealed class StartupAudio : IAudioSource
    {
        private readonly TestAudioSource inner = new();
        public bool Started;
        public string Name => "startup-fixture";
        public Task StartAsync(CancellationToken token) { Started = true; inner.Push(); inner.Finish(); return Task.CompletedTask; }
        public IAsyncEnumerable<AudioChunk> ReadChunksAsync(CancellationToken token) => inner.ReadChunksAsync(token);
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    private sealed class StartupAsr : IAsrProvider
    {
        public readonly TaskCompletionSource Connecting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IAsrSession CreateSession(AsrConfiguration config) => new Session(this);
        private sealed class Session(StartupAsr owner) : IAsrSession
        {
            private readonly TestAsrSession inner = new();
            public async Task ConnectAsync(CancellationToken token) { owner.Connecting.SetResult(); await owner.Ready.Task.WaitAsync(token); }
            public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) => inner.SendAudioAsync(pcm, token);
            public Task CompleteAsync(CancellationToken token) => inner.CompleteAsync(token);
            public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token) => inner.GetResultsAsync(token);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
