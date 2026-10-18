using System.Text;

namespace AntiDbt.Core;

public static class SubtitleExporter
{
    public static string ToSrt(IReadOnlyList<CaptionSegment> rows, bool includeProvisional = false)
    {
        var sb = new StringBuilder();
        int index = 1;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.SourceText))
                continue;

            var trans = row.TranslationText;
            if (string.IsNullOrWhiteSpace(trans) && includeProvisional)
                trans = row.ProvisionalTranslationText;

            var start = row.AudioStart;
            var end = row.AudioEnd > row.AudioStart
                ? row.AudioEnd
                : row.AudioStart + TimeSpan.FromSeconds(Math.Max(1.5, row.SourceText.Length * 0.08));

            sb.AppendLine(index.ToString());
            sb.AppendLine($"{FormatSrtTime(start)} --> {FormatSrtTime(end)}");
            sb.AppendLine(row.SourceText.Trim());
            if (!string.IsNullOrWhiteSpace(trans))
                sb.AppendLine(trans.Trim());
            sb.AppendLine();
            index++;
        }

        return sb.ToString().TrimEnd();
    }

    public static string ToVtt(IReadOnlyList<CaptionSegment> rows, bool includeProvisional = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();

        int index = 1;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.SourceText))
                continue;

            var trans = row.TranslationText;
            if (string.IsNullOrWhiteSpace(trans) && includeProvisional)
                trans = row.ProvisionalTranslationText;

            var start = row.AudioStart;
            var end = row.AudioEnd > row.AudioStart
                ? row.AudioEnd
                : row.AudioStart + TimeSpan.FromSeconds(Math.Max(1.5, row.SourceText.Length * 0.08));

            sb.AppendLine(index.ToString());
            sb.AppendLine($"{FormatVttTime(start)} --> {FormatVttTime(end)}");
            sb.AppendLine(row.SourceText.Trim());
            if (!string.IsNullOrWhiteSpace(trans))
                sb.AppendLine(trans.Trim());
            sb.AppendLine();
            index++;
        }

        return sb.ToString().TrimEnd();
    }

    public static string ToText(IReadOnlyList<CaptionSegment> rows, bool includeProvisional = false)
    {
        var sb = new StringBuilder();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.SourceText))
                continue;

            var trans = row.TranslationText;
            if (string.IsNullOrWhiteSpace(trans) && includeProvisional)
                trans = row.ProvisionalTranslationText;

            sb.AppendLine(row.SourceText.Trim());
            if (!string.IsNullOrWhiteSpace(trans))
                sb.AppendLine(trans.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSrtTime(TimeSpan time)
    {
        int hours = (int)time.TotalHours;
        return $"{hours:D2}:{time.Minutes:D2}:{time.Seconds:D2},{time.Milliseconds:D3}";
    }

    private static string FormatVttTime(TimeSpan time)
    {
        int hours = (int)time.TotalHours;
        return $"{hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }
}
