using System.Diagnostics;
namespace AntiDbt.Core;

public sealed record CaptionSegment(long SessionId, string SegmentId, int Revision, string SourceText,
    string TranslationText, bool IsFinal, TimeSpan AudioStart, TimeSpan AudioEnd, long ReceivedTimestamp,
    long? TranslatedTimestamp, string Language, string? TranslationError = null,
    string ProvisionalTranslationText = "", int TranslationRevision = 0);

public sealed class SubtitleStore
{
    private readonly object _sync = new();
    private readonly List<CaptionSegment> _rows = [];
    private readonly Dictionary<string, int> _deleted = new();
    private readonly int _maxHistory;
    private long _session;
    public event Action? Changed;
    public SubtitleStore(int maxHistory = 2000) => _maxHistory = Math.Clamp(maxHistory, 1, 10000);
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
        }
        Changed?.Invoke();
        return session;
    }
    public CaptionSegment? ApplySource(long session, AsrResult result)
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
                if (old is not null && result.Revision < old.Revision)
                    return null;
                if (index >= 0)
                    _rows.RemoveAt(index);
                row = null;
                _deleted[result.SegmentId] = result.Revision;
                if (_deleted.Count > 10000)
                    _deleted.Remove(_deleted.Keys.First());
            }
            else
            {
                if (old is not null && (result.Revision < old.Revision ||
                    result.Revision == old.Revision && (result.Text != old.SourceText || result.IsFinal == old.IsFinal || old.IsFinal)))
                    return null;

                string exactTranslation = old?.SourceText == result.Text ? old.TranslationText : "";
                long? translatedTimestamp = old?.SourceText == result.Text ? old.TranslatedTimestamp : null;

                // Compatible Draft Rule (F2):
                // Empty or whitespace source NEVER inherits provisional translation.
                // Refinements and prefix extensions retain provisional translation.
                string provisionalTranslation = "";
                int provisionalRevision = 0;

                if (!string.IsNullOrWhiteSpace(result.Text) && old is not null)
                {
                    if (IsCompatibleDraft(old.SourceText, result.Text))
                    {
                        provisionalTranslation = old.TranslationText.Length > 0 ? old.TranslationText : old.ProvisionalTranslationText;
                        provisionalRevision = old.TranslationRevision;
                    }
                }

                row = new(session, result.SegmentId, result.Revision, result.Text,
                    exactTranslation, result.IsFinal,
                    result.AudioStart, result.AudioEnd, Stopwatch.GetTimestamp(),
                    translatedTimestamp, result.Language ?? "auto", null,
                    provisionalTranslation, provisionalRevision);
                if (index >= 0)
                    _rows[index] = row;
                else
                    _rows.Add(row);
                _rows.Sort((a, b) => a.SessionId != b.SessionId ? a.SessionId.CompareTo(b.SessionId) : a.AudioStart.CompareTo(b.AudioStart));
                while (_rows.Count > _maxHistory)
                    _rows.RemoveAt(0);
            }
        }
        Changed?.Invoke();
        return row;
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
                return false;
            _rows[i] = _rows[i] with { TranslationError = null };
        }
        Changed?.Invoke();
        return true;
    }
    private bool UpdateTranslation(long session, string segmentId, int revision, string text, string? error)
    {
        lock (_sync)
        {
            if (session != _session)
                return false;
            var i = _rows.FindIndex(x => x.SessionId == session && x.SegmentId == segmentId && x.Revision == revision);
            if (i < 0)
                return false;
            _rows[i] = _rows[i] with
            {
                TranslationText = text,
                TranslatedTimestamp = Stopwatch.GetTimestamp(),
                TranslationError = error,
                ProvisionalTranslationText = "",
                TranslationRevision = revision
            };
        }
        Changed?.Invoke();
        return true;
    }
    public IReadOnlyList<CaptionSegment> Snapshot()
    {
        lock (_sync)
            return _rows.ToArray();
    }
    // Drafts cannot replace an exact or newer translation and must be a prefix-compatible draft of current source.
    public bool ApplyProvisionalTranslation(long session, string id, int revision, string text)
        => ApplyProvisionalTranslation(session, id, revision, "", text);
    public bool ApplyProvisionalTranslation(long session, string id, int revision, string sourceText, string text)
    {
        lock (_sync)
        {
            if (session != _session)
                return false;
            int i = _rows.FindIndex(x => x.SessionId == session && x.SegmentId == id && x.Revision > revision && x.TranslationRevision < revision && x.TranslationText.Length == 0);
            if (i < 0)
                return false;

            var current = _rows[i];
            if (string.IsNullOrWhiteSpace(current.SourceText))
                return false;

            if (sourceText.Length > 0 && !IsCompatibleDraft(sourceText, current.SourceText))
                return false;

            _rows[i] = _rows[i] with
            {
                ProvisionalTranslationText = text,
                TranslationRevision = revision
            };
        }
        Changed?.Invoke();
        return true;
    }

    internal static bool IsCompatibleDraft(string oldSource, string newSource)
    {
        if (string.IsNullOrWhiteSpace(oldSource) || string.IsNullOrWhiteSpace(newSource))
            return false;

        string cleanOld = oldSource.TrimEnd('.', '!', '?', '。', '！', '？', ' ');
        string cleanNew = newSource.TrimEnd('.', '!', '?', '。', '！', '？', ' ');
        if (cleanOld.Length == 0 || cleanNew.Length == 0)
            return false;

        // 1. Exact match or true prefix extension (e.g. "Hello" -> "Hello world")
        if (cleanNew.StartsWith(cleanOld, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. High-similarity refinement for longer sentences (e.g. ASR refined a single word in a full sentence)
        // Short phrases (e.g. "turn left" -> "turn right") have high relative change and are incompatible.
        int maxLen = Math.Max(cleanOld.Length, cleanNew.Length);
        if (maxLen >= 20)
        {
            int lcs = LongestCommonSubsequenceLength(cleanOld, cleanNew);
            if ((double)lcs / maxLen >= 0.75)
                return true;
        }

        return false;
    }

    private static int LongestCommonSubsequenceLength(string a, string b)
    {
        int n = a.Length, m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (int i = 1; i <= n; i++)
        {
            char ca = char.ToLowerInvariant(a[i - 1]);
            for (int j = 1; j <= m; j++)
            {
                char cb = char.ToLowerInvariant(b[j - 1]);
                curr[j] = (ca == cb) ? prev[j - 1] + 1 : Math.Max(prev[j], curr[j - 1]);
            }
            Array.Copy(curr, prev, m + 1);
        }
        return prev[m];
    }
}
