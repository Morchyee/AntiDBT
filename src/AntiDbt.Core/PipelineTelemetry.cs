using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AntiDbt.Core;

public enum TelemetryOutcome
{
    Success,
    Cancelled,
    Failed,
    CacheHit
}

public sealed record SegmentTelemetryRecord
{
    public long SessionId { get; init; }
    public string SegmentId { get; init; } = "";
    public int Revision { get; init; }
    public bool IsFinal { get; init; }
    public string Language { get; init; } = "";

    public double? QueueWaitMs { get; init; }
    public double? AsrServiceMs { get; init; }
    public double? MtDebounceWaitMs { get; init; }
    public double? MtServiceMs { get; init; }
    public double? UiApplyMs { get; init; }
    public double? TotalPipelineMs { get; init; }

    public TelemetryOutcome Outcome { get; init; } = TelemetryOutcome.Success;
    public string? ErrorKind { get; init; }
}

public sealed record TelemetrySummary
{
    public int SampleCount { get; init; }
    public int FinalCount { get; init; }
    public int PartialCount { get; init; }
    public int CacheHitCount { get; init; }
    public int CancelledCount { get; init; }
    public int FailedCount { get; init; }

    public LatencySnapshot QueueWait { get; init; } = new(0, 0, 0);
    public LatencySnapshot AsrService { get; init; } = new(0, 0, 0);
    public LatencySnapshot MtDebounceWait { get; init; } = new(0, 0, 0);
    public LatencySnapshot MtService { get; init; } = new(0, 0, 0);
    public LatencySnapshot UiApply { get; init; } = new(0, 0, 0);
    public LatencySnapshot TotalPipeline { get; init; } = new(0, 0, 0);

    public string? OsVersion { get; init; }
    public string? DotNetRuntime { get; init; }
    public string? AsrProvider { get; init; }
    public string? TranslationProvider { get; init; }
    public string? AsrModel { get; init; }
    public string? TranslationModel { get; init; }
    public string? SourceLanguage { get; init; }
    public string? TargetLanguage { get; init; }
    public string Disclaimer { get; init; } = "端到端耗时测量范围为音频出队经 ASR/MT 处理至 UI 集合更新（UiApplied）；不包含显示硬件刷新延迟与音频声学传播延迟。";
}

public sealed class PipelineTelemetryCollector
{
    private readonly object _sync = new();
    private readonly List<SegmentTelemetryRecord> _records = [];
    private readonly int _maxRecords;

    public bool Enabled { get; set; } = true;

    public PipelineTelemetryCollector(int maxRecords = 2000)
    {
        _maxRecords = Math.Clamp(maxRecords, 10, 100000);
    }

    public void Record(SegmentTelemetryRecord record)
    {
        if (!Enabled) return;
        lock (_sync)
        {
            if (_records.Count >= _maxRecords)
                _records.RemoveAt(0);
            _records.Add(record);
        }
    }

    public IReadOnlyList<SegmentTelemetryRecord> Snapshot()
    {
        lock (_sync)
            return _records.ToArray();
    }

    public void Clear()
    {
        lock (_sync)
            _records.Clear();
    }

    public TelemetrySummary Summarize(AppSettings? settings = null)
    {
        SegmentTelemetryRecord[] samples;
        lock (_sync)
            samples = _records.ToArray();

        var qw = new LatencyMonitor();
        var asr = new LatencyMonitor();
        var debounce = new LatencyMonitor();
        var mt = new LatencyMonitor();
        var ui = new LatencyMonitor();
        var total = new LatencyMonitor();

        int finals = 0, partials = 0, hits = 0, cancelled = 0, failed = 0;

        foreach (var r in samples)
        {
            if (r.IsFinal) finals++; else partials++;
            switch (r.Outcome)
            {
                case TelemetryOutcome.CacheHit: hits++; break;
                case TelemetryOutcome.Cancelled: cancelled++; break;
                case TelemetryOutcome.Failed: failed++; break;
            }

            if (r.QueueWaitMs.HasValue) qw.Record(TimeSpan.FromMilliseconds(r.QueueWaitMs.Value));
            if (r.AsrServiceMs.HasValue) asr.Record(TimeSpan.FromMilliseconds(r.AsrServiceMs.Value));
            if (r.MtDebounceWaitMs.HasValue) debounce.Record(TimeSpan.FromMilliseconds(r.MtDebounceWaitMs.Value));
            if (r.MtServiceMs.HasValue) mt.Record(TimeSpan.FromMilliseconds(r.MtServiceMs.Value));
            if (r.UiApplyMs.HasValue) ui.Record(TimeSpan.FromMilliseconds(r.UiApplyMs.Value));
            if (r.TotalPipelineMs.HasValue) total.Record(TimeSpan.FromMilliseconds(r.TotalPipelineMs.Value));
        }

        return new TelemetrySummary
        {
            SampleCount = samples.Length,
            FinalCount = finals,
            PartialCount = partials,
            CacheHitCount = hits,
            CancelledCount = cancelled,
            FailedCount = failed,
            QueueWait = qw.Snapshot(),
            AsrService = asr.Snapshot(),
            MtDebounceWait = debounce.Snapshot(),
            MtService = mt.Snapshot(),
            UiApply = ui.Snapshot(),
            TotalPipeline = total.Snapshot(),
            OsVersion = RuntimeInformation.OSDescription,
            DotNetRuntime = RuntimeInformation.FrameworkDescription,
            AsrProvider = settings?.Asr?.Provider,
            TranslationProvider = settings?.Translation?.Provider,
            AsrModel = settings?.Asr?.Model,
            TranslationModel = settings?.Translation?.Model,
            SourceLanguage = settings?.SourceLanguage,
            TargetLanguage = settings?.TargetLanguage
        };
    }

    public string ExportJson(AppSettings? settings = null, bool pretty = true)
    {
        var summary = Summarize(settings);
        return JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = pretty,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}
