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
                row = new(session, result.SegmentId, result.Revision, result.Text,
                    old?.SourceText == result.Text ? old.TranslationText : "", result.IsFinal,
                    result.AudioStart, result.AudioEnd, Stopwatch.GetTimestamp(),
                    old?.SourceText == result.Text ? old.TranslatedTimestamp : null, result.Language ?? "auto", null,
                    old is null ? "" : old.TranslationText.Length > 0 ? old.TranslationText : old.ProvisionalTranslationText, old?.TranslationRevision ?? 0);
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
    // Drafts cannot replace an exact or newer translation and are labeled pending by the UI.
    public bool ApplyProvisionalTranslation(long session, string id, int revision, string text)
    {
        lock (_sync)
        {
            if (session != _session)
                return false;
            int i = _rows.FindIndex(x => x.SessionId == session && x.SegmentId == id && x.Revision > revision && x.TranslationRevision < revision && x.TranslationText.Length == 0);
            if (i < 0)
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
}
