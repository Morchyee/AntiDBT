using System.Globalization;

namespace AntiDbt.Core;

public sealed record SentenceConfiguration(int SilenceMilliseconds = 500, double PreferredSeconds = 5,
    double MaximumSeconds = 9, int MaximumCharacters = 120, int StabilityMilliseconds = 300, double VadThreshold = 0.008)
{
    public static SentenceConfiguration From(AsrConfiguration config)
    {
        double Read(string key, double fallback, double min, double max)
        {
            if (!config.Options.TryGetValue(key, out var value)) return fallback;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number) || number < min || number > max)
                throw new ProviderException(ProviderErrorKind.Configuration, $"本地分句参数 {key} 无效。");
            return number;
        }
        double maximum = Read("sentenceMaxSeconds", 9, 2, 30);
        return new((int)Read("sentenceSilenceMs", 500, 200, 3000),
            Math.Min(Read("sentencePreferredSeconds", 5, 1, 30), maximum), maximum,
            (int)Read("sentenceMaxChars", 120, 30, 1000), (int)Read("sentenceStabilityMs", 300, 100, 1000),
            Read("sentenceVadThreshold", 0.008, 0.0001, 0.1));
    }
}

public sealed record SentenceUpdate(IReadOnlyList<AsrResult> Results, bool ResetRecognizer, string? Boundary);

// Audio-time policy, independent of worker speed. Never truncates a hypothesis or discards PCM.
// A transducer reset keeps the native encoder context and unread features. Models without that
// guarantee keep their decoder running and subtract only the already committed text prefix.
public sealed class StreamingSentenceManager(SentenceConfiguration settings, string? language, bool supportsContextReset = true)
{
    private string _id = Guid.NewGuid().ToString("N");
    private int _revision;
    private TimeSpan _start;
    private string _lastText = "", _carry = "", _committedPrefix = "";
    private readonly Queue<(TimeSpan At, string Raw)> _hypotheses = new();

    public SentenceUpdate Observe(string rawText, TimeSpan audioEnd, TimeSpan trailingSilence, bool nativeEndpoint = false,
        bool inputFinished = false, IReadOnlyDictionary<int, TimeSpan>? wordStarts = null)
    {
        string remaining = rawText;
        if (_committedPrefix.Length > 0)
        {
            if (!rawText.StartsWith(_committedPrefix, StringComparison.Ordinal))
            {
                CaptionDiagnostics.Record("asr", "committed-prefix-conflict", segment: _id, revision: _revision, sourceLength: rawText.Length);
                if (!nativeEndpoint && !inputFinished) return new([], false, null);
                remaining = "";
            }
            else remaining = rawText[_committedPrefix.Length..];
        }
        string full = _carry + remaining;
        string text = TextNormalizer.NormalizeAsrText(full);
        _hypotheses.Enqueue((audioEnd, full));
        while (_hypotheses.Count > 1 && _hypotheses.ElementAt(1).At <= audioEnd - TimeSpan.FromMilliseconds(settings.StabilityMilliseconds))
            _hypotheses.Dequeue();
        int stableLength = 0;
        if (_hypotheses.Peek().At <= audioEnd - TimeSpan.FromMilliseconds(settings.StabilityMilliseconds))
        {
            string old = _hypotheses.Peek().Raw;
            while (stableLength < Math.Min(old.Length, full.Length) && old[stableLength] == full[stableLength]) stableLength++;
        }
        bool hasText = text.Length > 0 || _lastText.Length > 0;
        var duration = audioEnd - _start;
        int safeCut = 0;
        for (int i = 1; i < full.TrimEnd().Length; i++)
            if (char.IsWhiteSpace(full[i]) && i <= stableLength && !string.IsNullOrWhiteSpace(full[..i])) safeCut = i;
        bool maximum = duration.TotalSeconds >= settings.MaximumSeconds;
        string? boundary = inputFinished ? "input-finished" : nativeEndpoint ? "native-endpoint"
            : hasText && trailingSilence >= TimeSpan.FromMilliseconds(settings.SilenceMilliseconds) ? "silence"
            : hasText && stableLength == full.Length && duration.TotalSeconds >= 1 && EndsSentence(text) ? "punctuation"
            : hasText && safeCut > 0 && (duration.TotalSeconds >= settings.PreferredSeconds || text.Length >= settings.MaximumCharacters) ? "stable-boundary"
            : maximum ? "maximum-duration" : null;
        var output = new List<AsrResult>();
        if (boundary is not null)
        {
            if (text.Length == 0) text = _lastText;
            // Keep the entire trailing word/subword with the next hypothesis. Native reset
            // preserves pending features; carry preserves tokens already decoded from that word.
            bool split = boundary == "stable-boundary" || maximum && !inputFinished && !EndsSentence(text);
            int cut = split ? full.TrimEnd().LastIndexOf(' ') : -1;
            if (boundary == "stable-boundary") cut = safeCut;
            string tail = cut > 0 ? full[cut..] : "";
            if (cut > 0) text = TextNormalizer.NormalizeAsrText(full[..cut]);
            int nativeCut = cut - _carry.Length + _committedPrefix.Length;
            var end = tail.Length > 0 && wordStarts is not null && wordStarts.TryGetValue(nativeCut, out var wordTime) && wordTime > _start && wordTime <= audioEnd ? wordTime : audioEnd;
            if (text.Length > 0) output.Add(new(_id, ++_revision, text, true, _start, end, language));
            bool reset = nativeEndpoint || inputFinished || supportsContextReset;
            _committedPrefix = reset ? "" : rawText[..Math.Max(0, rawText.Length - tail.Length)];
            _carry = reset && !inputFinished ? tail : "";
            CaptionDiagnostics.Record("asr", "sentence-final", segment: _id, revision: _revision,
                sourceLength: text.Length, detail: boundary, audioEnd: end.TotalSeconds);
            _id = Guid.NewGuid().ToString("N");
            _revision = 0;
            _start = end;
            _lastText = TextNormalizer.NormalizeAsrText(tail);
            _hypotheses.Clear();
            if (_lastText.Length > 0 && !inputFinished)
                output.Add(new(_id, ++_revision, _lastText, false, _start, audioEnd, language));
            if (output.Count > 1) output[0] = output[0] with { BatchSize = output.Count };
            return new(output, reset, boundary);
        }
        if (text.Length > 0 && text != _lastText)
        {
            _lastText = text;
            output.Add(new(_id, ++_revision, text, false, _start, audioEnd, language));
        }
        return new(output, false, null);
    }
    private static bool EndsSentence(string text) => text.Length > 0 && ".!?。！？".Contains(text[^1]);
}
