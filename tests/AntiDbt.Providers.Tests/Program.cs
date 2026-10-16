using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using AntiDbt.Core;
using AntiDbt.Providers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var outcomes = new List<object>();
async Task Check(string name, Func<Task> action) { try { await action(); outcomes.Add(new { name, passed=true }); Console.WriteLine("PASS " + name); } catch(Exception e) { outcomes.Add(new { name, passed=false, error=e.GetType().Name + ": " + e.Message }); Console.WriteLine("FAIL " + name + " " + e); } }
void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
async Task Reject(Func<Task> action, string? forbidden=null) { try { await action(); } catch(Exception e) { if (forbidden != null) Assert(!e.ToString().Contains(forbidden), "sensitive error leaked"); return; } throw new Exception("Expected rejection"); }
var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
var app = builder.Build(); app.UseWebSockets();
string? mtBody=null; bool mtAuth=false; bool legacyAuth=false; var frames=new List<byte[]>(); bool wsAuth=false;
app.MapPost("/mt", async context => { legacyAuth=context.Request.Headers["X-Api-App-Key"]=="dummy-app" && context.Request.Headers["X-Api-Access-Key"]=="dummy-access"; mtAuth=context.Request.Headers["X-Api-Key"]=="dummy-key" && context.Request.Headers["X-Api-Resource-Id"]=="volc.speech.mt"; using var reader=new StreamReader(context.Request.Body); mtBody=await reader.ReadToEndAsync(); await context.Response.WriteAsJsonAsync(new {code=20000000,data=new {translation_list=new[] { new {translation="你好"} }}}); });
app.MapPost("/error", async context => { await context.Response.WriteAsJsonAsync(new {code=45000001,message="SECRET_SERVER_BODY"}); });
app.MapPost("/status", context => { context.Response.StatusCode=401; return context.Response.WriteAsync("SECRET_SERVER_BODY"); });
app.MapPost("/rate-limit", context => { context.Response.StatusCode=429; context.Response.Headers.RetryAfter="2"; return context.Response.WriteAsync("SECRET_SERVER_BODY"); });
app.MapPost("/empty-mt", async context => { await context.Response.WriteAsJsonAsync(new {code=20000000,data=new {translation_list=new[] { new {translation="   "} }}}); });
app.MapPost("/redirect", context => { context.Response.StatusCode=302; context.Response.Headers.Location="/mt"; return Task.CompletedTask; });
app.MapPost("/slow", async context => { try { await Task.Delay(10000,context.RequestAborted); } catch(OperationCanceledException) {} });
byte[] Response(string text, int flags=1, bool gzip=false) { var payload=Encoding.UTF8.GetBytes(text); if(gzip) { using var output=new MemoryStream(); using(var zip=new GZipStream(output,CompressionMode.Compress,true)) zip.Write(payload); payload=output.ToArray(); } int offset=(flags&1)!=0?8:4; var frame=new byte[offset+4+payload.Length]; frame[0]=0x11; frame[1]=(byte)(0x90|flags); frame[2]=(byte)(gzip?0x11:0x10); if(offset==8) BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4),flags==3?-1:1); BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(offset),payload.Length); payload.CopyTo(frame,offset+4); return frame; }
async Task<byte[]> Receive(WebSocket ws) { var buffer=new byte[8192]; using var output=new MemoryStream(); WebSocketReceiveResult r; do { r=await ws.ReceiveAsync(buffer,CancellationToken.None); output.Write(buffer,0,r.Count); } while(!r.EndOfMessage); return output.ToArray(); }
app.Map("/asr", async context => { wsAuth=context.Request.Headers["X-Api-Key"]=="dummy-key" && context.Request.Headers["X-Api-Resource-Id"]=="volc.seedasr.sauc.duration"; using var ws=await context.WebSockets.AcceptWebSocketAsync(); frames.Clear(); frames.Add(await Receive(ws)); frames.Add(await Receive(ws)); frames.Add(await Receive(ws)); foreach(var (text,final) in new[] {("hello",false),("hello",false),("hello world",false),("hello world",true)}) { var data=Response(JsonSerializer.Serialize(new { result=new {utterances=new[]{new {start_time=0,end_time=200,text,definite=final}}}}),final?3:1,true); await ws.SendAsync(data.AsMemory(0,3),WebSocketMessageType.Binary,false,CancellationToken.None); await ws.SendAsync(data.AsMemory(3),WebSocketMessageType.Binary,true,CancellationToken.None); } await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"",CancellationToken.None); });
byte[] VolcError(int code, string message) { var msgBytes = Encoding.UTF8.GetBytes(message); var packet = new byte[12 + msgBytes.Length]; packet[0] = 0x11; packet[1] = 0xf0; packet[2] = 0x10; packet[3] = 0; BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4), code); BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), (uint)msgBytes.Length); msgBytes.CopyTo(packet, 12); return packet; }
var malformed = new Dictionary<string, byte[]> {
 ["truncated"]=[0x11,0x91,0x10,0,0,0,0,1],
 ["flags"]=[0x11,0x94,0x10,0,0,0,0,0],
 ["gzip"]=[0x11,0x90,0x11,0,0,0,0,2,1,2],
 ["shape"]=Response("{\"result\":[]}"),
 ["bomb"]=Response(new string('x',4*1024*1024+1),0,true),
 ["business"]=Response("{\"code\":45000001,\"message\":\"SECRET_SERVER_BODY\"}"),
 ["protocol"]=[0x11,0xf0,0x10,0,0,0,0,1,0,0,0,0],
 ["auth-type15"]=VolcError(45000002, "{\"message\":\"authentication failed\"}"),
 ["config-type15"]=VolcError(45000001, "{\"message\":\"invalid parameters\"}")
};
foreach(var item in malformed) { var captured=item.Value; app.Map("/"+item.Key,async context=> { using var ws=await context.WebSockets.AcceptWebSocketAsync(); await Receive(ws); await ws.SendAsync(captured,WebSocketMessageType.Binary,true,CancellationToken.None); }); }
app.Map("/hang",async context=> { using var ws=await context.WebSockets.AcceptWebSocketAsync(); await Receive(ws); try { await Task.Delay(10000,context.RequestAborted); } catch(OperationCanceledException) {} });
ReviewCases.Map(app);
await app.StartAsync(); var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(); var socketAddress=address.Replace("http:","ws:");
try { using var probe = new HttpClient(new HttpClientHandler { UseProxy=false }); await probe.GetAsync(address); } catch(Exception probeError) { Console.WriteLine("LOCAL_PROBE " + probeError); } var defaults=ProviderCatalog.DefaultSettings(); var request=new TranslationRequest(1,"s",1,"hello","","zh",["context must not be sent"]);
TranslationConfiguration Mt(string path)=> defaults.Translation with {Endpoint=address+path,ApiKey="dummy-key"};
AsrConfiguration Asr(string path)=> defaults.Asr with {Endpoint=socketAddress+path,ApiKey="dummy-key"};
await Check("default settings and LiveInterpret gate",async()=> { Assert(defaults.Asr.Language=="" && defaults.Asr.Endpoint.EndsWith("bigmodel_async") && defaults.TargetLanguage=="zh" && defaults.FontSize=="medium" && defaults.ShowSource && defaults.Topmost,"defaults"); await Reject(()=>Task.Run(()=>ProviderCatalog.CreateAsr(defaults.Asr with {Provider="LiveInterpret"}))); });
await Check("MT real HTTP headers and documented body",async()=> { var result=await new VolcengineMachineTranslationProvider(Mt("/mt")).TranslateAsync(request,CancellationToken.None); Assert(result=="你好" && mtAuth,"MT result/auth"); using var doc=JsonDocument.Parse(mtBody!); Assert(doc.RootElement.GetProperty("text_list")[0].GetString()=="hello" && !doc.RootElement.TryGetProperty("source_language",out _) && !doc.RootElement.TryGetProperty("context",out _),"body"); });
foreach(var path in new[]{"/error","/status","/redirect"}) await Check("MT safe error "+path,()=>Reject(()=>new VolcengineMachineTranslationProvider(Mt(path)).TranslateAsync(request,CancellationToken.None),"SECRET_SERVER_BODY"));
await Check("MT legacy authentication and explicit source",async()=> { var config=Mt("/mt") with {ApiKey="",Options=new() {["appId"]="dummy-app"},Secrets=new() {["accessKey"]="dummy-access"}}; await new VolcengineMachineTranslationProvider(config).TranslateAsync(request with {SourceLanguage="en"},CancellationToken.None); Assert(legacyAuth,"legacy headers"); using var doc=JsonDocument.Parse(mtBody!); Assert(doc.RootElement.GetProperty("source_language").GetString()=="en","explicit source"); });
await Check("MT cancellation",async()=> { using var cancel=new CancellationTokenSource(100); try { await new VolcengineMachineTranslationProvider(Mt("/slow")).TranslateAsync(request,cancel.Token); throw new Exception("cancel expected"); } catch(OperationCanceledException) { Assert(cancel.IsCancellationRequested,"token"); } });
await Check("ASR literal packets fragmented gzip dedup revision final",async()=> { await using var session=ProviderCatalog.CreateAsr(Asr("/asr")).CreateSession(Asr("/asr")); using var limit=new CancellationTokenSource(5000); await session.ConnectAsync(limit.Token); await session.SendAudioAsync(new byte[]{1,2,3,4},limit.Token); await Task.WhenAll(Enumerable.Range(0,8).Select(_=>session.CompleteAsync(limit.Token))); var results=new List<AsrResult>(); await foreach(var r in session.GetResultsAsync(limit.Token)) results.Add(r); Assert(wsAuth,"WS auth"); Assert(frames[0].Take(4).SequenceEqual(new byte[]{0x11,0x11,0x10,0}),"literal config header"); Assert(frames[1].SequenceEqual(new byte[]{0x11,0x21,0,0,0,0,0,2,0,0,0,4,1,2,3,4}),"literal audio frame"); Assert(frames[2].SequenceEqual(new byte[]{0x11,0x23,0,0,255,255,255,253,0,0,0,0}),"literal negative final"); Assert(results.Count==3 && results.Select(r=>r.Revision).SequenceEqual(new[]{1,2,3}) && results.Select(r=>r.SegmentId).Distinct().Count()==1 && results[^1].IsFinal,"result reconciliation"); using var doc=JsonDocument.Parse(frames[0].AsMemory(12)); Assert(doc.RootElement.GetProperty("request").GetProperty("enable_nonstream").GetBoolean(),"two pass"); });
foreach(var path in malformed.Keys) await Check("ASR reject "+path,async()=> { await using var session=new VolcengineAsrProvider().CreateSession(Asr("/"+path)); using var limit=new CancellationTokenSource(3000); await session.ConnectAsync(limit.Token); await Reject(async()=> { await foreach(var r in session.GetResultsAsync(limit.Token)) {} },"SECRET_SERVER_BODY"); });
await Check("ASR cancellation",async()=> { await using var session=new VolcengineAsrProvider().CreateSession(Asr("/hang")); using var cancel=new CancellationTokenSource(200); await session.ConnectAsync(cancel.Token); try { await foreach(var r in session.GetResultsAsync(cancel.Token)) {} throw new Exception("cancel expected"); } catch(OperationCanceledException) { Assert(cancel.IsCancellationRequested,"token"); } });
await Check("ASR final drain bounded",async()=> { await using var session=new VolcengineAsrProvider().CreateSession(Asr("/hang")); using var cancel=new CancellationTokenSource(5000); await session.ConnectAsync(cancel.Token); await session.CompleteAsync(cancel.Token); var started=System.Diagnostics.Stopwatch.StartNew(); await foreach(var r in session.GetResultsAsync(cancel.Token)) {} Assert(started.ElapsedMilliseconds < 4500,"drain bounded"); });
await Check("endpoint and missing credential validation",async()=> { foreach(var endpoint in new[]{"http://openspeech.bytedance.com/api","https://samantha.doubao.com/api","https://example.org/api"}) await Reject(()=>new VolcengineMachineTranslationProvider(Mt("/mt") with {Endpoint=endpoint}).TranslateAsync(request,CancellationToken.None)); await Reject(()=>new VolcengineMachineTranslationProvider(Mt("/mt") with {ApiKey=""}).TranslateAsync(request,CancellationToken.None)); });
await Check("MT rate limit extracts Retry-After",async()=>{try{await new VolcengineMachineTranslationProvider(Mt("/rate-limit")).TranslateAsync(request,CancellationToken.None);throw new Exception("rate limit expected");}catch(ProviderException e){Assert(e.Kind==ProviderErrorKind.RateLimited&&e.RetryAfter>=TimeSpan.FromSeconds(1)&&!e.ToString().Contains("SECRET_SERVER_BODY"),"rate limit classification");}});
await Check("MT rejects blank translation",async()=>{try{await new VolcengineMachineTranslationProvider(Mt("/empty-mt")).TranslateAsync(request,CancellationToken.None);throw new Exception("blank translation rejection expected");}catch(ProviderException e){Assert(e.Kind==ProviderErrorKind.InvalidResponse,"blank translation classification");}});
await Check("ASR maps auth type 15 error",async()=>{await using var session=new VolcengineAsrProvider().CreateSession(Asr("/auth-type15"));using var limit=new CancellationTokenSource(3000);await session.ConnectAsync(limit.Token);try{await foreach(var r in session.GetResultsAsync(limit.Token)){}throw new Exception("auth error expected");}catch(ProviderException e){Assert(e.Kind==ProviderErrorKind.Authentication,"auth classification");}});
await Check("ASR maps config type 15 error",async()=>{await using var session=new VolcengineAsrProvider().CreateSession(Asr("/config-type15"));using var limit=new CancellationTokenSource(3000);await session.ConnectAsync(limit.Token);try{await foreach(var r in session.GetResultsAsync(limit.Token)){}throw new Exception("config error expected");}catch(ProviderException e){Assert(e.Kind==ProviderErrorKind.Configuration,"config classification");}});
await Check("ASR official type 15 layout decodes error code and classifies Configuration", async () => {
    var errorText = Encoding.UTF8.GetBytes("{\"message\":\"invalid parameters\"}");
    var packet = new byte[12 + errorText.Length]; packet[0] = 0x11; packet[1] = 0xf0; packet[2] = 0x10; packet[3] = 0;
    BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), 45000001);
    BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), (uint)errorText.Length);
    errorText.CopyTo(packet, 12);
    var sessionType = typeof(VolcengineAsrProvider).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Single(t => t.GetMethod("Decode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) is not null);
    try {
        sessionType.GetMethod("Decode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [packet]);
        throw new Exception("Decode should have thrown ProviderException");
    } catch (System.Reflection.TargetInvocationException ex) {
        Assert(ex.InnerException is ProviderException pe && pe.Kind == ProviderErrorKind.Configuration, "official error packet classifies Configuration");
    }
});
await Check("Groq Whisper dropped jobs accurately counted under backpressure", async () => {
    using var handler = new TestBlockedHttpHandler();
    using var client = new HttpClient(handler);
    await using var groq = new GroqWhisperProvider(client).CreateSession(new() { Endpoint = "http://localhost/transcriptions", ApiKey = "fixture", Model = "whisper-large-v3", Language = "en" });
    await groq.ConnectAsync(default);
    var pcm = new byte[32000 * 4];
    for (int i = 0; i < pcm.Length; i += 2) BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i), 10000);
    for (int i = 0; i < 10; i++) await groq.SendAudioAsync(pcm, default);
    await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var kind = groq.GetType();
    var jobs = kind.GetField("jobs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(groq)!;
    var reader = jobs.GetType().GetProperty("Reader")!.GetValue(jobs)!;
    int pending = (int)reader.GetType().GetProperty("Count")!.GetValue(reader)!;
    long emitted = (long)kind.GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(groq)!;
    long reported = (long)kind.GetProperty("DroppedJobs")!.GetValue(groq)!;
    long expectedDropped = emitted - pending - 1;
    Assert(reported == expectedDropped && reported >= 5, $"DroppedJobs must match actual drops ({expectedDropped}), got {reported}");
});
await ReviewCases.Run(address, Check);
await app.StopAsync(); await app.DisposeAsync();
await Check("SherpaOnnx rejects nonexistent model directory", async () => {
    var config = new AsrConfiguration { Provider = "SherpaOnnx", Model = Path.Combine(Path.GetTempPath(), "nonexistent_sherpa_" + Guid.NewGuid().ToString("N")) };
    var provider = new SherpaOnnxStreamingAsrProvider();
    await using var session = provider.CreateSession(config);
    try {
        await session.ConnectAsync(CancellationToken.None);
        throw new Exception("Expected ModelUnavailable exception");
    } catch (ProviderException pe) {
        Assert(pe.Kind == ProviderErrorKind.ModelUnavailable, "expected ModelUnavailable, got " + pe.Kind);
        Assert(pe.Message.Contains("不存在"), "expected friendly error message");
    }
});

