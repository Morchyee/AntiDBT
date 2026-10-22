using System.Diagnostics;
using System.Threading.Channels;
using AntiDbt.Core;

internal sealed record RequestEnvelope(TranslationRequest Request, TaskCompletionSource<string> Completion);

internal sealed class ScriptedTranslator : ITranslationProvider, IDisposable
{
    private readonly Channel<RequestEnvelope> _channel = Channel.CreateUnbounded<RequestEnvelope>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly List<RequestEnvelope> _buffered = new();
    private readonly List<RequestEnvelope> _all = new();
    private readonly object _lock = new();

    public Func<TranslationRequest, string>? AutoResponder { get; set; }

    public IReadOnlyList<RequestEnvelope> CapturedRequests
    {
        get
        {
            lock (_lock) return _all.ToList();
        }
    }

    public Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var env = new RequestEnvelope(request, tcs);
        lock (_lock)
        {
            _all.Add(env);
        }
        if (AutoResponder != null)
        {
            try
            {
                var res = AutoResponder(request);
                tcs.TrySetResult(res);
                return tcs.Task;
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                return tcs.Task;
            }
        }
        _channel.Writer.TryWrite(env);
        return tcs.Task;
    }

    public async Task<RequestEnvelope> NextAsync(Func<TranslationRequest, bool>? predicate = null, TimeSpan? timeout = null)
    {
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(waitTimeout);

        lock (_lock)
        {
            if (predicate != null)
            {
                for (int i = 0; i < _buffered.Count; i++)
                {
                    if (predicate(_buffered[i].Request))
                    {
                        var match = _buffered[i];
                        _buffered.RemoveAt(i);
                        return match;
                    }
                }
            }
            else if (_buffered.Count > 0)
            {
                var match = _buffered[0];
                _buffered.RemoveAt(0);
                return match;
            }
        }

        while (await _channel.Reader.WaitToReadAsync(cts.Token))
        {
            while (_channel.Reader.TryRead(out var env))
            {
                if (predicate == null || predicate(env.Request))
                {
                    return env;
                }
                lock (_lock)
                {
                    _buffered.Add(env);
                }
            }
        }

        throw new TimeoutException("Timed out waiting for matching translation request.");
    }

    public void Complete(RequestEnvelope env, string result) => env.Completion.TrySetResult(result);
    public void Fail(RequestEnvelope env, Exception ex) => env.Completion.TrySetException(ex);

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var env in _all)
            {
                env.Completion.TrySetCanceled();
            }
        }
        _channel.Writer.TryComplete();
    }
}

internal sealed class GroupProbe
{
    private readonly SubtitleStore _store;
    private readonly TranslationDisplayGroupSnapshot? _ticket;

    public GroupProbe(SubtitleStore store, CaptionSegment[] expected)
    {
        _store = store;
        _ticket = store.TryPrepareTranslationDisplayGroup(expected);
    }

    public TranslationDisplayGroupSnapshot? Ticket => _ticket;

    public bool Commit(string text)
    {
        if (_ticket == null) return false;
        return _store.AssignTranslationDisplayGroup(_ticket, text);
    }
}

internal static class Phase555TestSupport
{
    public static GroupProbe CaptureGroup(SubtitleStore store, params CaptionSegment[] expected)
        => new(store, expected);

    public static AsrResult MakeAsr(string id, int revision, string text, bool final, int startMs, int endMs, string lang = "en", bool isDeleted = false)
        => new(id, revision, text, final, TimeSpan.FromMilliseconds(startMs), TimeSpan.FromMilliseconds(endMs), lang, IsDeleted: isDeleted || string.IsNullOrEmpty(text));

