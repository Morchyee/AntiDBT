using AntiDbt.Core;
using AntiDbt.Providers;
using System.Diagnostics;
using System.Text.Json;

internal static class SentenceFile
{
    // Deterministic real-model stress: repeats the bundled real speech, without wall-clock pacing.
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[1], out int seconds)) return 2;
        string directory = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(directory);
        bool baseline = args[3] == "baseline";
        bool forced = args[3] == "forced";
        var model = Path.GetFullPath("models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17");
        var config = new AsrConfiguration { Provider = "SherpaOnnx", Model = model, Language = "en" };
        if (baseline) config = config with { Options = new() { ["sentenceMaxSeconds"] = "30", ["sentencePreferredSeconds"] = "30", ["sentenceMaxChars"] = "1000", ["sentenceSilenceMs"] = "3000", ["rule2TrailingSilence"] = "1.2", ["rule3UtteranceLength"] = "20" } };
        if (forced) config = config with { Options = new() { ["sentencePreferredSeconds"] = "9", ["sentenceMaxChars"] = "1000", ["sentenceSilenceMs"] = "3000", ["sentenceVadThreshold"] = "0.0001", ["rule1TrailingSilence"] = "1000", ["rule2TrailingSilence"] = "1000" } };
        await using var asr = (SherpaOnnxStreamingAsrProvider.Session)new SherpaOnnxStreamingAsrProvider().CreateSession(config);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await asr.ConnectAsync(timeout.Token);
        var clock = Stopwatch.StartNew();
        var rows = new List<AsrResult>();
        var reader = Task.Run(async () => { await foreach (var row in asr.GetResultsAsync(timeout.Token)) rows.Add(row); });
        var wav = await File.ReadAllBytesAsync(Path.Combine(model, "test_wavs/0.wav"), timeout.Token);
        var pcm = wav[44..];
        long totalBytes = seconds * 32000L;
        for (long offset = 0; offset < totalBytes;)
        {
            int sourceOffset = (int)(offset % pcm.Length);
            int length = (int)Math.Min(Math.Min(6400, pcm.Length - sourceOffset), totalBytes - offset);
            await asr.SendAudioAsync(pcm.AsMemory(sourceOffset, length), timeout.Token);
            offset += length;
        }
        await asr.CompleteAsync(timeout.Token);
        await reader;
        var finals = rows.Where(x => x.IsFinal).ToArray();
        var history = new SubtitleStore();
        long session = history.BeginSession();
        foreach (var row in rows) history.ApplySource(session, row);
        double maximum = finals.Select(x => (x.AudioEnd - x.AudioStart).TotalSeconds).DefaultIfEmpty().Max();
        bool passed = asr.AcceptedSamples == seconds * 16000L && asr.ProcessedSamples == asr.AcceptedSamples &&
            finals.Length >= 2 && history.Snapshot().Count == finals.Length &&
            (baseline || maximum <= 9.11) && finals.Select(x => x.SegmentId).Distinct().Count() == finals.Length &&
            history.Snapshot().All(x => x.IsFinal && x.SourceText.Length > 0);
        var report = new { passed, seconds, baseline, forced, scope = "real model; repeated bundled human-speech WAV, accelerated feeding, not uninterrupted new natural speech",
            wallSeconds = clock.Elapsed.TotalSeconds, acceptedSamples = asr.AcceptedSamples, processedSamples = asr.ProcessedSamples,
            finalSegments = finals.Length, partialEvents = rows.Count(x => !x.IsFinal), maxSegmentSeconds = maximum,
            recognizedWords = finals.Sum(x => x.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length),
            historySegments = history.Snapshot().Count, finals };
        string path = Path.Combine(directory, baseline ? $"baseline-{seconds}s.json" : $"segmented-{seconds}s.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, seconds, baseline, segments = finals.Length, maximum, acceptedSamples = asr.AcceptedSamples, processedSamples = asr.ProcessedSamples }));
        return passed ? 0 : 1;
    }
}
