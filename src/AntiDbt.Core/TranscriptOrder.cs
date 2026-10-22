namespace AntiDbt.Core;

public static class TranscriptOrder
{
    public static int Compare(CaptionSegment? a, CaptionSegment? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        int sessionCmp = a.SessionId.CompareTo(b.SessionId);
        if (sessionCmp != 0) return sessionCmp;

        int timeCmp = a.TranscriptStart.Ticks.CompareTo(b.TranscriptStart.Ticks);
        if (timeCmp != 0) return timeCmp;

        return string.Compare(a.SegmentId, b.SegmentId, StringComparison.Ordinal);
    }
}
