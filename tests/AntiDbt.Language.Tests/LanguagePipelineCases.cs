using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AntiDbt.Core;
using AntiDbt.Providers;

static class LanguagePipelineCases
{
    public static async Task RunAsync(Func<string,Func<Task>,Task> check)
    {
        foreach(var (source,target,asrCode,mtSource,mtTarget) in new[]{("en","zh-Hans","en","EN","ZH-HANS"),("zh-Hans","en","zh","ZH","EN-US"),("ja","zh-Hans","ja","JA","ZH-HANS"),("zh-Hans","ja","zh","ZH","JA"),("de","en","de","DE","EN-US"),("fr","zh-Hans","fr","FR","ZH-HANS"),("en","es","en","EN","ES"),("auto","zh-Hans","","","ZH-HANS")})
        await check("Groq actual multipart -> caption -> DeepL "+source+"->"+target,async()=>
        {
            var text=LanguageRegistry.Find(source)!.Sample;var translated=LanguageRegistry.Find(target)!.Sample;
            var handler=new Handler(text,translated);using var client=new HttpClient(handler);
            var asr=ProviderDescriptors.DefaultAsr("GroqWhisper") with{Endpoint="http://127.0.0.1:3/audio/transcriptions",ApiKey="fixture",Language=source,Options=new(){["minSegmentMs"]="300",["silenceMs"]="200"}};
            var mt=ProviderDescriptors.DefaultTranslation("DeepL") with{Endpoint="http://127.0.0.1:3/v2/translate",ApiKey="fixture",DebounceMilliseconds=0};
            var store=new SubtitleStore();var seen=new List<CaptionSegment>();store.Changed+=()=>{lock(seen)seen.AddRange(store.Snapshot());};
            await using var pipeline=new CaptionPipeline(new Audio(),new LanguageAwareAsrProvider(new GroqWhisperProvider(client)),new ValidatedTranslationProvider(mt,new HttpTranslationProvider(mt,client)),new(){SourceLanguage=source,Asr=asr,Translation=mt,TargetLanguage=target},store);
            await pipeline.StartAsync();await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(5));var row=store.Snapshot().Single();
            Assert(row.SourceText==text&&row.TranslationText==translated&&row.IsFinal&&row.TranslationRevision==row.Revision,"original language or final alignment lost");
            Assert(handler.Language==asrCode&&handler.AsrPath=="/audio/transcriptions"&&handler.Model=="whisper-large-v3-turbo","Groq must transcribe original audio, not audio/translations");
            Assert(handler.Source==mtSource&&handler.Target==mtTarget,"DeepL request language mismatch");
            lock(seen){var original=seen.FindIndex(x=>x.SourceText==text&&x.TranslationText.Length==0);var translation=seen.FindIndex(x=>x.TranslationText==translated);Assert(original>=0&&translation>original,"original must render before translation");}
        });
    }
    static void Assert(bool value,string reason){if(!value)throw new Exception(reason);}
    sealed class Handler(string text,string translated):HttpMessageHandler
    {
        public string Language="",Model="",AsrPath="",Source="",Target="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string json;
            if(request.Content is MultipartFormDataContent form)
            {
                AsrPath=request.RequestUri!.AbsolutePath;
                foreach(var field in form){var name=field.Headers.ContentDisposition!.Name!.Trim('"');if(name=="language")Language=await field.ReadAsStringAsync(token);if(name=="model")Model=await field.ReadAsStringAsync(token);}
                json=JsonSerializer.Serialize(new{text});
            }
            else
            {
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Source=body.RootElement.TryGetProperty("source_lang",out var source)?source.GetString()!:"";Target=body.RootElement.GetProperty("target_lang").GetString()!;
                Assert(body.RootElement.GetProperty("text")[0].GetString()==text,"ASR original changed before MT");json=JsonSerializer.Serialize(new{translations=new[]{new{text=translated}}});
            }
            return new(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
        }
    }
    sealed class Audio:IAudioSource
    {
        public string Name=>"synthetic language pipeline";public Task StartAsync(CancellationToken token)=>Task.CompletedTask;public Task StopAsync(CancellationToken token)=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
        public async IAsyncEnumerable<AudioChunk> ReadChunksAsync([EnumeratorCancellation]CancellationToken token){for(int i=0;i<6;i++){var pcm=new byte[3200];if(i<3)for(int j=1;j<pcm.Length;j+=2)pcm[j]=16;yield return new(pcm,TimeSpan.FromMilliseconds(i*100),TimeSpan.FromMilliseconds(100),Stopwatch.GetTimestamp());await Task.Delay(20,token);}}
    }
}
