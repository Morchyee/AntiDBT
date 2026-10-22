using System.Diagnostics;
namespace AntiDbt.Core;

public sealed record CaptionSegment(long SessionId, string SegmentId, int Revision, string SourceText,
    string TranslationText, bool IsFinal, TimeSpan AudioStart, TimeSpan AudioEnd, long ReceivedTimestamp,
    long? TranslatedTimestamp, string Language, string? TranslationError = null,
    string ProvisionalTranslationText = "", int TranslationRevision = 0,
    double? AsrLatencyMs = null, double? QueueWaitMs = null,
    long? AudioCapturedTimestamp = null, string LastVisibleTranslation = "",
    string? TranslationDisplayGroup = null, string? DisplayTranslationOverride = null)
{
    public TimeSpan TranscriptStart { get; init; } = AudioStart;
    public long SourceVersion { get; internal init; }
    public long PendingDisplayGroupAttempt { get; internal init; }

    // Display fallback is never evidence of a completed translation for Revision.
    public string DisplayTranslation => DisplayTranslationOverride ?? (string.IsNullOrWhiteSpace(SourceText) ? ""
        : !string.IsNullOrWhiteSpace(TranslationText) ? TranslationText
        : !string.IsNullOrWhiteSpace(ProvisionalTranslationText) ? ProvisionalTranslationText
        : LastVisibleTranslation);
}

public readonly record struct TranslationDisplayGroupMember(
    long SessionId, string SegmentId, int Revision, string SourceText,
    string Language, bool IsFinal, TimeSpan AudioStart, TimeSpan AudioEnd,
    TimeSpan TranscriptStart, long SourceVersion, string? DisplayGroup);

public sealed class TranslationDisplayGroupSnapshot
{
    internal SubtitleStore Owner { get; }
    public long SessionId { get; }
    public long AttemptId { get; }
    public IReadOnlyList<TranslationDisplayGroupMember> Members { get; }

    internal TranslationDisplayGroupSnapshot(
        SubtitleStore owner,
        long sessionId,
        long attemptId,
        TranslationDisplayGroupMember[] members)
    {
        Owner = owner;
        SessionId = sessionId;
        AttemptId = attemptId;
        Members = Array.AsReadOnly(members);
    }
}

public sealed class SubtitleStore
{
    private readonly object _sync = new();
    private readonly List<CaptionSegment> _rows = [];
    private readonly Dictionary<string, int> _deleted = new();
    private int _maxHistory;
    private long _session;
    private long _nextSourceVersion;
    private long _nextDisplayGroupAttempt;
    public event Action? Changed;
    public SubtitleStore(int maxHistory = 2000) => _maxHistory = Math.Clamp(maxHistory, 1, 10000);
    public int MaxHistory
    {
        get { lock (_sync) return _maxHistory; }
        set
        {
            lock (_sync)
            {
                _maxHistory = Math.Clamp(value, 1, 10000);
                while (_rows.Count > _maxHistory)
                    _rows.RemoveAt(0);
            }
            Changed?.Invoke();
        }
    }
    public long CurrentSession
    {
        get
        {
            lock (_sync)
                return _session;
        }
    }
    public long BeginSession(bool clear = true)
    {
        long session;
        lock (_sync)
        {
            session = ++_session;
            _deleted.Clear();
            if (clear)
                _rows.Clear();
            CaptionDiagnostics.Record("store", clear ? "session-clear" : "session-retain", session, rows: _rows.Count);
        }
        Changed?.Invoke();
        return session;
    }
    public void Clear()
    {
        lock (_sync)
        {
            _rows.Clear();
            _deleted.Clear();
            CaptionDiagnostics.Record("store", "explicit-clear", _session);
        }
        Changed?.Invoke();
    }
    public CaptionSegment? ApplySource(long session, AsrResult result)
        => ApplySource(session, result, true);