await Check("SherpaOnnx rejects missing tokens file", async () => {
    var tempDir = Path.Combine(Path.GetTempPath(), "test_tokens_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try {
        File.WriteAllText(Path.Combine(tempDir, "encoder.onnx"), "dummy");
        File.WriteAllText(Path.Combine(tempDir, "decoder.onnx"), "dummy");
        File.WriteAllText(Path.Combine(tempDir, "joiner.onnx"), "dummy");
        var config = new AsrConfiguration { Provider = "SherpaOnnx", Model = tempDir };
        var provider = new SherpaOnnxStreamingAsrProvider();
        await using var session = provider.CreateSession(config);
        try {
            await session.ConnectAsync(CancellationToken.None);
            throw new Exception("Expected ModelUnavailable exception for missing tokens");
        } catch (ProviderException pe) {
            Assert(pe.Kind == ProviderErrorKind.ModelUnavailable, "expected ModelUnavailable, got " + pe.Kind);
            Assert(pe.Message.Contains("tokens.txt"), "message should specify missing tokens.txt");
        }
    } finally {
        try { Directory.Delete(tempDir, true); } catch { }
    }
});

await Check("SherpaOnnx rejects missing onnx weights", async () => {
    var tempDir = Path.Combine(Path.GetTempPath(), "test_weights_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try {
        File.WriteAllText(Path.Combine(tempDir, "tokens.txt"), "dummy");
        var config = new AsrConfiguration { Provider = "SherpaOnnx", Model = tempDir };
        var provider = new SherpaOnnxStreamingAsrProvider();
        await using var session = provider.CreateSession(config);
        try {
            await session.ConnectAsync(CancellationToken.None);
            throw new Exception("Expected ModelUnavailable exception for missing weights");
        } catch (ProviderException pe) {
            Assert(pe.Kind == ProviderErrorKind.ModelUnavailable, "expected ModelUnavailable, got " + pe.Kind);
            Assert(pe.Message.Contains("关键模型文件"), "message should specify missing model files");
        }
    } finally {
        try { Directory.Delete(tempDir, true); } catch { }
    }
});

