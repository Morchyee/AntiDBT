using System.Text.Json.Serialization;

namespace AntiDbt.Core;

public sealed record AudioChunk(byte[] Pcm, TimeSpan AudioStart, TimeSpan AudioDuration, long CapturedTimestamp);
public sealed record AsrResult(string SegmentId, int Revision, string Text, bool IsFinal,
    TimeSpan AudioStart, TimeSpan AudioEnd, string? Language = null, bool IsDeleted = false);
public sealed record TranslationRequest(long SessionId, string SegmentId, int Revision, string Text,
    string SourceLanguage, string TargetLanguage, IReadOnlyList<string> Context);

public interface IAudioSource : IAsyncDisposable
{
    string Name
    {
        get;
    }
    Task StartAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<AudioChunk> ReadChunksAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
public interface IAsrProvider
{
    IAsrSession CreateSession(AsrConfiguration configuration);
}
public interface IAsrSession : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken);
    IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
}
public interface ITranslationProvider
{
    Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}

public sealed record AsrConfiguration
{
    public string Provider { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public string Model { get; init; } = "";
    public string Language { get; init; } = "en";
    [JsonIgnore] public string ApiKey { get; init; } = "";
    public Dictionary<string, string> Options { get; init; } = new();
    [JsonIgnore] public Dictionary<string, string> Secrets { get; init; } = new();
}
public sealed record TranslationConfiguration
{
    public string Provider { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public string Model { get; init; } = "";
    [JsonIgnore] public string ApiKey { get; init; } = "";
    public Dictionary<string, string> Options { get; init; } = new();
    [JsonIgnore] public Dictionary<string, string> Secrets { get; init; } = new();
    public int TimeoutSeconds { get; init; } = 30;
    public int DebounceMilliseconds { get; init; } = 350;
    public int ContextSentences { get; init; } = 4;
}
public sealed record AppSettings
{
    // Empty is the legacy sentinel; LanguageSettings.Upgrade derives it from Asr.Language.
    public string SourceLanguage { get; init; } = "";
    public List<string> RecentLanguages { get; init; } = new();
    public string Theme { get; init; } = "System";
    public string FallbackTranslationProviderId { get; init; } = "";
    public bool FallbackEnabled { get; init; }
    public bool FallbackCostConsent { get; init; }
    public AsrConfiguration Asr { get; init; } = new();
    public TranslationConfiguration Translation { get; init; } = new();
    public Dictionary<string,AsrConfiguration> AsrProfiles { get; init; } = new();
    public Dictionary<string,TranslationConfiguration> TranslationProfiles { get; init; } = new();
    public string TargetLanguage { get; init; } = "zh";
    public string FontSize { get; init; } = "medium";
    public bool ShowSource { get; init; } = true;
    public bool Topmost { get; init; } = true;
    public string AudioDeviceId { get; init; } = "";
    public double? WindowLeft
    {
        get; init;
    }
    public double? WindowTop
    {
        get; init;
    }
    public int MaxHistory { get; init; } = 2000;
    public int SilenceSeconds { get; init; } = 7;
    public double BackgroundOpacity { get; init; } = 0.80;
}
