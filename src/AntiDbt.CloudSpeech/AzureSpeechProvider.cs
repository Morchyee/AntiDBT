using AntiDbt.Core;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
namespace AntiDbt.CloudSpeech;
public sealed class AzureSpeechProvider : IAsrProvider {
    readonly Func<AsrConfiguration, ISpeechTransport> factory;
    public AzureSpeechProvider() : this(c => new AzureTransport(c)) { }
    public AzureSpeechProvider(Func<AsrConfiguration, ISpeechTransport> factory) => this.factory = factory;
    public IAsrSession CreateSession(AsrConfiguration configuration) => new SpeechSession(() => {
        if (string.IsNullOrWhiteSpace(configuration.ApiKey) || !configuration.Options.TryGetValue("region", out var region) || !System.Text.RegularExpressions.Regex.IsMatch(region, "^[a-z]+[0-9]*$")) throw SpeechValidation.Bad();
        SpeechValidation.Language(configuration);
        return factory(configuration);
    });
}
internal sealed class AzureTransport : ISpeechTransport {
    readonly SpeechConfig config;
    readonly AudioStreamFormat format;
    readonly PushAudioInputStream push;
    readonly AudioConfig audio;
    readonly SpeechRecognizer recognizer;
    readonly NativeResources resources = new();
    int disposed;
    public event Action<SpeechEvent>? Result;
    public event Action<ProviderErrorKind>? Failed;
    public event Action? Stopped;
    public AzureTransport(AsrConfiguration c) {
        config = SpeechConfig.FromSubscription(c.ApiKey, c.Options["region"]);
        config.SpeechRecognitionLanguage = SpeechValidation.Language(c);
        try {
        format = resources.Acquire(() => AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
        push = resources.Acquire(() => AudioInputStream.CreatePushStream(format));
        audio = resources.Acquire(() => AudioConfig.FromStreamInput(push));
        recognizer = resources.Acquire(() => new SpeechRecognizer(config, audio));
        recognizer.Recognizing += (_, e) => Emit(e.Result, false);
        recognizer.Recognized += (_, e) => Emit(e.Result, true);
        recognizer.Canceled += (_, e) => { if (e.Reason == CancellationReason.Error) Failed?.Invoke(AzureEvents.Error(e.ErrorCode)); else Stopped?.Invoke(); };
        recognizer.SessionStopped += (_, _) => Stopped?.Invoke();
        } catch { resources.Dispose(); throw; }
    }
    void Emit(SpeechRecognitionResult r, bool final) {
        var value = AzureEvents.Result(r.Reason, r.Text, final, r.OffsetInTicks, r.Duration, config.SpeechRecognitionLanguage);
        if (value != null) Result?.Invoke(value);
    }
    public Task StartAsync(CancellationToken token) => recognizer.StartContinuousRecognitionAsync().WaitAsync(token);
    public Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) { token.ThrowIfCancellationRequested(); push.Write(pcm.ToArray()); return Task.CompletedTask; }
    public Task CompleteAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); push.Close(); return Task.CompletedTask; }
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; try { await recognizer.StopContinuousRecognitionAsync().WaitAsync(TimeSpan.FromSeconds(2)); } finally { resources.Dispose(); } }
}
// Production callbacks and fixtures share the same SDK-field conversion.
internal static class AzureEvents {
    internal static SpeechEvent? Result(ResultReason reason, string text, bool final, long offset, TimeSpan duration, string language)
        => reason is ResultReason.RecognizingSpeech or ResultReason.RecognizedSpeech
            ? new(text, final, TimeSpan.FromTicks((long)offset), TimeSpan.FromTicks((long)offset) + duration, language) : null;
    internal static ProviderErrorKind Error(CancellationErrorCode code) => code switch {
        CancellationErrorCode.AuthenticationFailure or CancellationErrorCode.Forbidden => ProviderErrorKind.Authentication,
        CancellationErrorCode.TooManyRequests => ProviderErrorKind.RateLimited,
        CancellationErrorCode.BadRequest => ProviderErrorKind.Configuration,
        CancellationErrorCode.ServiceTimeout => ProviderErrorKind.Timeout,
        CancellationErrorCode.ConnectionFailure or CancellationErrorCode.ServiceUnavailable => ProviderErrorKind.Network,
        CancellationErrorCode.EmbeddedModelError => ProviderErrorKind.ModelUnavailable,
        _ => ProviderErrorKind.Service
    };
}