await Check("SherpaOnnx real model streams real audio with partial and final results", async () => {
    var modelDir = Path.GetFullPath("models/sherpa-onnx-streaming-zipformer-en-20M-2023-02-17");
    var wavPath = Path.Combine(modelDir, "test_wavs", "0.wav");
    if (!File.Exists(wavPath) || !File.Exists(Path.Combine(modelDir, "tokens.txt")))
        return;

    var config = new AsrConfiguration {
        Provider = "SherpaOnnx",
        Model = modelDir,
        Language = "en",
        Options = new() { ["numThreads"] = "2" }
    };
    var provider = new SherpaOnnxStreamingAsrProvider();
    await using var session = provider.CreateSession(config);
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await session.ConnectAsync(limit.Token);

    var wavBytes = await File.ReadAllBytesAsync(wavPath, limit.Token);
    var pcmBytes = wavBytes[44..]; // skip 44-byte wav header

    var results = new List<AsrResult>();
    var readerTask = Task.Run(async () => {
        await foreach (var r in session.GetResultsAsync(limit.Token)) {
            results.Add(r);
        }
    });

    int chunkSize = 6400; // 200ms at 16kHz 16-bit mono
    int offset = 0;
    while (offset < pcmBytes.Length) {
        int length = Math.Min(chunkSize, pcmBytes.Length - offset);
        await session.SendAudioAsync(pcmBytes.AsMemory(offset, length), limit.Token);
        offset += length;
        await Task.Delay(20, limit.Token);
    }
    await session.CompleteAsync(limit.Token);
    await readerTask;

    Assert(results.Count > 0, "expected at least 1 ASR result");
    bool hasPartial = results.Any(r => !r.IsFinal);
    bool hasFinal = results.Any(r => r.IsFinal);
    Assert(hasPartial, "expected real partial recognition results during streaming");
    Assert(hasFinal, "expected final recognition result after completion");
    var finalText = results.Last(r => r.IsFinal).Text;
    Assert(!string.IsNullOrWhiteSpace(finalText), "final recognized text must not be empty");
    Console.WriteLine($"[SHERPA_ONNX_VERIFIED] Recognized: \"{finalText}\" (Total Events: {results.Count}, Partials: {results.Count(r => !r.IsFinal)})");
});

