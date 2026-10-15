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
        var currentLength = 0;
        foreach (var row in rows)
        {
            if (group.Count > 0 && ShouldStartNewParagraph(row, group[^1], currentLength))
            {
                result.Add(Join(group));
                group.Clear();
                currentLength = 0;
            }
            group.Add(row);
            currentLength += row.SourceText.Length;
        }
        if (group.Count > 0)
            result.Add(Join(group));
        return result.Where(x => x.Source.Length > 0 || x.Translation.Length > 0).ToArray();
    }

    private static SubtitleParagraph Join(IEnumerable<CaptionSegment> rows) => new(
        JoinTexts(rows.Select(x => x.SourceText)),
        JoinTexts(rows.Select(x => x.TranslationText.Length > 0 ? x.TranslationText : x.ProvisionalTranslationText)),
        rows.Any(x => x.TranslationText.Length == 0 && x.SourceText.Length > 0));

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
            return char.IsAsciiLetterOrDigit(first);

        if (IsCjkPunctuation(first))
            return false;

        if (IsCjkChar(last) && IsCjkChar(first))
            return false;

        if (IsCjkChar(last) && IsCjkPunctuation(first))
            return false;

        if (IsCjkPunctuation(last) && IsCjkChar(first))
            return false;

        return true;
    }

    private static bool IsCjkChar(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0x3040 && c <= 0x30FF) ||
        (c >= 0xAC00 && c <= 0xD7AF);

    private static bool IsCjkPunctuation(char c) =>
        c is '。' or '，' or '！' or '？' or '、' or '；' or '：' or '“' or '”' or '‘' or '’' or '（' or '）' or '《' or '》' or '【' or '】' or '…' or '—';

    private static bool EndsSentence(string text) => text.TrimEnd().EndsWithAny("。", "？", "！", "…", ".", "?", "!");
    private static bool EndsWithAny(this string text, params string[] suffixes) => suffixes.Any(x => text.EndsWith(x, StringComparison.Ordinal));

    private static double StopwatchSeconds(long ticks) => (double)ticks / System.Diagnostics.Stopwatch.Frequency;
}
