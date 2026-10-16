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
var malformed = new Dictionary<string, byte[]> {
 ["truncated"]=[0x11,0x91,0x10,0,0,0,0,1],
 ["flags"]=[0x11,0x94,0x10,0,0,0,0,0],
 ["gzip"]=[0x11,0x90,0x11,0,0,0,0,2,1,2],
 ["shape"]=Response("{\"result\":[]}"),
 ["bomb"]=Response(new string('x',4*1024*1024+1),0,true),
 ["business"]=Response("{\"code\":45000001,\"message\":\"SECRET_SERVER_BODY\"}"),
 ["protocol"]=[0x11,0xf0,0x10,0,0,0,0,1,0,0,0,0],
 ["auth-type15"]=[0x11,0xf0,0x10,0,0,0,0,4,0,0,0x03,0xe8]
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
await ReviewCases.Run(address, Check);
await app.StopAsync(); await app.DisposeAsync();
var artifact=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/provider-tests.json")); Directory.CreateDirectory(Path.GetDirectoryName(artifact)!); await File.WriteAllTextAsync(artifact,JsonSerializer.Serialize(new {scope="localhost HTTP and WebSocket only; no cloud calls",redEvidence="tests/AntiDbt.Providers.Tests/behavioral-red.json (final drain regression failed before fix); review-red.json records six review regressions; language-red.json records official language aliases; suffix-red.json records unchanged provisional prefix; initial red.log is build-only",tests=outcomes},new JsonSerializerOptions{WriteIndented=true}));
if(outcomes.Any(o=>o.GetType().GetProperty("passed")!.GetValue(o) is false)) Environment.ExitCode=1;