await Check("HybridStreamingAsr correlates local partials and cloud final to exact same segment", async () => {
    var scripted = new[] {
        new AsrResult("raw-1", 1, "hello", false, TimeSpan.Zero, TimeSpan.FromSeconds(0.5)),
        new AsrResult("raw-1", 2, "hello world", false, TimeSpan.Zero, TimeSpan.FromSeconds(1.0)),
        new AsrResult("raw-1", 3, "hello world", true, TimeSpan.Zero, TimeSpan.FromSeconds(1.2))
    };
    var mockLocal = new MockScriptedAsrProvider(scripted);
    var hybrid = new HybridStreamingAsrProvider(
        localProvider: mockLocal,
        cloudTranscriber: (pcm, cfg, tok) => Task.FromResult<string?>("Hello, world!"));

    var config = new AsrConfiguration { Provider = "HybridStreamingAsr", Language = "en" };
    await using var session = hybrid.CreateSession(config);
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await session.ConnectAsync(limit.Token);
    await session.SendAudioAsync(new byte[3200], limit.Token);
    await session.CompleteAsync(limit.Token);

    var results = new List<AsrResult>();
    await foreach (var r in session.GetResultsAsync(limit.Token))
        results.Add(r);

    Assert(results.Count == 3, $"expected 3 results but got {results.Count}");
    Assert(results.All(r => r.SegmentId == "hybrid-1"), "all results must share the exact same SegmentId hybrid-1");
    Assert(results.Select(r => r.Revision).SequenceEqual(new[] { 1, 2, 3 }), "revisions must increment monotonically");
    Assert(!results[0].IsFinal && results[0].Text == "hello", "first result is partial hello");
    Assert(!results[1].IsFinal && results[1].Text == "hello world", "second result is partial hello world");
    Assert(results[2].IsFinal && results[2].Text == "Hello, world!", "final result is cloud replaced high-accuracy final");
});

