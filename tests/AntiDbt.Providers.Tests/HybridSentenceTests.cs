using AntiDbt.Core;
using AntiDbt.Providers;
using System.Collections.Concurrent;

internal static class HybridSentenceTests
{
    public static async Task RunAsync()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var events = new[] {
            new AsrResult("a", 1, "alpha be", false, TimeSpan.Zero, TimeSpan.FromSeconds(.8), "en"),
            new AsrResult("a", 2, "alpha", true, TimeSpan.Zero, TimeSpan.FromSeconds(.5), "en") with { BatchSize = 2 },
            new AsrResult("b", 1, "be", false, TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(.8), "en"),
            new AsrResult("b", 2, "beta gamma", true, TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(2), "en")
        };
        var sizes = new List<int>();
        var firstCloud = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextVisible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new HybridStreamingAsrProvider(new MockScriptedAsrProvider(events), cloudTranscriber: (pcm, _, _) =>
        {
            sizes.Add(pcm.Length);
            return sizes.Count == 1 ? firstCloud.Task : Task.FromResult<string?>("Beta corrected");
        });
        await using var session = provider.CreateSession(new() { Language = "en" });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await session.ConnectAsync(timeout.Token);
        await session.SendAudioAsync(new byte[64000], timeout.Token);
        var seen = new ConcurrentQueue<AsrResult>();
        var reader = Task.Run(async () => {
            await foreach (var row in session.GetResultsAsync(timeout.Token))
            {
                seen.Enqueue(row);
                if (row.SegmentId == "hybrid-2" && !row.IsFinal) nextVisible.TrySetResult();
            }
        });
        var completion = session.CompleteAsync(timeout.Token);
        try
        {
            await nextVisible.Task.WaitAsync(timeout.Token);
            Check(seen.Any(x => x.SegmentId == "hybrid-1" && x.IsFinal), "local Final waited for cloud and hid the sentence handoff");
        }
        finally { firstCloud.TrySetResult("Alpha corrected"); }
        await completion;
        await reader;
        Check(sizes.SequenceEqual(new[] { 16000, 48000 }), "cloud sentence stole queued future audio or lost the retained tail");
        var rows = seen.ToArray();
        int batch = Array.FindIndex(rows, x => x.BatchSize == 2);
        Check(batch >= 0 && rows[batch].IsFinal && rows[batch + 1].SegmentId == "hybrid-2" && !rows[batch + 1].IsFinal, "cloud correction interleaved with atomic Final/Partial batch");
        var store = new SubtitleStore();
        long generation = store.BeginSession();
        foreach (var row in rows) store.ApplySource(generation, row);
        Check(store.Snapshot().Count == 2 && store.Snapshot().All(x => x.IsFinal), "hybrid confirmed history missing");
        Check(store.Snapshot()[0].SourceText == "Alpha corrected" && store.Snapshot()[1].SourceText == "Beta corrected", "late cloud correction targeted the next segment");
    }
}