    // Only a complete provider batch with matching audio coverage can transfer a display fallback.
    public IReadOnlyList<CaptionSegment> ApplySources(long session, IReadOnlyList<AsrResult> results)
    {
        CaptionSegment[] updated;
        lock (_sync)
        {
            if (session != _session) return [];
            var removedIds = results.Where(x => x.IsDeleted).Select(x => x.SegmentId).ToHashSet();
            var previous = Snapshot().Where(x => x.SessionId == session && removedIds.Contains(x.SegmentId)).ToArray();
            var incoming = results.Where(x => !x.IsDeleted).ToArray();
            var retained = incoming.Length == 2 ? _rows.FirstOrDefault(x => x.SessionId == session && x.SegmentId == incoming[0].SegmentId && !x.IsFinal) : null;
            bool sentenceSplit = retained is not null && incoming[0].IsFinal && !incoming[1].IsFinal &&
                incoming[0].SegmentId != incoming[1].SegmentId && incoming[0].AudioStart == retained.AudioStart &&
                incoming[0].AudioEnd == incoming[1].AudioStart && incoming[1].AudioEnd >= retained.AudioEnd;
            if (sentenceSplit) previous = Snapshot().Where(x => x.SessionId == session && x.SegmentId == retained!.SegmentId).ToArray();
            bool related = previous.Length > 0 && incoming.Length > 0 &&
                previous.All(x => !x.IsFinal && (sentenceSplit || results.Any(r => r.IsDeleted && r.SegmentId == x.SegmentId && r.Revision >= x.Revision))) &&
                incoming.All(x => sentenceSplit && x.SegmentId == retained!.SegmentId || !_rows.Any(r => r.SessionId == session && r.SegmentId == x.SegmentId)) &&
                previous.Min(x => x.AudioStart) == incoming.Min(x => x.AudioStart) &&
                (sentenceSplit || previous.Max(x => x.AudioEnd) == incoming.Max(x => x.AudioEnd)) &&
                previous.All(x => x.Language == (incoming[0].Language ?? "auto")) &&
                incoming.All(x => x.Language == incoming[0].Language && !string.IsNullOrWhiteSpace(x.Text)) &&
                previous.All(x => x.TranslationDisplayGroup is null || _rows.Where(r => r.TranslationDisplayGroup == x.TranslationDisplayGroup).All(r => removedIds.Contains(r.SegmentId)));
            string fallback = related ? SubtitleProjection.JoinTexts(previous.Select(x => x.DisplayTranslation)) : "";
            foreach (var result in results) ApplySource(session, result, false);
            var ids = incoming.Select(x => x.SegmentId).ToHashSet();
            if (fallback.Length > 0)
            {
                string group = Guid.NewGuid().ToString("N");
                bool first = true;
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].SessionId != session || !ids.Contains(_rows[i].SegmentId)) continue;
                    _rows[i] = _rows[i] with { TranslationDisplayGroup = group, LastVisibleTranslation = first ? fallback : "", PendingDisplayGroupAttempt = 0 };
                    first = false;
                }
            }
            updated = _rows.Where(x => x.SessionId == session && ids.Contains(x.SegmentId)).ToArray();
        }
        Changed?.Invoke();
        return updated;
    }

    private CaptionSegment? ApplySource(long session, AsrResult result, bool notify)
    {
        CaptionSegment? row;
        lock (_sync)
        {
            if (session != _session)
                return null;
            if (_deleted.TryGetValue(result.SegmentId, out var deletedRevision) && result.Revision <= deletedRevision)
                return null;
            var index = _rows.FindIndex(x => x.SessionId == session && x.SegmentId == result.SegmentId);
            var old = index < 0 ? null : _rows[index];
            if (result.IsDeleted)
            {
                if (old?.IsFinal == true)
                {
                    CaptionDiagnostics.Record("store", "reject-final-delete", session, result.SegmentId, result.Revision, rows: _rows.Count);
                    return null;
                }
                if (old is not null && result.Revision < old.Revision)
                    return null;
                ClearDisplayGroup(old?.TranslationDisplayGroup);
                if (index >= 0)
                    _rows.RemoveAt(index);
                row = null;
                _deleted[result.SegmentId] = result.Revision;
                if (_deleted.Count > 10000)
                    _deleted.Remove(_deleted.Keys.First());
            }
            else
            {
                if (old?.IsFinal == true && !result.IsFinal)
                {
                    CaptionDiagnostics.Record("store", "reject-final-to-partial", session, result.SegmentId, result.Revision, rows: _rows.Count);
                    return null;
                }
                if (old is not null && (result.Revision < old.Revision ||
                    result.Revision == old.Revision && (result.Text != old.SourceText || result.IsFinal == old.IsFinal || old.IsFinal)))
                    return null;

                bool sameLanguage = old is not null && old.Language == (result.Language ?? "auto");
                if (!sameLanguage || string.IsNullOrWhiteSpace(result.Text))
                    ClearDisplayGroup(old?.TranslationDisplayGroup);
                string exactTranslation = sameLanguage && old?.SourceText == result.Text ? old.TranslationText : "";
                string lastVisible = sameLanguage && !string.IsNullOrWhiteSpace(result.Text)
                    ? old!.TranslationDisplayGroup is null ? old.DisplayTranslation : old.LastVisibleTranslation : "";

                // Compatible Draft Rule (F2):
                // Empty or whitespace source NEVER inherits provisional translation.
                // Refinements and prefix extensions retain provisional translation.
                string provisionalTranslation = "";
                int provisionalRevision = sameLanguage && !string.IsNullOrWhiteSpace(result.Text)
                    ? exactTranslation.Length > 0 ? result.Revision : old!.TranslationRevision : 0;

                if (!string.IsNullOrWhiteSpace(result.Text) && sameLanguage && old is not null)
                {
                    bool draftNotExpired = old.TranslatedTimestamp is not null
                        ? Stopwatch.GetElapsedTime(old.TranslatedTimestamp.Value) < TimeSpan.FromSeconds(5)
                        : (result.Revision - old.TranslationRevision) <= 3;

                    if (draftNotExpired && IsCompatibleDraft(old.SourceText, result.Text))
                    {
                        provisionalTranslation = old.TranslationText.Length > 0 ? old.TranslationText : old.ProvisionalTranslationText;
                        if (exactTranslation.Length == 0) provisionalRevision = old.TranslationRevision;
                    }
                }

                long? translatedTimestamp = exactTranslation.Length > 0
                    ? old?.TranslatedTimestamp
                    : (provisionalTranslation.Length > 0 ? (old?.TranslatedTimestamp ?? old?.ReceivedTimestamp) : null);
                long? audioCaptured = old?.SourceText == result.Text ? old.AudioCapturedTimestamp : null;

                row = new CaptionSegment(session, result.SegmentId, result.Revision, result.Text,
                    exactTranslation, result.IsFinal,
                    result.AudioStart, result.AudioEnd, Stopwatch.GetTimestamp(),
                    translatedTimestamp, result.Language ?? "auto", null,
                    provisionalTranslation, provisionalRevision,
                    null, null, audioCaptured, lastVisible,
                    sameLanguage && !string.IsNullOrWhiteSpace(result.Text) ? old?.TranslationDisplayGroup : null)
                {
                    TranscriptStart = old?.TranscriptStart ?? result.AudioStart,
                    SourceVersion = ++_nextSourceVersion,
                    PendingDisplayGroupAttempt = 0
                };

                if (index >= 0)
                    _rows[index] = row;
                else
                    _rows.Add(row);

                _rows.Sort(TranscriptOrder.Compare);
                while (_rows.Count > _maxHistory)
                    _rows.RemoveAt(0);
            }
            CaptionDiagnostics.Record("store", result.IsDeleted ? "segment-delete" : "source-update", session, result.SegmentId,
                result.Revision, result.Text.Length, row?.DisplayTranslation.Length ?? 0, _rows.Count);
        }
        if (notify) Changed?.Invoke();
        return row;
    }

    private void ClearDisplayGroup(string? group)
    {
        if (group is null) return;
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i].TranslationDisplayGroup == group)
                _rows[i] = _rows[i] with { TranslationDisplayGroup = null, LastVisibleTranslation = "", PendingDisplayGroupAttempt = 0 };
    }

    public TranslationDisplayGroupSnapshot? TryPrepareTranslationDisplayGroup(IReadOnlyList<CaptionSegment> expectedMembers)
    {
        if (expectedMembers == null || expectedMembers.Count < 2) return null;
        lock (_sync)
        {
            long session = expectedMembers[0].SessionId;
            if (session != _session) return null;

            string firstLang = expectedMembers[0].Language;
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in expectedMembers)
            {
                if (m.SessionId != session) return null;
                if (string.IsNullOrWhiteSpace(m.SourceText)) return null;
                if (m.Language != firstLang) return null;
                if (!seenIds.Add(m.SegmentId)) return null;
            }

            int[] indices = new int[expectedMembers.Count];
            for (int i = 0; i < expectedMembers.Count; i++)
            {
                var exp = expectedMembers[i];
                int idx = _rows.FindIndex(r => r.SessionId == session && r.SegmentId == exp.SegmentId);
                if (idx < 0) return null;

                var live = _rows[idx];
                if (live.Revision != exp.Revision ||
                    live.SourceText != exp.SourceText ||
                    live.Language != exp.Language ||
                    live.IsFinal != exp.IsFinal ||
                    live.AudioStart != exp.AudioStart ||
                    live.AudioEnd != exp.AudioEnd ||
                    live.TranscriptStart != exp.TranscriptStart ||
                    live.SourceVersion != exp.SourceVersion)
                {
                    return null;
                }

                indices[i] = idx;
            }

            for (int i = 1; i < indices.Length; i++)
            {
                if (indices[i] != indices[i - 1] + 1)
                    return null;
            }

            for (int i = 0; i < indices.Length; i++)
            {
                var live = _rows[indices[i]];
                if (live.TranslationDisplayGroup != null)
                {
                    for (int j = 0; j < _rows.Count; j++)
                    {
                        if (_rows[j].TranslationDisplayGroup == live.TranslationDisplayGroup &&
                            !seenIds.Contains(_rows[j].SegmentId))
                        {
                            return null;
                        }
                    }
                }
            }

            bool allExact = true;
            for (int i = 0; i < indices.Length; i++)
            {
                var live = _rows[indices[i]];
                bool isExact = !string.IsNullOrWhiteSpace(live.TranslationText) && live.TranslationRevision == live.Revision;
                if (!isExact)
                {
                    allExact = false;
                    break;
                }
            }
            if (allExact) return null;

            long attemptId = ++_nextDisplayGroupAttempt;
            var members = new TranslationDisplayGroupMember[expectedMembers.Count];
            for (int i = 0; i < indices.Length; i++)
            {
                var live = _rows[indices[i]];
                members[i] = new TranslationDisplayGroupMember(
                    live.SessionId, live.SegmentId, live.Revision, live.SourceText,
                    live.Language, live.IsFinal, live.AudioStart, live.AudioEnd,
                    live.TranscriptStart, live.SourceVersion, live.TranslationDisplayGroup);

                _rows[indices[i]] = live with { PendingDisplayGroupAttempt = attemptId };
            }

            return new TranslationDisplayGroupSnapshot(this, session, attemptId, members);
        }
    }

    public bool AssignTranslationDisplayGroup(TranslationDisplayGroupSnapshot expected, string translation)
    {
        if (expected == null || string.IsNullOrWhiteSpace(translation)) return false;
        if (expected.Members == null || expected.Members.Count < 2) return false;

        lock (_sync)
        {
            if (expected.Owner != this || expected.SessionId != _session) return false;

            long session = expected.SessionId;
            int count = expected.Members.Count;
            int[] indices = new int[count];
            var memberIdSet = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < count; i++)
            {
                var exp = expected.Members[i];
                memberIdSet.Add(exp.SegmentId);
                int idx = _rows.FindIndex(r => r.SessionId == session && r.SegmentId == exp.SegmentId);
                if (idx < 0) return false;

                var live = _rows[idx];
                if (live.Revision != exp.Revision ||
                    live.SourceText != exp.SourceText ||
                    live.Language != exp.Language ||
                    live.IsFinal != exp.IsFinal ||
                    live.AudioStart != exp.AudioStart ||
                    live.AudioEnd != exp.AudioEnd ||
                    live.TranscriptStart != exp.TranscriptStart ||
                    live.SourceVersion != exp.SourceVersion)
                {
                    return false;
                }

                if (live.PendingDisplayGroupAttempt != expected.AttemptId)
                {
                    return false;
                }

                indices[i] = idx;
            }

            for (int i = 1; i < indices.Length; i++)
            {
                if (indices[i] != indices[i - 1] + 1)
                    return false;
            }

            for (int i = 0; i < count; i++)
            {
                var live = _rows[indices[i]];
                var exp = expected.Members[i];
                if (live.TranslationDisplayGroup != exp.DisplayGroup)
                    return false;

                if (live.TranslationDisplayGroup != null)
                {
                    for (int j = 0; j < _rows.Count; j++)
                    {
                        if (_rows[j].TranslationDisplayGroup == live.TranslationDisplayGroup &&
                            !memberIdSet.Contains(_rows[j].SegmentId))
                        {
                            return false;
                        }
                    }
                }
            }

            bool allExact = true;
            for (int i = 0; i < count; i++)
            {
                var live = _rows[indices[i]];
                bool isExact = !string.IsNullOrWhiteSpace(live.TranslationText) && live.TranslationRevision == live.Revision;
                if (!isExact)
                {
                    allExact = false;
                    break;
                }
            }
            if (allExact) return false;

            string groupId = Guid.NewGuid().ToString("N");
            for (int i = 0; i < count; i++)
            {
                var live = _rows[indices[i]];
                _rows[indices[i]] = live with
                {
                    TranslationDisplayGroup = groupId,
                    LastVisibleTranslation = (i == 0 ? translation : ""),
                    PendingDisplayGroupAttempt = 0
                };
            }
        }

        Changed?.Invoke();
        return true;
    }

    public bool ApplyTranslation(long session, string segmentId, int revision, string text)
        => UpdateTranslation(session, segmentId, revision, text, null);
    public bool ApplyTranslationError(long session, string segmentId, int revision, string message)
        => UpdateTranslation(session, segmentId, revision, "", message);
    public bool ClearTranslationError(long session, string segmentId, int revision)
    {
        lock (_sync)
        {
            if (session != _session)
                return false;
            var i = _rows.FindIndex(x => x.SessionId == session && x.SegmentId == segmentId && x.Revision == revision);
            if (i < 0)
            {
                CaptionDiagnostics.Record("translation", "reject-stale", session, segmentId, revision);
                return false;
            }
            _rows[i] = _rows[i] with { TranslationError = null };
        }
        Changed?.Invoke();
        return true;
    }
    private bool UpdateTranslation(long session, string segmentId, int revision, string text, string? error)
    {
        if (error is null && string.IsNullOrWhiteSpace(text))
            return false;
        lock (_sync)
        {
            if (session != _session)
                return false;
            var i = _rows.FindIndex(x => x.SessionId == session && x.SegmentId == segmentId && x.Revision == revision);
            if (i < 0)
            {
                CaptionDiagnostics.Record("translation", "reject-stale", session, segmentId, revision);
                return false;
            }
            var old = _rows[i];
            _rows[i] = error is not null ? old with { TranslationError = error } : old with
            {
                TranslationText = text,
                TranslatedTimestamp = Stopwatch.GetTimestamp(),
                TranslationError = null,
                ProvisionalTranslationText = "",
                TranslationRevision = revision,
                PendingDisplayGroupAttempt = 0,
                LastVisibleTranslation = old.TranslationDisplayGroup is null ? text : old.LastVisibleTranslation
            };
            if (error is null && old.TranslationDisplayGroup is { } group)
            {
                var groupMembers = _rows.Where(x => x.TranslationDisplayGroup == group).ToList();
                if (groupMembers.Count > 0 && groupMembers.All(x => !string.IsNullOrWhiteSpace(x.TranslationText) && x.TranslationRevision == x.Revision))
                {
                    for (int j = 0; j < _rows.Count; j++)
                    {
                        if (_rows[j].TranslationDisplayGroup == group)
                        {
                            _rows[j] = _rows[j] with
                            {
                                TranslationDisplayGroup = null,
                                LastVisibleTranslation = _rows[j].TranslationText,
                                PendingDisplayGroupAttempt = 0
                            };
                        }
                    }
                }
            }
            CaptionDiagnostics.Record("translation", error is null ? "accepted" : "error-retained", session, segmentId,
                revision, _rows[i].SourceText.Length, _rows[i].DisplayTranslation.Length, _rows.Count);
        }
        Changed?.Invoke();
        return true;
    }
    public IReadOnlyList<CaptionSegment> Snapshot()
    {
        lock (_sync)
        {
            var snapshot = _rows.ToArray();
            foreach (var group in snapshot.Where(x => x.TranslationDisplayGroup is not null).GroupBy(x => x.TranslationDisplayGroup))
            {
                var fallback = group.First().LastVisibleTranslation;
                bool first = true;
                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (snapshot[i].TranslationDisplayGroup != group.Key) continue;
                    snapshot[i] = snapshot[i] with { DisplayTranslationOverride = first ? fallback : "" };
                    first = false;
                }
            }
            return snapshot;
        }
    }
    // Drafts cannot replace an exact or newer translation and must be a prefix-compatible draft of current source.
    public bool ApplyProvisionalTranslation(long session, string id, int revision, string text)
        => ApplyProvisionalTranslation(session, id, revision, "", text);
    public bool ApplyProvisionalTranslation(long session, string id, int revision, string sourceText, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        lock (_sync)
        {
            if (session != _session)
                return false;
            int i = _rows.FindIndex(x => x.SessionId == session && x.SegmentId == id && x.Revision > revision && x.TranslationRevision < revision && x.TranslationText.Length == 0);
            if (i < 0)
            {
                CaptionDiagnostics.Record("translation", "reject-stale", session, id, revision);
                return false;
            }

            var current = _rows[i];
            if (string.IsNullOrWhiteSpace(current.SourceText))
                return false;

            if (sourceText.Length > 0 && !IsCompatibleDraft(sourceText, current.SourceText))
                return false;

            _rows[i] = _rows[i] with
            {
                ProvisionalTranslationText = text,
                TranslationRevision = revision,
                PendingDisplayGroupAttempt = 0,
                LastVisibleTranslation = current.TranslationDisplayGroup is null ? text : current.LastVisibleTranslation
            };
        }
        Changed?.Invoke();
        return true;
    }

    private static readonly string[] s_negationWords = [
        "not", "no", "never", "neither", "nor", "none",
        "don't", "doesn't", "didn't", "won't", "wouldn't", "can't", "cannot",
        "couldn't", "shouldn't", "isn't", "aren't", "wasn't", "weren't",
        "without", "nowhere", "nothing", "nobody", "hardly", "barely"
    ];

    private static readonly char[] s_cjkNegationChars = ['不', '没', '未', '无', '非', '别', '否'];

    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0x3040 && c <= 0x309F) ||
        (c >= 0x30A0 && c <= 0x30FF) ||
        (c >= 0xAC00 && c <= 0xD7AF);

    private static bool ContainsReversalTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var c in s_cjkNegationChars)
        {
            if (text.Contains(c))
                return true;
        }

        var words = text.Split(
            [' ', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            foreach (var neg in s_negationWords)
            {
                if (string.Equals(word, neg, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    public static bool IsCompatibleDraft(string oldSource, string newSource)
    {
        if (string.IsNullOrWhiteSpace(oldSource) || string.IsNullOrWhiteSpace(newSource))
            return false;

        string cleanOld = oldSource.TrimEnd('.', '!', '?', '。', '！', '？', ' ');
        string cleanNew = newSource.TrimEnd('.', '!', '?', '。', '！', '？', ' ');
        if (cleanOld.Length == 0 || cleanNew.Length == 0)
            return false;

        // Converting a statement to a question (or vice versa) changes semantic role
        bool oldIsQuestion = oldSource.TrimEnd().EndsWith('?') || oldSource.TrimEnd().EndsWith('？');
        bool newIsQuestion = newSource.TrimEnd().EndsWith('?') || newSource.TrimEnd().EndsWith('？');
        if (newIsQuestion != oldIsQuestion)
            return false;

        // Exact match
        if (string.Equals(cleanOld, cleanNew, StringComparison.OrdinalIgnoreCase))
            return true;

        // Strict prefix extension: newSource must strictly start with cleanOld
        if (!cleanNew.StartsWith(cleanOld, StringComparison.OrdinalIgnoreCase))
            return false;

        // Must respect word boundary: cannot extend in the middle of a Latin/alphanumeric word (e.g. cat -> category)
        char nextChar = cleanNew[cleanOld.Length];
        char lastOldChar = cleanOld[^1];
        if (!char.IsWhiteSpace(nextChar) && !char.IsPunctuation(nextChar) && !IsCjk(nextChar) && !IsCjk(lastOldChar))
            return false;

        // Number token boundary: a digit must not be mutated into a decimal, thousands grouping, range, or fraction
        if (char.IsDigit(lastOldChar))
        {
            if (char.IsDigit(nextChar) || nextChar is '%' or '‰' or '°' or '℃' or '℉')
                return false;
            if (char.IsPunctuation(nextChar) && cleanOld.Length + 1 < cleanNew.Length && char.IsDigit(cleanNew[cleanOld.Length + 1]))
                return false;
        }

        // The newly appended extension must not introduce negation or reversal words
        string extension = cleanNew[cleanOld.Length..];
        if (ContainsReversalTokens(extension))
            return false;

        return true;
    }
}