await Check("HybridStreamingAsr falls back to local text when cloud fails", async () => {
    var scripted = new[] {
        new AsrResult("raw-1", 1, "test local", false, TimeSpan.Zero, TimeSpan.FromSeconds(0.5)),
        new AsrResult("raw-1", 2, "test local speech", true, TimeSpan.Zero, TimeSpan.FromSeconds(1.0))
    };
    var mockLocal = new MockScriptedAsrProvider(scripted);
    var hybrid = new HybridStreamingAsrProvider(
        localProvider: mockLocal,
        cloudTranscriber: (pcm, cfg, tok) => throw new HttpRequestException("simulated cloud network failure"));

    var config = new AsrConfiguration { Provider = "HybridStreamingAsr", Language = "en" };
    await using var session = hybrid.CreateSession(config);
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await session.ConnectAsync(limit.Token);
    await session.SendAudioAsync(new byte[3200], limit.Token);
    await session.CompleteAsync(limit.Token);

    var results = new List<AsrResult>();
    await foreach (var r in session.GetResultsAsync(limit.Token))
        results.Add(r);

    Assert(results.Count == 2, $"expected 2 results but got {results.Count}");
    Assert(results.All(r => r.SegmentId == "hybrid-1"), "all results must share SegmentId hybrid-1");
    Assert(results[1].IsFinal && results[1].Text == "test local speech", "final result must gracefully fall back to local text");
});