    public static void AssertZ(SubtitleStore store, IReadOnlyList<CaptionSegment> before, bool commitResult, int changedCount)
    {
        if (commitResult)
            throw new Exception("Assertion Z failed: expected commitResult to be false, but was true.");
        if (changedCount != 0)
            throw new Exception($"Assertion Z failed: expected 0 Changed events, but got {changedCount}.");

        var after = store.Snapshot();
        if (after.Count != before.Count)
            throw new Exception($"Assertion Z failed: row count changed from {before.Count} to {after.Count}.");

        for (int i = 0; i < before.Count; i++)
        {
            var b = before[i];
            var a = after[i];

            // 1. Structural value equality on the record
            if (!a.Equals(b))
            {
                throw new Exception($"Assertion Z failed: segment {b.SegmentId} record equality failed.\nBefore: {b}\nAfter: {a}");
            }

            // 2. Explicit 23-field comparison including SessionId
            if (a.SessionId != b.SessionId ||
                a.SegmentId != b.SegmentId ||
                a.Revision != b.Revision ||
                a.SourceText != b.SourceText ||
                a.TranslationText != b.TranslationText ||
                a.IsFinal != b.IsFinal ||
                a.AudioStart != b.AudioStart ||
                a.AudioEnd != b.AudioEnd ||
                a.ReceivedTimestamp != b.ReceivedTimestamp ||
                a.TranslatedTimestamp != b.TranslatedTimestamp ||
                a.Language != b.Language ||
                a.TranslationError != b.TranslationError ||
                a.ProvisionalTranslationText != b.ProvisionalTranslationText ||
                a.TranslationRevision != b.TranslationRevision ||
                a.AsrLatencyMs != b.AsrLatencyMs ||
                a.QueueWaitMs != b.QueueWaitMs ||
                a.AudioCapturedTimestamp != b.AudioCapturedTimestamp ||
                a.LastVisibleTranslation != b.LastVisibleTranslation ||
                a.TranslationDisplayGroup != b.TranslationDisplayGroup ||
                a.DisplayTranslationOverride != b.DisplayTranslationOverride ||
                a.TranscriptStart != b.TranscriptStart ||
                a.SourceVersion != b.SourceVersion ||
                a.PendingDisplayGroupAttempt != b.PendingDisplayGroupAttempt)
            {
                throw new Exception($"Assertion Z failed: segment {b.SegmentId} state mutated.\nBefore: {b}\nAfter: {a}");
            }
        }
    }

    public static void AssertV(SubtitleStore store, IReadOnlyList<CaptionSegment> before, bool commitResult, int changedCount, string expectedGroupText, params string[] expectedMemberIds)
    {
        if (!commitResult)
            throw new Exception("Assertion V failed: expected commitResult to be true, but was false.");
        if (changedCount != 1)
            throw new Exception($"Assertion V failed: expected exactly 1 Changed event, but got {changedCount}.");

        var after = store.Snapshot();
        if (after.Count != before.Count)
            throw new Exception($"Assertion V failed: row count changed from {before.Count} to {after.Count}.");

        string? groupId = null;
        var expectedSet = expectedMemberIds.ToHashSet();

        for (int i = 0; i < after.Count; i++)
        {
            var a = after[i];
            var b = before[i];

            if (a.SegmentId != b.SegmentId || a.Revision != b.Revision || a.SourceText != b.SourceText ||
                a.IsFinal != b.IsFinal || a.AudioStart != b.AudioStart || a.AudioEnd != b.AudioEnd)
            {
                throw new Exception($"Assertion V failed: source identity changed for segment {a.SegmentId}.");
            }

            if (expectedSet.Contains(a.SegmentId))
            {
                if (string.IsNullOrEmpty(a.TranslationDisplayGroup))
                    throw new Exception($"Assertion V failed: member {a.SegmentId} missing TranslationDisplayGroup.");

                if (groupId == null)
                    groupId = a.TranslationDisplayGroup;
                else if (a.TranslationDisplayGroup != groupId)
                    throw new Exception($"Assertion V failed: mismatched group ID '{a.TranslationDisplayGroup}' vs '{groupId}'.");

                if (a.SegmentId == expectedMemberIds[0])
                {
                    if (a.LastVisibleTranslation != expectedGroupText)
                        throw new Exception($"Assertion V failed: first member LastVisibleTranslation was '{a.LastVisibleTranslation}', expected '{expectedGroupText}'.");
                }
                else
                {
                    if (a.LastVisibleTranslation != "")
                        throw new Exception($"Assertion V failed: subsequent member {a.SegmentId} LastVisibleTranslation was '{a.LastVisibleTranslation}', expected empty.");
                }

                // CRITICAL AQ-01 contract: combined translation must NOT become independent exact TranslationText!
                if (a.TranslationText == expectedGroupText)
                    throw new Exception($"Assertion V failed: combined text leaked into independent TranslationText of {a.SegmentId}.");
            }
        }

        var compact = SubtitleProjection.Compact(after);
        var expanded = SubtitleProjection.Expanded(after);

        int compactCount = CountSubstrings(compact.Translation, expectedGroupText);
        if (compactCount != 1)
            throw new Exception($"Assertion V failed: Compact view contained {compactCount} copies of '{expectedGroupText}', expected exactly 1.");

        int expandedCount = 0;
        foreach (var p in expanded)
        {
            expandedCount += CountSubstrings(p.Translation, expectedGroupText);
        }
        if (expandedCount != 1)
            throw new Exception($"Assertion V failed: Expanded view contained {expandedCount} copies of '{expectedGroupText}', expected exactly 1.");
    }

    public static int CountSubstrings(string? text, string sub)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(sub)) return 0;
        int count = 0, index = 0;
        while ((index = text.IndexOf(sub, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += sub.Length;
        }
        return count;
    }
}
