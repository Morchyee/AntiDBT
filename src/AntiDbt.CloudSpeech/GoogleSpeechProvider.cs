using AntiDbt.Core;
using Google.Cloud.Speech.V2;
using Google.Api.Gax.Grpc;
using Google.Protobuf;
using System.Text.Json;
namespace AntiDbt.CloudSpeech;
public sealed class GoogleSpeechProvider : IAsrProvider {
    readonly Func<AsrConfiguration, ISpeechTransport> factory;
    readonly TimeSpan limit;
    public GoogleSpeechProvider() : this(c => new GoogleTransport(c)) { }
    public GoogleSpeechProvider(Func<AsrConfiguration, ISpeechTransport> factory, TimeSpan? streamLimit = null) { this.factory = factory; limit = streamLimit ?? TimeSpan.FromMinutes(4.75); if (limit <= TimeSpan.Zero || limit > TimeSpan.FromMinutes(4.75)) throw new ArgumentOutOfRangeException(nameof(streamLimit)); }
    public IAsrSession CreateSession(AsrConfiguration c) => new SpeechSession(() => { Validate(c); return factory(c); }, limit);
    internal static void Validate(AsrConfiguration c) {
        if (!c.Options.TryGetValue("project", out var project) || !System.Text.RegularExpressions.Regex.IsMatch(project, "^[a-z][a-z0-9-]{4,61}[a-z0-9]$")) throw SpeechValidation.Bad();
        var region = c.Options.GetValueOrDefault("region", "global");
        if (!System.Text.RegularExpressions.Regex.IsMatch(region, "^(global|us|eu|[a-z]+-[a-z]+[0-9])$")) throw SpeechValidation.Bad();
        if (c.Endpoint.Length > 0) throw SpeechValidation.Bad();
        if (c.Model.Length > 0 && c.Model != "long") throw SpeechValidation.Bad();
        SpeechValidation.Language(c);
        try {
            if (!c.Secrets.TryGetValue("serviceAccountJson", out var json)) throw SpeechValidation.Bad();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() != "service_account" || root.GetProperty("token_uri").GetString() != "https://oauth2.googleapis.com/token") throw SpeechValidation.Bad();
            if (string.IsNullOrWhiteSpace(root.GetProperty("private_key").GetString()) || string.IsNullOrWhiteSpace(root.GetProperty("client_email").GetString())) throw SpeechValidation.Bad();
        } catch { throw SpeechValidation.Bad(); }
    }
}
internal sealed class GoogleTransport(AsrConfiguration config, Func<StreamingRecognizeRequest, CancellationToken, Task>? testWrite = null, IAsyncEnumerable<StreamingRecognizeResponse>? testResponses = null) : ISpeechTransport {
    readonly CancellationTokenSource lifetime = new();
    SpeechClient.StreamingRecognizeStream? call;
    Task? reader;
    int disposed;
    TimeSpan previousEnd;
    public event Action<SpeechEvent>? Result;
    public event Action<ProviderErrorKind>? Failed;
    public event Action? Stopped;
    public async Task StartAsync(CancellationToken token) {
        var region = config.Options.GetValueOrDefault("region", "global");
        if (testWrite != null) { await WriteConfigAsync(region, token); if (testResponses != null) reader = ReadAsync(); return; }
        var client = await new SpeechClientBuilder { Endpoint = region == "global" ? "speech.googleapis.com" : $"{region}-speech.googleapis.com", GoogleCredential = Google.Apis.Auth.OAuth2.GoogleCredential.FromServiceAccountCredential(Google.Apis.Auth.OAuth2.ServiceAccountCredential.FromServiceAccountData(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(config.Secrets["serviceAccountJson"])))) }.BuildAsync(token);
        call = client.StreamingRecognize(CallSettings.FromCancellationToken(lifetime.Token));
        await WriteConfigAsync(region, token);
        reader = ReadAsync();
    }
    async Task WriteConfigAsync(string region, CancellationToken token) {
        await WriteRequestAsync(new StreamingRecognizeRequest {
            Recognizer = $"projects/{config.Options["project"]}/locations/{region}/recognizers/_",
            StreamingConfig = new StreamingRecognitionConfig {
                Config = new RecognitionConfig {
                    ExplicitDecodingConfig = new ExplicitDecodingConfig { Encoding = ExplicitDecodingConfig.Types.AudioEncoding.Linear16, SampleRateHertz = 16000, AudioChannelCount = 1 },
                    LanguageCodes = { SpeechValidation.Language(config) }, Model = "long"
                }, StreamingFeatures = new StreamingRecognitionFeatures { InterimResults = true }
            }
        }, token);
    }
    async Task ReadAsync() {
        try {
            await foreach (var response in (testResponses ?? call!.GetResponseStream()).WithCancellation(lifetime.Token)) {
                var pending = new System.Text.StringBuilder();
                TimeSpan pendingEnd = previousEnd;
                string? pendingLanguage = null;
                foreach (var r in response.Results) {
                    if (r.Alternatives.Count == 0) continue;
                    var end = r.ResultEndOffset?.ToTimeSpan() ?? previousEnd;
                    if (r.IsFinal) {
                        Result?.Invoke(new(r.Alternatives[0].Transcript, true, previousEnd, end, r.LanguageCode));
                        previousEnd = end;
                    } else {
                        pending.Append(r.Alternatives[0].Transcript);
                        pendingEnd = end;
                        pendingLanguage = r.LanguageCode;
                    }
                }
                if (pending.Length > 0) Result?.Invoke(new(pending.ToString(), false, previousEnd, pendingEnd, pendingLanguage));
            }
            Stopped?.Invoke();
        } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Grpc.Core.RpcException e) { Failed?.Invoke(e.StatusCode switch { Grpc.Core.StatusCode.Unauthenticated or Grpc.Core.StatusCode.PermissionDenied => ProviderErrorKind.Authentication, Grpc.Core.StatusCode.ResourceExhausted => ProviderErrorKind.Quota, Grpc.Core.StatusCode.DeadlineExceeded => ProviderErrorKind.Timeout, Grpc.Core.StatusCode.InvalidArgument => ProviderErrorKind.Configuration, Grpc.Core.StatusCode.NotFound => ProviderErrorKind.ModelUnavailable, Grpc.Core.StatusCode.Unavailable => ProviderErrorKind.Network, _ => ProviderErrorKind.Service }); }
        catch (TimeoutException) { Failed?.Invoke(ProviderErrorKind.Timeout); lifetime.Cancel(); }
        catch { Failed?.Invoke(ProviderErrorKind.Service); }
    }
    public async Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) {
        for (int offset = 0; offset < pcm.Length; offset += 15000)
            await WriteRequestAsync(new StreamingRecognizeRequest { Audio = ByteString.CopyFrom(pcm.Slice(offset, Math.Min(15000, pcm.Length - offset)).Span) }, token);
    }
    async Task WriteRequestAsync(StreamingRecognizeRequest request, CancellationToken token) {
        try { await (testWrite != null ? testWrite(request, token) : call!.WriteAsync(request)).WaitAsync(token); }
        catch { Abort(); throw; }
    }
    public async Task CompleteAsync(CancellationToken token) { await call!.WriteCompleteAsync().WaitAsync(token); if (reader != null) await reader.WaitAsync(token); }
    public void Abort() { lifetime.Cancel(); }
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; Abort(); call?.Dispose(); if (reader != null) { try { await reader.WaitAsync(TimeSpan.FromSeconds(2)); } catch { } } }
}


