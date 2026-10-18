using AntiDbt.Core;

internal static class StickyTranslationTests
{
    public static async Task RunAsync()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static AsrResult Source(int revision, string text, bool final = false, string id = "sticky", string language = "en")
            => new(id, revision, text, final, TimeSpan.Zero, TimeSpan.FromSeconds(1), language);
        var store = new SubtitleStore();
        var session = store.BeginSession();
        store.ApplySource(session, Source(1, "Turn left"));
        store.ApplyTranslation(session, "sticky", 1, "向左转");
        var paragraph = SubtitleProjection.Expanded(store.Snapshot()).Single();
        var notifications = new List<string>();
        paragraph.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SubtitleParagraph.Translation)) notifications.Add(paragraph.Translation); };
        void Visible(string expected)
        {
            Check(SubtitleProjection.Compact(store.Snapshot()).Translation == expected, "compact blank or incorrect fallback");
            var projected = SubtitleProjection.Expanded(store.Snapshot()).SingleOrDefault() ?? new SubtitleParagraph("", "");
            Check(projected.Translation == expected, "expanded blank or incorrect fallback");
            paragraph.UpdateFrom(projected);
        }
        void Retained() => Visible("向左转");
        store.Changed += Retained;
        for (int i = 2; i <= 100; i++)
        {
            store.ApplySource(session, Source(i, $"Turn right {i}"));
            Check(store.Snapshot().Single().TranslationText == "", "fallback marked exact");
        }
        store.ApplyTranslationError(session, "sticky", 100, "failed");
        store.ClearTranslationError(session, "sticky", 100);
        Check(!store.ApplyTranslation(session, "sticky", 99, "stale"), "stale accepted");
        Check(!store.ApplyTranslation(session, "sticky", 100, "  "), "blank translation accepted");
        // Unsubscribe before the successful atomic handoff.
        store.Changed -= Retained;
        Visible("向左转");
        store.ApplyTranslation(session, "sticky", 100, "向右转");
        Visible("向右转");
        Check(notifications.SequenceEqual(new[] { "向右转" }), "UI emitted empty or intermediate translation");
        store.ApplyTranslationError(session, "sticky", 100, "retry failed");
        Visible("向右转");
        store.ApplySource(session, Source(101, "日本語", language: "ja"));
        Visible("");
        store.ApplyTranslation(session, "sticky", 101, "日语");
        store.ApplySource(session, Source(102, "", true));
        Visible("");
        store.Clear();
        Check(store.Snapshot().Count == 0, "clear retained fallback");
        session = store.BeginSession();
        store.ApplySource(session, Source(1, "new"));
        Visible("");
        store.ApplyTranslation(session, "sticky", 1, "new translation");
        store.ApplySource(session, Source(1, "next", id: "next"));
        Check(store.Snapshot().Single(x => x.SegmentId == "next").TranslationText == "", "next sentence inherited exact");
        Check(SubtitleProjection.Expanded(new[] { store.Snapshot().Last() }).Single().Translation == "", "next sentence inherited display");
        store.BeginSession(false);
        store.ApplySource(store.CurrentSession, Source(1, "same ID new session"));
        Check(SubtitleProjection.Expanded(new[] { store.Snapshot().Last() }).Single().Translation == "", "new session inherited display");

        var slowStore = new SubtitleStore();
        var generation = slowStore.BeginSession();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new TranslationScheduler(slowStore, new SlowTranslator(started, finish), new() { DebounceMilliseconds = 0, TimeoutSeconds = 1 }, "zh");
        slowStore.ApplySource(generation, Source(1, "A"));
        slowStore.ApplyTranslation(generation, "sticky", 1, "OLD");
        scheduler.Schedule(slowStore.ApplySource(generation, Source(2, "B"))!);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (int i = 0; i < 30; i++)
        {
            Check(SubtitleProjection.Compact(slowStore.Snapshot()).Translation == "OLD", "slow MT blank compact frame");
            Check(SubtitleProjection.Expanded(slowStore.Snapshot()).Single().Translation == "OLD", "slow MT blank expanded frame");
            await Task.Delay(50);
        }
        await scheduler.FlushAsync(CancellationToken.None);
        Check(slowStore.Snapshot().Single().TranslationError != null, "timeout missing error");
        finish.TrySetResult("late timeout result");
        Check(SubtitleProjection.Compact(slowStore.Snapshot()).Translation == "OLD", "timeout cleared fallback");
        scheduler.Schedule(slowStore.Snapshot().Single());
        await scheduler.FlushAsync(CancellationToken.None);
        Check(SubtitleProjection.Compact(slowStore.Snapshot()).Translation == "NEW", "retry did not replace fallback");

        // A genuinely slow successful response, followed by cancellation and final preemption.
        var delayed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var slowScheduler = new TranslationScheduler(slowStore, new SlowTranslator(slowStarted, delayed), new() { DebounceMilliseconds = 0, TimeoutSeconds = 5 }, "zh");
        slowScheduler.Schedule(slowStore.ApplySource(generation, Source(3, "C"))!);
        await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (int i = 0; i < 30; i++)
        {
            Check(SubtitleProjection.Expanded(slowStore.Snapshot()).Single().Translation == "NEW", "1.5s slow success lost fallback");
            await Task.Delay(50);
        }
        delayed.SetResult("SLOW SUCCESS");
        await slowScheduler.FlushAsync(CancellationToken.None);
        Check(SubtitleProjection.Compact(slowStore.Snapshot()).Translation == "SLOW SUCCESS", "slow success not handed off");
        var cancelStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cancelScheduler = new TranslationScheduler(slowStore, new SlowTranslator(cancelStarted, cancelledResult), new() { DebounceMilliseconds = 0 }, "zh");
        cancelScheduler.Schedule(slowStore.ApplySource(generation, Source(4, "D"))!);
        await cancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancelScheduler.CancelPending();
        await cancelScheduler.FlushAsync(CancellationToken.None);
        cancelledResult.SetResult("CANCELLED LATE");
        Check(SubtitleProjection.Compact(slowStore.Snapshot()).Translation == "SLOW SUCCESS", "cancel cleared translation");
        cancelScheduler.Schedule(slowStore.ApplySource(generation, Source(5, "D final", true))!);
        await cancelScheduler.FlushAsync(CancellationToken.None);
        Check(SubtitleProjection.Compact(slowStore.Snapshot()).Translation == "NEW", "Final did not replace cancelled request fallback");

        // Explicit provider split/merge batches share a display fallback until every replacement is exact.
        var splitStore = new SubtitleStore();
        var splitSession = splitStore.BeginSession();
        var original = Source(1, "one two", id: "original");
        splitStore.ApplySource(splitSession, original);
        splitStore.ApplyTranslation(splitSession, "original", 1, "一二");
        var frames = new List<string>();
        splitStore.Changed += () => frames.Add(SubtitleProjection.Compact(splitStore.Snapshot()).Translation);
        var one = Source(1, "one", id: "one") with { AudioEnd = TimeSpan.FromMilliseconds(500) };
        var two = Source(1, "two", id: "two") with { AudioStart = TimeSpan.FromMilliseconds(500) };
        splitStore.ApplySources(splitSession, new[] { original with { Revision = 2, IsDeleted = true, Text = "" }, one, two });
        Check(frames.SequenceEqual(new[] { "一二" }), "split emitted empty intermediate frame");
        splitStore.ApplyTranslation(splitSession, "one", 1, "一");
        Check(SubtitleProjection.Expanded(splitStore.Snapshot()).Single().Translation == "一二", "split duplicated partial and group fallback");
        splitStore.ApplyTranslation(splitSession, "two", 1, "二");
        Check(SubtitleProjection.Compact(splitStore.Snapshot()).Translation == "一二", "split handoff incorrect");
        splitStore.ApplySources(splitSession, new[] { one with { Revision = 2, IsDeleted = true }, two with { Revision = 2, IsDeleted = true }, Source(1, "merged", id: "merged") });
        Check(SubtitleProjection.Expanded(splitStore.Snapshot()).Single().Translation == "一二", "merge lost display");
        Check(!splitStore.ApplyTranslation(splitSession, "one", 1, "late"), "retired split result accepted");
        splitStore.ApplyTranslationError(splitSession, "merged", 1, "merge MT failed");
        Check(SubtitleProjection.Compact(splitStore.Snapshot()).Translation == "一二", "merge failure lost display");
        splitStore.ApplyTranslation(splitSession, "merged", 1, "合并");
        Check(SubtitleProjection.Compact(splitStore.Snapshot()).Translation == "合并", "merge success not replaced");
        splitStore.ApplySource(splitSession, Source(2, "", id: "merged") with { IsDeleted = true });
        Check(splitStore.Snapshot().Count == 0, "delete retained group fallback");
        Check(frames.Take(frames.Count - 1).All(x => x.Length > 0), "split/merge produced blank frame");

        var ordering = new SubtitleStore();
        var orderingSession = ordering.BeginSession();
        ordering.ApplySource(orderingSession, Source(2, "Turn right"));
        ordering.ApplyTranslation(orderingSession, "sticky", 2, "NEWER VISIBLE");
        ordering.ApplySource(orderingSession, Source(3, "Turn left again"));
        Check(!ordering.ApplyProvisionalTranslation(orderingSession, "sticky", 1, "Turn left", "OLDER VISIBLE"), "older compatible draft overwrote newer sticky display");
        Check(SubtitleProjection.Compact(ordering.Snapshot()).Translation == "NEWER VISIBLE", "visible revision ordering regressed");

        var pipelineStore = new SubtitleStore();
        var pipelineFrames = new List<string>();
        bool pipelineVisible = false;
        pipelineStore.Changed += () =>
        {
            string text = SubtitleProjection.Compact(pipelineStore.Snapshot()).Translation;
            if (pipelineVisible) pipelineFrames.Add(text);
            pipelineVisible |= text.Length > 0;
        };
        var audio = new TestAudioSource();
        audio.Push(); audio.Finish();
        await using var pipeline = new CaptionPipeline(audio, new BatchAsr(pipelineStore, new[] {
            original,
            original with { Revision = 2, IsDeleted = true, Text = "", BatchSize = 3 },
            one,
            two with { IsFinal = true }
        }), new CountingTranslator(), new() { Translation = new() { DebounceMilliseconds = 0 } }, pipelineStore);
        await pipeline.StartAsync();
        await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Check(pipelineFrames.Count > 0 && pipelineFrames.All(x => x.Length > 0), "pipeline rendered incomplete provider batch");
        Check(pipelineStore.Snapshot().Count == 2 && pipelineStore.Snapshot().All(x => x.TranslationText.Length > 0), "pipeline failed to schedule all batch replacements");
    }

    private sealed class SlowTranslator(TaskCompletionSource started, TaskCompletionSource<string> finish) : ITranslationProvider
    {
        private int calls;
        public string Id => "sticky-controlled";
        public Task<string> TranslateAsync(TranslationRequest request, CancellationToken token)
        {
            if (Interlocked.Increment(ref calls) > 1) return Task.FromResult("NEW");
            started.TrySetResult();
            return finish.Task;
        }
    }

    private sealed class BatchAsr(SubtitleStore store, AsrResult[] events) : IAsrProvider
    {
        public IAsrSession CreateSession(AsrConfiguration configuration) => new Session(store, events);
        private sealed class Session(SubtitleStore store, AsrResult[] events) : IAsrSession
        {
            public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
            public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) => Task.CompletedTask;
            public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public async IAsyncEnumerable<AsrResult> GetResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
            {
                yield return events[0];
                while (!store.Snapshot().Any(x => x.TranslationText.Length > 0)) await Task.Delay(5, token);
                foreach (var change in events.Skip(1))
                {
                    yield return change;
                    await Task.Delay(30, token);
                }
            }
        }
    }
}