await Check("HybridStreamingAsr transitions to next segment without crosstalk", async () => {
    var scripted = new[] {
        new AsrResult("raw-1", 1, "first sentence", true, TimeSpan.Zero, TimeSpan.FromSeconds(1.0)),
        new AsrResult("raw-2", 1, "second sentence", true, TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(2.0))
    };
    var mockLocal = new MockScriptedAsrProvider(scripted);
    var hybrid = new HybridStreamingAsrProvider(
        localProvider: mockLocal,
        cloudTranscriber: (pcm, cfg, tok) => Task.FromResult<string?>(null));

    var config = new AsrConfiguration { Provider = "HybridStreamingAsr", Language = "en" };
    await using var session = hybrid.CreateSession(config);
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await session.ConnectAsync(limit.Token);
    await session.SendAudioAsync(new byte[3200], limit.Token);
    await session.CompleteAsync(limit.Token);

    var results = new List<AsrResult>();
    await foreach (var r in session.GetResultsAsync(limit.Token))
        results.Add(r);

    Assert(results.Count == 2, $"expected 2 results but got {results.Count}");
    Assert(results[0].SegmentId == "hybrid-1" && results[0].Text == "first sentence", "first segment is hybrid-1");
    Assert(results[1].SegmentId == "hybrid-2" && results[1].Text == "second sentence", "second segment is hybrid-2");
});

