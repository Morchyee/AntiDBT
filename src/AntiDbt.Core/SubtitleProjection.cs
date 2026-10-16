using System.ComponentModel;
using System.Diagnostics;

namespace AntiDbt.Core;

public sealed class SubtitleParagraph : INotifyPropertyChanged, IEquatable<SubtitleParagraph>
{
    private string _source;
    private string _translation;
    private bool _translationPending;
    private string? _translationError;
    private long _sessionId;
    private string _segmentId;
    private int _revision;
    private bool _canRetry;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SubtitleParagraph(
        string source,
        string translation,
        bool translationPending = false,
        string? translationError = null,
        long sessionId = 0,
        string segmentId = "",
        int revision = 0,
        bool canRetry = false)
    {
        _source = source ?? "";
        _translation = translation ?? "";
        _translationPending = translationPending;
        _translationError = translationError;
        _sessionId = sessionId;
        _segmentId = segmentId ?? "";
        _revision = revision;
        _canRetry = canRetry;
    }

    public string Source
    {
        get => _source;
        set => SetField(ref _source, value ?? "");
    }

    public string Translation
    {
        get => _translation;
        set => SetField(ref _translation, value ?? "");
    }

    public bool TranslationPending
    {
        get => _translationPending;
        set
        {
            if (SetField(ref _translationPending, value))
            {
                OnPropertyChanged(nameof(TranslationOpacity));
            }
        }
    }

    public string? TranslationError
    {
        get => _translationError;
        set
        {
            if (SetField(ref _translationError, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ErrorDisplay));
            }
        }
    }

    public long SessionId
    {
        get => _sessionId;
        set => SetField(ref _sessionId, value);
    }

    public string SegmentId
    {
        get => _segmentId;
        set => SetField(ref _segmentId, value ?? "");
    }

    public int Revision
    {
        get => _revision;
        set => SetField(ref _revision, value);
    }

    public bool CanRetry
    {
        get => _canRetry;
        set => SetField(ref _canRetry, value);
    }

    public bool HasError => !string.IsNullOrEmpty(TranslationError);
    public string ErrorDisplay => string.IsNullOrEmpty(TranslationError) ? "" : $"⚠️ 翻译异常: {TranslationError}";
    public double TranslationOpacity => TranslationPending ? 0.65 : 1.0;

    public void UpdateFrom(SubtitleParagraph other)
    {
        if (other is null) return;
        Source = other.Source;
        Translation = other.Translation;
        TranslationPending = other.TranslationPending;
        TranslationError = other.TranslationError;
        SessionId = other.SessionId;
        SegmentId = other.SegmentId;
        Revision = other.Revision;
        CanRetry = other.CanRetry;
    }

    private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public bool Equals(SubtitleParagraph? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _source == other._source &&
               _translation == other._translation &&
               _translationPending == other._translationPending &&
               _translationError == other._translationError &&
               _sessionId == other._sessionId &&
               _segmentId == other._segmentId &&
               _revision == other._revision &&
               _canRetry == other._canRetry;
    }

    public override bool Equals(object? obj) => Equals(obj as SubtitleParagraph);

    public override int GetHashCode() =>
        HashCode.Combine(_source, _translation, _translationPending, _translationError, _sessionId, _segmentId, _revision, _canRetry);

    public static bool operator ==(SubtitleParagraph? left, SubtitleParagraph? right) => Equals(left, right);
    public static bool operator !=(SubtitleParagraph? left, SubtitleParagraph? right) => !Equals(left, right);
}

public static class SubtitleProjection
{
    public static SubtitleParagraph Compact(IReadOnlyList<CaptionSegment> rows, long currentSession = 0)
    {
        if (rows.Count == 0)
            return new("", "");
        var tail = new List<CaptionSegment>();
        CaptionSegment? next = null;
        var length = 0;
        foreach (var row in rows.Reverse())
        {
            if (next is not null && (row.SessionId != next.SessionId ||
                StopwatchSeconds(next.ReceivedTimestamp - row.ReceivedTimestamp) >= 5))
                break;
            if (next is not null && EndsSentence(row.SourceText))
                break;
            tail.Add(row);
            length += row.SourceText.Length;
            if (length >= 2000)
                break;
            next = row;
        }
        tail.Reverse();
        return Join(tail, currentSession);
    }

    public static IReadOnlyList<SubtitleParagraph> Expanded(IReadOnlyList<CaptionSegment> rows, long currentSession = 0)
    {
        var result = new List<SubtitleParagraph>();
        var group = new List<CaptionSegment>();
        var currentLength = 0;
        foreach (var row in rows)
        {
            if (group.Count > 0 && ShouldStartNewParagraph(row, group[^1], currentLength))
            {
                result.Add(Join(group, currentSession));
                group.Clear();
                currentLength = 0;
            }
            group.Add(row);
            currentLength += row.SourceText.Length;
        }
        if (group.Count > 0)
            result.Add(Join(group, currentSession));
        return result.Where(x => !string.IsNullOrWhiteSpace(x.Source)).ToArray();
    }

