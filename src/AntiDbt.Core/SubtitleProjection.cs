namespace AntiDbt.Core;
public sealed record SubtitleParagraph(string Source, string Translation, bool TranslationPending = false);
public static class SubtitleProjection
{
    public static SubtitleParagraph Compact(IReadOnlyList<CaptionSegment> rows)
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
        return Join(tail);
    }
    public static IReadOnlyList<SubtitleParagraph> Expanded(IReadOnlyList<CaptionSegment> rows)
    {
        var result = new List<SubtitleParagraph>();
        var group = new List<CaptionSegment>();
        long? session = null;
        foreach (var row in rows)
        {
            if (group.Count > 0 && session != row.SessionId)
            {
                result.Add(Join(group));
                group.Clear();
            }
            session = row.SessionId;
            group.Add(row);
            if (EndsSentence(row.SourceText) || group.Sum(x => x.SourceText.Length) >= 90)
            {
                result.Add(Join(group));
                group.Clear();
            }
        }
        if (group.Count > 0)
            result.Add(Join(group));
        return result.Where(x => x.Source.Length > 0 || x.Translation.Length > 0).ToArray();
    }
    private static SubtitleParagraph Join(IEnumerable<CaptionSegment> rows) => new(
        string.Join(" ", rows.Select(x => x.SourceText).Where(x => x.Length > 0)),
        string.Join(" ", rows.Select(x => x.TranslationText.Length > 0 ? x.TranslationText : x.ProvisionalTranslationText).Where(x => x.Length > 0)),
        rows.Any(x => x.TranslationText.Length == 0 && x.SourceText.Length > 0));
    private static bool EndsSentence(string text) => text.TrimEnd().EndsWithAny("。", "？", "！", "…", ".", "?", "!");
    private static bool EndsWithAny(this string text, params string[] suffixes) => suffixes.Any(x => text.EndsWith(x, StringComparison.Ordinal));
    private static double StopwatchSeconds(long ticks) => (double)ticks / System.Diagnostics.Stopwatch.Frequency;
}
