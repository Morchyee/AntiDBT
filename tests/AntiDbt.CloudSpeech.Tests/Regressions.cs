using AntiDbt.CloudSpeech;
using AntiDbt.Core;
using Google.Cloud.Speech.V2;
using Google.Protobuf.WellKnownTypes;
using System.Runtime.CompilerServices;
internal static class Regressions {
 public static async Task<int> Run(AsrConfiguration config) {
  int passed = 0, failures = 0;
  async Task Case(string name, Func<Task> test) { try { await test(); Console.WriteLine("PASS " + name); passed++; } catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.GetType().Name); failures++; } }
  void Require(bool condition) { if (!condition) throw new Exception("assertion"); }
  StreamingRecognitionResult Portion(string text, bool final, int end) => new() { IsFinal = final, ResultEndOffset = Duration.FromTimeSpan(TimeSpan.FromSeconds(end)), LanguageCode = "en-US", Alternatives = { new SpeechRecognitionAlternative { Transcript = text } } };
  async Task<List<AsrResult>> Receive(params StreamingRecognizeResponse[] responses) {
   await using var session = new GoogleSpeechProvider(c => new GoogleTransport(c, (_, _) => Task.CompletedTask, Responses(responses))).CreateSession(config);
   await session.ConnectAsync(default);
   var list = new List<AsrResult>(); using var timeout = new CancellationTokenSource(2000);
   await foreach (var value in session.GetResultsAsync(timeout.Token)) list.Add(value);
   return list;
  }
  await Case("Google receive multiple interim portions", async () => {
   var list = await Receive(new StreamingRecognizeResponse() { Results = { Portion("to be", false, 1), Portion(" or not to be", false, 2) } }, new StreamingRecognizeResponse() { Results = { Portion("to be or not to be", true, 2) } });
   Require(list.Count == 2 && list[0].Text == "to be or not to be" && list[1].IsFinal && list[0].SegmentId == list[1].SegmentId);
  });
  await Case("Google receive final then interim portions and empty alternative", async () => {
   var list = await Receive(new StreamingRecognizeResponse() { Results = { new StreamingRecognitionResult(), Portion("settled", true, 1), Portion("next", false, 2), Portion(" part", false, 3) } });
   Require(list.Count == 2 && list[0].IsFinal && list[1].Text == "next part" && list[1].AudioStart == TimeSpan.FromSeconds(1) && list[0].SegmentId != list[1].SegmentId);
  });
  await Case("Canceled pending write terminates and prevents concurrent send", async () => {
   var fixture = new PendingFixture(); await using var session = new SpeechSession(() => fixture); await session.ConnectAsync(default);
   using var cancel = new CancellationTokenSource(); var write = session.SendAudioAsync(new byte[2], cancel.Token); await fixture.Entered.Task; var concurrent = session.SendAudioAsync(new byte[2], default); cancel.Cancel();
   try { await write; } catch (OperationCanceledException) { }
   try { await concurrent; } catch (Exception) { }
   try { await session.SendAudioAsync(new byte[2], default); } catch (Exception) { }
   Require(fixture.Writes == 1 && fixture.Disposes == 1);
  });
  await Case("Failed connection releases acquired transport immediately", async () => {
   var fixture = new PendingFixture { FailStart = true }; await using var session = new SpeechSession(() => fixture);
   try { await session.ConnectAsync(default); } catch (ProviderException) { }
   Require(fixture.Disposes == 1);
  });
  await Case("EOF drains a final arriving after close and rejects sends", async () => {
   var fixture = new PendingFixture(); await using var session = new SpeechSession(() => fixture); await session.ConnectAsync(default);
   await session.CompleteAsync(default);
   bool rejected = false; try { await session.SendAudioAsync(new byte[2], default); } catch (ProviderException) { rejected = true; } catch (OperationCanceledException) { }
   var list = new List<AsrResult>(); await foreach (var value in session.GetResultsAsync(default)) list.Add(value);
   Require(rejected && list.Count == 1 && list[0].IsFinal && list[0].Text == "late final");
  });
  foreach (var status in new[] { Grpc.Core.StatusCode.Unauthenticated, Grpc.Core.StatusCode.ResourceExhausted, Grpc.Core.StatusCode.DeadlineExceeded, Grpc.Core.StatusCode.InvalidArgument, Grpc.Core.StatusCode.Unavailable }) {
   await Case("Google receive SDK error " + status, async () => {
    await using var session = new GoogleSpeechProvider(c => new GoogleTransport(c, (_, _) => Task.CompletedTask, ErrorResponses(status))).CreateSession(config);
    await session.ConnectAsync(default);
    try { await foreach (var value in session.GetResultsAsync(default)) { } throw new Exception("missing error"); }
    catch (ProviderException e) { Require(!e.ToString().Contains("SECRET") && e.Message.Contains(e.Kind.ToString())); }
   });
  }
    await Case("Azure acquired resources unwind despite disposer exception", () => {
   int released = 0; var resources = new NativeResources(); resources.Acquire(() => new Disposable(() => released++)); resources.Acquire(() => new Disposable(() => throw new Exception("native dispose")));
   try { resources.Dispose(); } catch { }
   Require(released == 1); return Task.CompletedTask;
  });
    await Case("Azure SDK fields and cancellation categories", () => {
   var partial = AzureEvents.Result(Microsoft.CognitiveServices.Speech.ResultReason.RecognizingSpeech, "draft", false, 100, TimeSpan.FromTicks(200), "en-US");
   var final = AzureEvents.Result(Microsoft.CognitiveServices.Speech.ResultReason.RecognizedSpeech, "final", true, 100, TimeSpan.FromTicks(300), "en-US");
   Require(partial is { Final: false } && partial.Start.Ticks == 100 && final is { Final: true } && final.End.Ticks == 400);
   Require(AzureEvents.Result(Microsoft.CognitiveServices.Speech.ResultReason.NoMatch, "", true, 0, TimeSpan.Zero, "en-US") == null);
   Require(AzureEvents.Error(Microsoft.CognitiveServices.Speech.CancellationErrorCode.AuthenticationFailure) == ProviderErrorKind.Authentication && AzureEvents.Error(Microsoft.CognitiveServices.Speech.CancellationErrorCode.TooManyRequests) == ProviderErrorKind.RateLimited && AzureEvents.Error(Microsoft.CognitiveServices.Speech.CancellationErrorCode.ServiceTimeout) == ProviderErrorKind.Timeout);
   return Task.CompletedTask;
  });
  await Case("Azure acquisition failure unwinds each preceding stage", () => {
   for (int failAt = 0; failAt < 4; failAt++) {
    int acquired = 0, released = 0; var resources = new NativeResources();
    try { for (int stage = 0; stage < 4; stage++) { int index = stage; resources.Acquire(() => { if (index == failAt) throw new Exception("native acquisition"); acquired++; return new Disposable(() => released++); }); } }
    catch { resources.Dispose(); }
    Require(released == acquired && acquired == failAt);
   }
   return Task.CompletedTask;
  });
  await Case("Google silent blocked receive canceled by overall stream limit", async () => {
   var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   await using var session = new GoogleSpeechProvider(c => new GoogleTransport(c, (_, _) => Task.CompletedTask, Silence(canceled)), TimeSpan.FromMilliseconds(30)).CreateSession(config);
   await session.ConnectAsync(default);
   try { await foreach (var value in session.GetResultsAsync(default)) { } } catch (ProviderException e) { Require(e.Kind == ProviderErrorKind.Timeout); }
   await canceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
  });
  Console.WriteLine($"Regressions: {passed} passed, {failures} failed"); if (failures > 0) throw new Exception("Regression failures"); return passed;
 }
 static async IAsyncEnumerable<StreamingRecognizeResponse> Silence(TaskCompletionSource canceled, [EnumeratorCancellation] CancellationToken token = default) { try { await Task.Delay(Timeout.Infinite, token); } finally { canceled.TrySetResult(); } yield break; }
 static async IAsyncEnumerable<StreamingRecognizeResponse> Responses(StreamingRecognizeResponse[] responses, [EnumeratorCancellation] CancellationToken token = default) { await Task.Yield(); foreach (var r in responses) { token.ThrowIfCancellationRequested(); yield return r; } }
 static async IAsyncEnumerable<StreamingRecognizeResponse> ErrorResponses(Grpc.Core.StatusCode status) { await Task.Yield(); if (status != Grpc.Core.StatusCode.OK) throw new Grpc.Core.RpcException(new(status, "SECRET private_key")); yield break; }
 sealed class Disposable(Action release) : IDisposable { public void Dispose() => release(); }
 sealed class PendingFixture : ISpeechTransport {
  public event Action<SpeechEvent>? Result; public event Action<ProviderErrorKind>? Failed { add { } remove { } } public event Action? Stopped;
  public int Writes, Disposes; public bool FailStart; public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task StartAsync(CancellationToken token) => FailStart ? Task.FromException(new Exception("SECRET")) : Task.CompletedTask;
  readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) { Writes++; Entered.TrySetResult(); return released.Task; }
  public async Task CompleteAsync(CancellationToken token) { await Task.Delay(10, token); Result?.Invoke(new("late final", true, TimeSpan.Zero, TimeSpan.FromSeconds(1))); Stopped?.Invoke(); }
  public ValueTask DisposeAsync() { Disposes++; released.TrySetResult(); return ValueTask.CompletedTask; }
 }
}