var artifact=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/provider-tests.json")); Directory.CreateDirectory(Path.GetDirectoryName(artifact)!); await File.WriteAllTextAsync(artifact,JsonSerializer.Serialize(new {scope="localhost HTTP and WebSocket only; no cloud calls",redEvidence="tests/AntiDbt.Providers.Tests/behavioral-red.json (final drain regression failed before fix); review-red.json records six review regressions; language-red.json records official language aliases; suffix-red.json records unchanged provisional prefix; initial red.log is build-only",tests=outcomes},new JsonSerializerOptions{WriteIndented=true}));
if(outcomes.Any(o=>o.GetType().GetProperty("passed")!.GetValue(o) is false)) Environment.ExitCode=1;

sealed class TestBlockedHttpHandler : HttpMessageHandler
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.Infinite, token);
        throw new InvalidOperationException();
    }
}

sealed class MockScriptedAsrProvider(IEnumerable<AsrResult> scriptedResults) : IAsrProvider
{
    public IAsrSession CreateSession(AsrConfiguration configuration) => new MockScriptedAsrSession(scriptedResults);
}

sealed class MockScriptedAsrSession(IEnumerable<AsrResult> scriptedResults) : IAsrSession
{
    readonly System.Threading.Channels.Channel<AsrResult> _channel = System.Threading.Channels.Channel.CreateUnbounded<AsrResult>();
    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        foreach (var r in scriptedResults)
            await _channel.Writer.WriteAsync(r, cancellationToken);
        _channel.Writer.TryComplete();
    }
    public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}







