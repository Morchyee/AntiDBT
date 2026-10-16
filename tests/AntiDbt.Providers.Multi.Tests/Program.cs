using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AntiDbt.Core;
using AntiDbt.Providers;

if (args.Contains("--localhost")) return await LocalProtocol.RunAsync();
var checks=new List<object>();int failed=0;
void Assert(bool value,string why){if(!value)throw new Exception(why);}
async Task Check(string name,Func<Task> body){try{await body();checks.Add(new{name,pass=true});}catch(Exception e){failed++;checks.Add(new{name,pass=false,error=e.GetType().Name+": "+e.Message});}}
async Task Error(ProviderErrorKind kind,Func<Task> body){try{await body();throw new Exception("expected error");}catch(ProviderException e){Assert(e.Kind==kind,"wrong error category");Assert(!e.ToString().Contains("private-dummy"),"credential leaked");}}
TranslationConfiguration Config(string id)=>ProviderDescriptors.DefaultTranslation(id) with{Endpoint="http://127.0.0.1/mock",ApiKey="private-dummy",Model="local-test-text-model"};
TranslationRequest Text(string value="Hello")=>new(1,"a",1,value,"en-US","zh",["Earlier context"]);
string Success(string id)=>id switch{"DeepL"=>"{\"translations\":[{\"text\":\"你好\"}]}","AzureTranslator"=>"[{\"translations\":[{\"text\":\"你好\"}]}]","GoogleTranslationBasic"=>"{\"data\":{\"translations\":[{\"translatedText\":\"你好 &amp; 再见\"}]}}",_=>"{\"choices\":[{\"message\":{\"content\":\"你好\"}}]}"};
foreach(var id in new[]{"DeepL","AzureTranslator","GoogleTranslationBasic","GroqLlm","OpenAiCompatible"})
{
    await Check(id+" protocol and translation",async()=>{
        var handler=new Handler(async(r,n,ct)=>{
            var raw=await r.Content!.ReadAsStringAsync(ct);using var doc=JsonDocument.Parse(raw);
            if(id=="DeepL"){Assert(r.Headers.Authorization?.Scheme=="DeepL-Auth-Key","DeepL auth");Assert(doc.RootElement.GetProperty("source_lang").GetString()=="EN","source map");Assert(doc.RootElement.GetProperty("context").GetString()=="Earlier context","context");}
            else if(id=="AzureTranslator"){Assert(r.Headers.Contains("Ocp-Apim-Subscription-Key")&&r.RequestUri!.Query.Contains("api-version=3.0")&&r.RequestUri.Query.Contains("to=zh-Hans")&&r.RequestUri.Query.Contains("from=en"),"Azure headers/query");Assert(doc.RootElement[0].GetProperty("Text").GetString()=="Hello","Azure text");}
            else if(id=="GoogleTranslationBasic"){Assert(r.RequestUri!.Query.Contains("key="),"Basic API key");Assert(doc.RootElement.GetProperty("format").GetString()=="text","plain text");}
            else{Assert(r.Headers.Authorization?.Scheme=="Bearer"&&r.RequestUri!.AbsolutePath.EndsWith("chat/completions"),"LLM auth/endpoint");Assert(doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("Return only"),"translation prompt");Assert(doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()=="Hello","prompt/data separate");}
            return Reply(200,Success(id));});
        using var client=new HttpClient(handler);var text=await new HttpTranslationProvider(Config(id),client).TranslateAsync(Text(),default);Assert(text.StartsWith("你好")&&(id!="GoogleTranslationBasic"||text.Contains("& 再见")),"translation/entity decode");Assert(handler.Calls==1,"duplicate healthy request");
    });
    foreach(var pair in new[]{(401,ProviderErrorKind.Authentication),(403,ProviderErrorKind.Authentication),(404,ProviderErrorKind.ModelUnavailable),(456,ProviderErrorKind.Quota),(400,ProviderErrorKind.Configuration),(500,ProviderErrorKind.Service),(302,ProviderErrorKind.Service)})
        await Check(id+" HTTP "+pair.Item1,async()=>{using var client=new HttpClient(new Handler((r,n,ct)=>Task.FromResult(Reply(pair.Item1,"{\"error\":{\"message\":\"private-dummy\"}}"))));await Error(pair.Item2,()=>new HttpTranslationProvider(Config(id),client).TranslateAsync(Text(),default));});
    await Check(id+" empty response",async()=>{using var client=new HttpClient(new Handler((r,n,ct)=>Task.FromResult(Reply(200,"{}"))));await Error(ProviderErrorKind.InvalidResponse,()=>new HttpTranslationProvider(Config(id),client).TranslateAsync(Text(),default));});
    await Check(id+" cancellation",async()=>{using var client=new HttpClient(new Handler(async(r,n,ct)=>{await Task.Delay(2000,ct);return Reply(200,Success(id));}));using var stop=new CancellationTokenSource(30);try{await new HttpTranslationProvider(Config(id),client).TranslateAsync(Text(),stop.Token);throw new Exception("cancellation missed");}catch(OperationCanceledException){Assert(stop.IsCancellationRequested,"cancellation token");}});
}
await Check("429 Retry-After cooldown and bounded retry",async()=>{
    var handler=new Handler((r,n,ct)=>{var response=Reply(n==1?429:200,Success("DeepL"));if(n==1)response.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(80));return Task.FromResult(response);});using var client=new HttpClient(handler);var watch=System.Diagnostics.Stopwatch.StartNew();await new HttpTranslationProvider(Config("DeepL"),client).TranslateAsync(Text(),default);Assert(handler.Calls==2&&watch.ElapsedMilliseconds>=60,"cooldown skipped");
});
await Check("429 long cooldown cancellation sends no early retry",async()=>{var handler=new Handler((r,n,ct)=>{var result=Reply(429,"{}");result.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));return Task.FromResult(result);});using var client=new HttpClient(handler);using var stop=new CancellationTokenSource(50);try{await new HttpTranslationProvider(Config("DeepL"),client).TranslateAsync(Text(),stop.Token);}catch(OperationCanceledException){}Assert(handler.Calls==1,"early retry");});
await Check("custom URL and credentials cannot redirect or change host",async()=>{foreach(var address in new[]{"http://example.org/v1","https://user:secret@example.org/v1","https://example.org/v1?key=oops","https://example.org/v1#fragment"})await Error(ProviderErrorKind.Configuration,()=>Task.Run(()=>new HttpTranslationProvider(Config("OpenAiCompatible") with{Endpoint=address})));await Error(ProviderErrorKind.Configuration,()=>Task.Run(()=>new HttpTranslationProvider(Config("DeepL") with{Endpoint="https://example.org/v2/translate"})));});
await Check("Whisper WAV 16k mono and overlap repair",()=>{var wav=GroqWhisperProvider.Wav([1,0,2,0]);Assert(Encoding.ASCII.GetString(wav,0,4)=="RIFF"&&BitConverter.ToInt32(wav,24)==16000&&BitConverter.ToInt16(wav,22)==1&&wav.Length==48,"WAV format");Assert(GroqWhisperProvider.RemoveOverlap("hello world","world again")=="again"&&GroqWhisperProvider.RemoveOverlap("你好世界","世界再见")=="再见","overlap");return Task.CompletedTask;});
await Check("Whisper VAD silence is not submitted and emits final only",async()=>{
    var handler=new Handler(async(r,n,ct)=>{Assert(r.Headers.Authorization?.Scheme=="Bearer","ASR auth");var body=await r.Content!.ReadAsStringAsync(ct);Assert(body.Contains("RIFF")&&body.Contains("whisper-large-v3-turbo")&&!r.RequestUri!.AbsolutePath.Contains("translations"),"WAV/model/transcriptions");return Reply(200,"{\"text\":\"original language\"}");});using var client=new HttpClient(handler);var config=ProviderDescriptors.DefaultAsr("GroqWhisper") with{Endpoint="http://127.0.0.1/transcriptions",ApiKey="private-dummy"};
    await using var session=new GroqWhisperProvider(client).CreateSession(config);await session.ConnectAsync(default);await session.SendAudioAsync(new byte[32000],default);Assert(handler.Calls==0,"silence charged");var pcm=Enumerable.Range(0,32000).Select(i=>(byte)(i%2==0?0:16)).ToArray();await session.SendAudioAsync(pcm,default);await session.SendAudioAsync(new byte[32000],default);await session.CompleteAsync(default);var results=new List<AsrResult>();using var deadline=new CancellationTokenSource(2000);await foreach(var result in session.GetResultsAsync(deadline.Token))results.Add(result);Assert(handler.Calls==1&&results.Count==1&&results[0].IsFinal,"segmented/final/dedup");
});
await Check("catalog independent combinations and final-only capabilities",()=>{Assert(ProviderDescriptors.Asr.Count==6&&ProviderDescriptors.Translation.Count==8,"catalog size");Assert(!ProviderDescriptors.Find("GroqWhisper",true).Partial&&ProviderDescriptors.Find("AzureSpeech",true).Partial,"partial declaration");foreach(var asr in ProviderDescriptors.Asr)foreach(var mt in ProviderDescriptors.Translation){Assert(ProviderCatalog.CreateAsr(ProviderDescriptors.DefaultAsr(asr.Id)) is not null,"ASR factory");var config=ProviderDescriptors.DefaultTranslation(mt.Id) with{ApiKey="dummy",Model="test"};Assert(ProviderCatalog.CreateTranslator(config) is not null,"MT factory");}return Task.CompletedTask;});
await Check("review 429 quota is terminal without charged retries",async()=>{var handler=new Handler((r,n,ct)=>Task.FromResult(Reply(429,"{\"error\":{\"code\":\"insufficient_quota\"}}")));using var client=new HttpClient(handler);await Error(ProviderErrorKind.Quota,()=>new HttpTranslationProvider(Config("GroqLlm"),client).TranslateAsync(Text(),default));Assert(handler.Calls==1,"quota retried");});
await Check("review concurrent cooldown never shortens existing RetryAfter",async()=>{
    var bothStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var longResponse=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var shortResponse=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var handler=new Handler(async(r,n,ct)=>{
        if(n==2)bothStarted.TrySetResult();
        if(n<=2)await (n==1?longResponse.Task:shortResponse.Task).WaitAsync(ct);
        var response=Reply(n<=2?429:200,Success("DeepL"));
        if(n<=2)response.Headers.RetryAfter=new RetryConditionHeaderValue(n==1?TimeSpan.FromSeconds(2):TimeSpan.Zero);
        return response;
    });
    using var client=new HttpClient(handler);var provider=new HttpTranslationProvider(Config("DeepL"),client);using var stop=new CancellationTokenSource(3000);
    async Task Run(string value){try{await provider.TranslateAsync(Text(value),stop.Token);}catch(OperationCanceledException){}}
    var pending=new[]{Run("a"),Run("b")};
    await bothStarted.Task.WaitAsync(stop.Token); // Both HTTP operations exist before either response arrives.
    longResponse.SetResult();await Task.Delay(150,stop.Token);
    shortResponse.SetResult();await Task.Delay(150,stop.Token);
    int observed=handler.Calls;stop.Cancel();await Task.WhenAll(pending);
    Assert(observed==2,"expected two initial requests and no early retry; observed "+observed);
});
await Check("review overlap respects whole words and punctuation",()=>{Assert(GroqWhisperProvider.RemoveOverlap("a cat","catalog entries")=="catalog entries","partial word truncated");Assert(GroqWhisperProvider.RemoveOverlap("hello world.","world again")=="again","boundary punctuation duplicate");return Task.CompletedTask;});
await Check("review dropped audio jobs cannot dedup nonadjacent text",async()=>{
    var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var handler=new Handler(async(r,n,ct)=>{if(n==1){started.SetResult();await release.Task.WaitAsync(ct);}return Reply(200,n==1?"{\"text\":\"wordgap\"}":"{\"text\":\"wordgap later\"}");});using var client=new HttpClient(handler);var config=ProviderDescriptors.DefaultAsr("GroqWhisper") with{Endpoint="http://127.0.0.1/asr",ApiKey="private-dummy",Options=new(){["minSegmentMs"]="300",["maxSegmentMs"]="500",["overlapMs"]="100"}};
    await using var session=new GroqWhisperProvider(client).CreateSession(config);await session.ConnectAsync(default);var voice=Enumerable.Range(0,16000).Select(i=>(byte)(i%2==0?0:16)).ToArray();await session.SendAudioAsync(voice,default);await started.Task.WaitAsync(TimeSpan.FromSeconds(2));await session.SendAudioAsync(Enumerable.Repeat(voice,10).SelectMany(x=>x).ToArray(),default);await session.CompleteAsync(default);release.SetResult();var rows=new List<AsrResult>();using var deadline=new CancellationTokenSource(3000);await foreach(var row in session.GetResultsAsync(deadline.Token))rows.Add(row);Assert(rows.Count>=2&&rows[1].Text=="wordgap later","nonadjacent overlap trimmed");
});
await Check("HTTP deadline distinguishes timeout from user cancellation",async()=>{using var client=new HttpClient(new Handler(async(r,n,ct)=>{await Task.Delay(3000,ct);return Reply(200,Success("DeepL"));}));await Error(ProviderErrorKind.Timeout,()=>new HttpTranslationProvider(Config("DeepL") with{TimeoutSeconds=1},client).TranslateAsync(Text(),default));});
await Check("network failure is sanitized and not automatically resubmitted",async()=>{var handler=new Handler((r,n,ct)=>throw new HttpRequestException("private-dummy"));using var client=new HttpClient(handler);await Error(ProviderErrorKind.Network,()=>new HttpTranslationProvider(Config("DeepL"),client).TranslateAsync(Text(),default));Assert(handler.Calls==1,"network request duplicated");});
await Check("model and unsupported-language business errors",async()=>{foreach(var pair in new[]{("model_decommissioned",ProviderErrorKind.ModelUnavailable),("unsupported_language",ProviderErrorKind.UnsupportedLanguage)}){using var client=new HttpClient(new Handler((r,n,ct)=>Task.FromResult(Reply(400,JsonSerializer.Serialize(new{error=new{code=pair.Item1}})))));await Error(pair.Item2,()=>new HttpTranslationProvider(Config("GroqLlm"),client).TranslateAsync(Text(),default));}});
await Check("Groq candidate model list excludes known non-translation models",()=>{using var doc=JsonDocument.Parse("{\"data\":[{\"id\":\"whisper-large-v3\"},{\"id\":\"vendor/orpheus\"},{\"id\":\"prompt-guard\"},{\"id\":\"groq/compound\"},{\"id\":\"future-text-model\"},{\"id\":\"future-text-model\"}]}");Assert(ProviderDescriptors.TextModelCandidates(doc.RootElement).SequenceEqual(new[]{"future-text-model"}),"candidate list");return Task.CompletedTask;});

