using System.Diagnostics;
namespace AntiDbt.Core;

public sealed record CaptionSegment(long SessionId, string SegmentId, int Revision, string SourceText,
    string TranslationText, bool IsFinal, TimeSpan AudioStart, TimeSpan AudioEnd, long ReceivedTimestamp,
    long? TranslatedTimestamp, string Language, string? TranslationError = null,
    string ProvisionalTranslationText = "", int TranslationRevision = 0,
    double? AsrLatencyMs = null, double? QueueWaitMs = null,
    long? AudioCapturedTimestamp = null);

public sealed class SubtitleStore
{
    private readonly object _sync = new();
    private readonly List<CaptionSegment> _rows = [];
    private readonly Dictionary<string, int> _deleted = new();
    private int _maxHistory;
    private long _session;
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

                // Compatible Draft Rule (F2):
                // Empty or whitespace source NEVER inherits provisional translation.
                // Refinements and prefix extensions retain provisional translation.
                string provisionalTranslation = "";
                int provisionalRevision = 0;

                if (!string.IsNullOrWhiteSpace(result.Text) && old is not null)
                {
                    bool draftNotExpired = old.TranslatedTimestamp is not null
                        ? Stopwatch.GetElapsedTime(old.TranslatedTimestamp.Value) < TimeSpan.FromSeconds(5)
                        : (result.Revision - old.TranslationRevision) <= 3;

                    if (draftNotExpired && IsCompatibleDraft(old.SourceText, result.Text))
                    {
                        provisionalTranslation = old.TranslationText.Length > 0 ? old.TranslationText : old.ProvisionalTranslationText;
                        provisionalRevision = old.TranslationRevision;
                    }
                }

                long? translatedTimestamp = exactTranslation.Length > 0
                    ? old?.TranslatedTimestamp
                    : (provisionalTranslation.Length > 0 ? (old?.TranslatedTimestamp ?? old?.ReceivedTimestamp) : null);
                long? audioCaptured = old?.SourceText == result.Text ? old.AudioCapturedTimestamp : null;

                row = new(session, result.SegmentId, result.Revision, result.Text,
                    exactTranslation, result.IsFinal,
                    result.AudioStart, result.AudioEnd, Stopwatch.GetTimestamp(),
                    translatedTimestamp, result.Language ?? "auto", null,
                    provisionalTranslation, provisionalRevision,
                    null, null, audioCaptured);
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