    private static SubtitleParagraph Join(IEnumerable<CaptionSegment> rows, long currentSession = 0)
    {
        var list = rows as IList<CaptionSegment> ?? rows.ToList();
        var last = list.Count > 0 ? list[^1] : null;
        var hasPending = list.Any(x => x.TranslationText.Length == 0 && !string.IsNullOrWhiteSpace(x.SourceText) && string.IsNullOrEmpty(x.TranslationError));

        var errorSegment = list.FirstOrDefault(x => !string.IsNullOrEmpty(x.TranslationError) && x.IsFinal)
            ?? list.FirstOrDefault(x => !string.IsNullOrEmpty(x.TranslationError));
        var target = errorSegment ?? last;

        var firstError = errorSegment?.TranslationError;
        var isCurrentSession = currentSession == 0 || (target is not null && target.SessionId == currentSession);
        var canRetry = isCurrentSession && list.Any(x => !string.IsNullOrEmpty(x.TranslationError) && x.IsFinal);

        var source = JoinTexts(list.Select(x => x.SourceText));
        var translation = string.IsNullOrWhiteSpace(source)
            ? ""
            : JoinTexts(list.Select(x =>
            {
                if (string.IsNullOrWhiteSpace(x.SourceText))
                    return "";
                if (x.TranslationText.Length > 0)
                    return x.TranslationText;
                if (x.TranslatedTimestamp is not null && Stopwatch.GetElapsedTime(x.TranslatedTimestamp.Value) >= TimeSpan.FromSeconds(5))
                    return "";
                return x.ProvisionalTranslationText;
            }));

        return new(
            source,
            translation,
            hasPending,
            firstError,
            target?.SessionId ?? 0,
            target?.SegmentId ?? "",
            target?.Revision ?? 0,
            canRetry);
    }

    public static bool ShouldStartNewParagraph(CaptionSegment current, CaptionSegment previous, int currentGroupLength)
    {
        if (current.SessionId != previous.SessionId)
            return true;

        if (currentGroupLength >= 180)
            return true;

        if (current.AudioStart > previous.AudioEnd && (current.AudioStart - previous.AudioEnd) >= TimeSpan.FromSeconds(2.5))
            return true;

        if (StopwatchSeconds(current.ReceivedTimestamp - previous.ReceivedTimestamp) >= 3.5)
            return true;

        return false;
    }

    public static string JoinTexts(IEnumerable<string> parts)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var raw in parts)
        {
            var part = raw?.Trim();
            if (string.IsNullOrEmpty(part))
                continue;
            if (sb.Length > 0)
            {
                char last = sb[^1];
                char first = part[0];
                if (NeedsSpaceBetween(last, first))
                    sb.Append(' ');
            }
            sb.Append(part);
        }
        return sb.ToString();
    }

    private static bool NeedsSpaceBetween(char last, char first)
    {
        if (char.IsWhiteSpace(last) || char.IsWhiteSpace(first))
            return false;

        if (",.?!:;)%]\"'".Contains(first))
            return false;

        if ("([{“‘".Contains(last))
            return false;

        if (IsCjkPunctuation(last))
            return char.IsAsciiLetterOrDigit(first) || IsHangul(first);

        if (IsCjkPunctuation(first))
            return false;

        // Chinese / Japanese kanji & kana join without space
        if (IsHanOrKana(last) && IsHanOrKana(first))
            return false;

        if (IsHanOrKana(last) && IsCjkPunctuation(first))
            return false;

        if (IsCjkPunctuation(last) && IsHanOrKana(first))
            return false;

        // Hangul (Korean) words join with spaces
        return true;
    }

    private static bool IsHanOrKana(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0x3040 && c <= 0x30FF);

    private static bool IsHangul(char c) =>
        (c >= 0xAC00 && c <= 0xD7AF) ||
        (c >= 0x1100 && c <= 0x11FF) ||
        (c >= 0x3130 && c <= 0x318F);

    private static bool IsCjkPunctuation(char c) =>
        c is '。' or '，' or '！' or '？' or '、' or '；' or '：' or '“' or '”' or '‘' or '’' or '（' or '）' or '《' or '》' or '【' or '】' or '…' or '—';

    private static bool EndsSentence(string text) => text.TrimEnd().EndsWithAny("。", "？", "！", "…", ".", "?", "!");
    private static bool EndsWithAny(this string text, params string[] suffixes) => suffixes.Any(x => text.EndsWith(x, StringComparison.Ordinal));

    private static double StopwatchSeconds(long ticks) => (double)ticks / System.Diagnostics.Stopwatch.Frequency;
}
