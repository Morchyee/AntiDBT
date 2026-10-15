using AntiDbt.CloudSpeech;
using AntiDbt.Core;
using Google.Cloud.Speech.V2;
if(args.Contains("--native-smoke")) {
    // Construct and dispose native SDK resources without starting recognition or any cloud request.
    var config=Microsoft.CognitiveServices.Speech.SpeechConfig.FromSubscription("synthetic-not-a-key","eastus");
    config.SpeechRecognitionLanguage="en-US";
    using var format=Microsoft.CognitiveServices.Speech.Audio.AudioStreamFormat.GetWaveFormatPCM(16000,16,1);
    using var push=Microsoft.CognitiveServices.Speech.Audio.AudioInputStream.CreatePushStream(format);
    using var input=Microsoft.CognitiveServices.Speech.Audio.AudioConfig.FromStreamInput(push);
    using var recognizer=new Microsoft.CognitiveServices.Speech.SpeechRecognizer(config,input);
    push.Write(new byte[320]);push.Close();
    Console.WriteLine("PASS Azure native SDK resource construction/PCM input/disposal; recognition never started; no cloud authentication");
    return;
}
int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); passed++; }
var azure = new AsrConfiguration { ApiKey = "fixture", Language = "en-US", Options = new() { ["region"] = "eastus" } };
var google = new AsrConfiguration { Language = "en-US", Options = new() { ["project"] = "test-project", ["region"] = "global" }, Secrets = new() { ["serviceAccountJson"] = "{\"type\":\"service_account\",\"token_uri\":\"https://oauth2.googleapis.com/token\",\"private_key\":\"fixture\",\"client_email\":\"fixture@example.com\"}" } };
foreach (bool isGoogle in new[] { false, true }) {
    string name = isGoogle ? "Google" : "Azure";
    var fake = new Fixture();
    IAsrProvider provider = isGoogle ? new GoogleSpeechProvider(_ => fake) : new AzureSpeechProvider(_ => fake);
    try { await using var missing = provider.CreateSession(new()); await missing.ConnectAsync(default); throw new Exception("auth accepted"); }
    catch (ProviderException e) { Check(e.Kind == ProviderErrorKind.Configuration, name + " missing authentication"); }
    var session = provider.CreateSession(isGoogle ? google : azure);
    await session.ConnectAsync(default);
    await session.SendAudioAsync(new byte[] { 1, 2, 3, 4 }, default);
    Check(fake.Bytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }), name + " PCM preservation");
    fake.Emit(new("hello", false, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
    fake.Emit(new("hello world", true, TimeSpan.Zero, TimeSpan.FromSeconds(2)));
    fake.Emit(new("next", true, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)));
    await session.CompleteAsync(default);
    var values = new List<AsrResult>(); await foreach (var value in session.GetResultsAsync(default)) values.Add(value);
    Check(values.Count == 3 && values[0].SegmentId == values[1].SegmentId && values[1].Revision == 2 && values[2].SegmentId != values[1].SegmentId, name + " partial final identity revisions");
    Check(fake.Completed, name + " EOF drain");
    using (var cancel = new CancellationTokenSource()) {
        cancel.Cancel();
        try { await session.SendAudioAsync(new byte[2], cancel.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { Check(true, name + " canceled audio write"); }
    }
    await session.DisposeAsync(); await session.DisposeAsync();
    Check(fake.Disposes == 1, name + " idempotent disposal");
    try { await session.SendAudioAsync(new byte[2], default); throw new Exception("disposed write accepted"); }
    catch (ObjectDisposedException) { Check(true, name + " disposed write rejected"); }
    var failed = new Fixture();
    provider = isGoogle ? new GoogleSpeechProvider(_ => failed) : new AzureSpeechProvider(_ => failed);
    await using var error = provider.CreateSession(isGoogle ? google : azure); await error.ConnectAsync(default); failed.Error();
    try { await foreach (var value in error.GetResultsAsync(default)) { } throw new Exception("error hidden"); }
    catch (ProviderException e) { Check(!e.ToString().Contains("fixture") && e.Kind == ProviderErrorKind.Authentication, name + " sanitized error"); }
}
foreach (var json in new[] { "broken", google.Secrets["serviceAccountJson"].Replace("oauth2.googleapis.com", "evil.example"), google.Secrets["serviceAccountJson"].Replace("service_account", "authorized_user") }) {
    await using var session = new GoogleSpeechProvider(_ => throw new Exception("must not parse SDK")).CreateSession(google with { Secrets = new() { ["serviceAccountJson"] = json } });
    try { await session.ConnectAsync(default); throw new Exception("bad JSON accepted"); }
    catch (ProviderException e) { Check(e.Kind == ProviderErrorKind.Configuration, "Google JSON rejected before SDK"); }
}
var requests = new List<StreamingRecognizeRequest>();
await using (var transport = new GoogleTransport(google, (r, _) => { requests.Add(r); return Task.CompletedTask; })) {
    await transport.StartAsync(default); await transport.WriteAsync(new byte[32002], default);
    Check(requests[0].Audio.Length == 0 && requests[0].StreamingConfig.Config.ExplicitDecodingConfig.SampleRateHertz == 16000 && requests[0].StreamingConfig.StreamingFeatures.InterimResults, "Google config first PCM/interim");
    Check(requests.Skip(1).All(r => r.StreamingConfig == null && r.Audio.Length <= 15000) && requests.Skip(1).Sum(r => r.Audio.Length) == 32002, "Google 15000 byte chunks");
}
await using (var session = new GoogleSpeechProvider(_ => new Fixture(), TimeSpan.FromMilliseconds(20)).CreateSession(google)) {
    await session.ConnectAsync(default);
    try { await foreach (var value in session.GetResultsAsync(default)) { } throw new Exception("limit hidden"); }
    catch (ProviderException e) { Check(e.Kind == ProviderErrorKind.Timeout, "Google shortened real timer stream limit"); }
}
passed += await Regressions.Run(google);
Console.WriteLine($"{passed}/{passed} passed (offline gateway fixtures; no cloud or real gRPC server)");
sealed class Fixture : ISpeechTransport {
 public event Action<SpeechEvent>? Result;
 public event Action<ProviderErrorKind>? Failed;
 public event Action? Stopped;
 public byte[] Bytes = []; public bool Completed; public int Disposes;
 public void Emit(SpeechEvent result) => Result?.Invoke(result);
 public void Error() => Failed?.Invoke(ProviderErrorKind.Authentication);
 public Task StartAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
 public Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) { token.ThrowIfCancellationRequested(); Bytes = pcm.ToArray(); return Task.CompletedTask; }
 public Task CompleteAsync(CancellationToken token) { Completed = true; Stopped?.Invoke(); return Task.CompletedTask; }
 public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
}