foreach(var status in new[]{401,403})foreach(var body in new[]{"[]","null","\"private-dummy\"","{\"error\":{\"status\":3}}","{\"error\":{\"status\":{}}}"})
    await Check("malformed auth "+status+" "+body,async()=>{var h=new Handler((r,n,t)=>Task.FromResult(Reply(status,body)));using var c=new HttpClient(h);await Error(ProviderErrorKind.Authentication,()=>new HttpTranslationProvider(Config("DeepL"),c).TranslateAsync(Text(),default));Assert(h.Calls==1,"auth retried");});
await Check("malformed 429 still establishes cooldown",async()=>{
    var h=new Handler((r,n,t)=>{var response=Reply(n==1?429:200,n==1?"[]":Success("DeepL"));if(n==1)response.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(250));return Task.FromResult(response);});using var c=new HttpClient(h);var watch=System.Diagnostics.Stopwatch.StartNew();await new HttpTranslationProvider(Config("DeepL"),c).TranslateAsync(Text(),default);Assert(h.Calls==2&&watch.ElapsedMilliseconds>=200,"malformed rate limit bypassed");
});
foreach(var body in new[]{"[]","null","{\"error\":{\"status\":3}}"})await Check("malformed success remains invalid "+body,async()=>{using var c=new HttpClient(new Handler((r,n,t)=>Task.FromResult(Reply(200,body))));await Error(ProviderErrorKind.InvalidResponse,()=>new HttpTranslationProvider(Config("DeepL"),c).TranslateAsync(Text(),default));});
foreach(var alias in new[]{"zh","zh-CN","zh-SG","zh-Hans","zh-TW","zh-HK","zh-Hant"})await Check("Azure Chinese mapping "+alias,async()=>{
    var expected=alias is "zh-TW" or "zh-HK" or "zh-Hant"?"zh-Hant":"zh-Hans";
    using var c=new HttpClient(new Handler(async(r,n,t)=>{Assert(r.RequestUri!.Query.Contains("to="+expected)&&r.RequestUri.Query.Contains("from="+expected),"Azure alias query");using var d=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(t));Assert(d.RootElement[0].GetProperty("Text").GetString()=="Hello","Azure body");return Reply(200,Success("AzureTranslator"));}));await new HttpTranslationProvider(Config("AzureTranslator"),c).TranslateAsync(Text() with{SourceLanguage=alias,TargetLanguage=alias},default);
});
await Check("Groq cooldown survives timeout and replacement sessions",async()=>{
    var first=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var watch=System.Diagnostics.Stopwatch.StartNew();
    var h=new Handler((r,n,t)=>{first.TrySetResult();var response=Reply(n==1?429:200,n==1?"{}":"{\"text\":\"ok\"}");if(n==1)response.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));return Task.FromResult(response);});using var c=new HttpClient(h);var p=new GroqWhisperProvider(c);
    var config=ProviderDescriptors.DefaultAsr("GroqWhisper") with{Endpoint="http://127.0.0.1/asr",ApiKey="private-dummy",Options=new(){["timeoutSeconds"]="1",["minSegmentMs"]="300",["maxSegmentMs"]="500"}};
    var voice=Enumerable.Range(0,16000).Select(i=>(byte)(i%2==0?0:16)).ToArray();
    await using(var a=p.CreateSession(config)){await a.ConnectAsync(default);await a.SendAudioAsync(voice,default);await a.CompleteAsync(default);await first.Task.WaitAsync(TimeSpan.FromSeconds(2));await Error(ProviderErrorKind.Timeout,async()=>{await foreach(var row in a.GetResultsAsync(default)){} });}
    await using(var b=p.CreateSession(config)){await b.ConnectAsync(default);await b.SendAudioAsync(voice,default);await b.CompleteAsync(default);await Error(ProviderErrorKind.Timeout,async()=>{await foreach(var row in b.GetResultsAsync(default)){} });Assert(h.Calls==1,"replacement session bypassed cooldown");}
    await using(var stopped=p.CreateSession(config)){await stopped.ConnectAsync(default);await stopped.SendAudioAsync(voice,default);await Task.Delay(100);Assert(h.Calls==1,"stopped session bypassed cooldown");var before=System.Diagnostics.Stopwatch.StartNew();await stopped.DisposeAsync();Assert(before.ElapsedMilliseconds<500&&h.Calls==1,"user stop failed during cooldown");}
    var remaining=TimeSpan.FromSeconds(3)-watch.Elapsed+TimeSpan.FromMilliseconds(100);if(remaining>TimeSpan.Zero)await Task.Delay(remaining);
    await using(var resumed=p.CreateSession(config)){await resumed.ConnectAsync(default);await resumed.SendAudioAsync(voice,default);await resumed.CompleteAsync(default);await foreach(var row in resumed.GetResultsAsync(default)){} Assert(h.Calls>=2,"cooldown never expired");}
});
Directory.CreateDirectory("artifacts/phase3");await File.WriteAllTextAsync("artifacts/phase3/http-tests.json",JsonSerializer.Serialize(new{scope="mock HTTP handlers; dummy credentials; no cloud",total=checks.Count,failed,checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{total=checks.Count,failed,checks}));return failed==0?0:1;
static HttpResponseMessage Reply(int status,string json)=>new((HttpStatusCode)status){Content=new StringContent(json,Encoding.UTF8,"application/json")};
sealed class Handler(Func<HttpRequestMessage,int,CancellationToken,Task<HttpResponseMessage>> handle):HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>handle(request,Interlocked.Increment(ref Calls),token);
}

