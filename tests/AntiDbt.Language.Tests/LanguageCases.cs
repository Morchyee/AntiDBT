using System.Net;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using AntiDbt.Core;
using AntiDbt.Providers;

static class LanguageCases
{
    static void Assert(bool value,string reason){if(!value)throw new Exception(reason);}
    public static async Task RunAsync(Func<string,Func<Task>,Task> check)
    {
        var pairs=new[]{("en","zh-Hans"),("zh-Hans","en"),("ja","zh-Hans"),("zh-Hans","ja"),("de","en"),("fr","zh-Hans"),("en","es")};
        foreach(var provider in new[]{"DeepL","AzureTranslator","GoogleTranslationBasic","GroqLlm","OpenAiCompatible","VolcengineMachineTranslation"})
        foreach(var (source,target) in pairs)
        await check($"real handler parameters {provider} {source}->{target}",async()=>
        {
            var handler=new CaptureHandler(provider);using var client=new HttpClient(handler);
            var config=ProviderDescriptors.DefaultTranslation(provider) with{Endpoint="http://127.0.0.1:1/translate",ApiKey="synthetic-only",Model="synthetic-model"};
            ITranslationProvider mt=provider=="VolcengineMachineTranslation"?new VolcengineMachineTranslationProvider(config,client):new HttpTranslationProvider(config,client);
            await mt.TranslateAsync(new(1,"s",1,"public synthetic sample",source,target,[]),CancellationToken.None);
            var expectedSource=source=="zh-Hans"?provider=="DeepL"?"ZH":provider=="GoogleTranslationBasic"?"zh-CN":provider=="AzureTranslator"?"zh-Hans":provider=="VolcengineMachineTranslation"?"zh":"zh-Hans":provider=="DeepL"?source.ToUpperInvariant():source;
            var expectedTarget=target=="zh-Hans"?provider=="DeepL"?"ZH-HANS":provider=="GoogleTranslationBasic"?"zh-CN":provider=="AzureTranslator"?"zh-Hans":provider=="VolcengineMachineTranslation"?"zh":"zh-Hans":provider=="DeepL"?target=="en"?"EN-US":target.ToUpperInvariant():target;
            using var body=JsonDocument.Parse(handler.Body);
            if(provider=="AzureTranslator"){Assert(handler.Uri!.Query.Contains("from="+Uri.EscapeDataString(expectedSource))&&handler.Uri.Query.Contains("to="+Uri.EscapeDataString(expectedTarget)),"Azure URI languages");}
            else if(provider is "OpenAiCompatible" or "GroqLlm"){var prompt=body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;Assert(prompt.Contains("("+source+")")&&prompt.Contains("("+target+")"),"LLM source/target prompt");Assert(handler.Uri!.AbsolutePath.EndsWith("/chat/completions"),"transcription must not be used for MT");}
            else {var sourceField=provider=="DeepL"?"source_lang":provider=="VolcengineMachineTranslation"?"source_language":"source";var targetField=provider=="DeepL"?"target_lang":provider=="VolcengineMachineTranslation"?"target_language":"target";Assert(body.RootElement.GetProperty(sourceField).GetString()==expectedSource&&body.RootElement.GetProperty(targetField).GetString()==expectedTarget,"HTTP body language mapping");}
        });
        await check("script and region mappings remain distinct",()=>
        {
            var d=ProviderDescriptors.DefaultTranslation("DeepL");Assert(ProviderLanguages.MtTargetCode(d,"zh-Hant")=="ZH-HANT"&&ProviderLanguages.MtTargetCode(d,"en-GB")=="EN-GB"&&ProviderLanguages.MtTargetCode(d,"pt-BR")=="PT-BR","DeepL variants");
            Assert(ProviderLanguages.MtTargetCode(d with{Provider="GoogleTranslationBasic"},"zh-Hant")=="zh-TW","Google Traditional");Assert(ProviderLanguages.MtTargetCode(d with{Provider="VolcengineMachineTranslation"},"no")=="nb","Volc Norwegian");return Task.CompletedTask;
        });
        foreach(var (provider,source,code) in new[]{("GroqWhisper","zh-Hans","zh"),("GroqWhisper","auto",""),("AzureSpeech","zh-Hans","zh-CN"),("AzureSpeech","ja","ja-JP"),("GoogleCloudSpeech","en","en-US"),("VolcengineAsr2","auto","")})
        await check($"ASR adapter receives actual locale {provider} {source}",async()=>
        {
            var inner=new CaptureAsr();var config=ProviderDescriptors.DefaultAsr(provider) with{Language=source};var mapped=new LanguageAwareAsrProvider(inner);
            await using var session=mapped.CreateSession(config);Assert(inner.Configuration!.Language==code,"ASR code not forwarded");await foreach(var result in session.GetResultsAsync(CancellationToken.None))Assert(result.Language==(source=="auto"?"zh-Hans":source),"canonical result language lost");
        });
        await check("unsupported auto/model/region and account restrictions reject",()=>
        {
            Assert(ProviderLanguages.ValidateAsr(ProviderDescriptors.DefaultAsr("AzureSpeech"),"auto")!=null,"Azure auto fake support");Assert(ProviderLanguages.ValidateAsr(ProviderDescriptors.DefaultAsr("VolcengineAsr2"),"zh-Hans")!=null,"Volc unsupported forcing");Assert(ProviderLanguages.ValidateAsr(ProviderDescriptors.DefaultAsr("GoogleCloudSpeech"),"zh-Hans")!=null,"Google long Chinese");Assert(ProviderLanguages.ValidateAsr(ProviderDescriptors.DefaultAsr("GroqWhisper") with{Model="unverified"},"auto")!=null,"unknown model");Assert(ProviderLanguages.ValidateTranslation(ProviderDescriptors.DefaultTranslation("BaiduTranslate"),"en","he")!=null,"Baidu account rights");Assert(ProviderLanguages.ValidateTranslation(ProviderDescriptors.DefaultTranslation("OpenAiCompatible"),"en","zh-Hans")!=null,"unknown LLM language declared supported");return Task.CompletedTask;
        });
        await check("LLM explicit declaration and target auto rejection",()=>{var c=ProviderDescriptors.DefaultTranslation("OpenAiCompatible") with{Options=new(){["modelLanguages"]="en,zh-Hans,ja"}};Assert(ProviderLanguages.ValidateTranslation(c,"zh-Hans","en")==null&&ProviderLanguages.ValidateTranslation(c,"ja","zh-Hans")==null&&ProviderLanguages.ValidateTranslation(c,"auto","auto")!=null,"model declaration");return Task.CompletedTask;});
        await check("same language bypass does not invoke network or need MT key",async()=>{var s=new AppSettings{SourceLanguage="en",TargetLanguage="en",Asr=ProviderDescriptors.DefaultAsr("GroqWhisper"),Translation=ProviderDescriptors.DefaultTranslation("DeepL")};var mt=ProviderCatalog.CreateTranslator(s);Assert(await mt.TranslateAsync(new(1,"s",1,"original","en","en",[]),CancellationToken.None)=="original","same language");});
        await check("compat preset never forwards another service credentials or model",()=>{var c=new TranslationConfiguration{ApiKey="fixture-secret",Model="old-model",Secrets=new(){["secret"]="fixture"},Options=new(){["modelLanguages"]="en,zh"}};foreach(var p in ServiceGuides.Presets){var next=ServiceGuides.ApplyPreset(c,p.Id);Assert(next.ApiKey==""&&next.Secrets.Count==0&&next.Model==""&&next.Options["modelLanguages"]=="","preset credential/model bleed");}return Task.CompletedTask;});
        await check("fallback consent enforced before backup creation",()=>{var s=LanguageSettings.Upgrade(ProviderCatalog.DefaultSettings()) with{FallbackEnabled=true,FallbackCostConsent=false,Translation=ProviderDescriptors.DefaultTranslation("DeepL") with{ApiKey="fixture"}};try{ProviderCatalog.CreateTranslator(s);throw new Exception("backup without consent");}catch(ProviderException){}return Task.CompletedTask;});
        foreach(var kind in new[]{ProviderErrorKind.Authentication,ProviderErrorKind.Configuration,ProviderErrorKind.Quota,ProviderErrorKind.UnsupportedLanguage,ProviderErrorKind.ModelUnavailable,ProviderErrorKind.Network,ProviderErrorKind.Timeout,ProviderErrorKind.Service,ProviderErrorKind.RateLimited})
        await check("bounded fallback error routing "+kind,async()=>{var primary=new StubTranslation(kind);var backup=new StubTranslation(null);var route=new FallbackTranslationProvider(primary,backup);try{await route.TranslateAsync(new(1,"s",1,"a","en","zh",[]),CancellationToken.None);}catch(ProviderException){}bool expected=kind is ProviderErrorKind.Network or ProviderErrorKind.Timeout or ProviderErrorKind.Service or ProviderErrorKind.RateLimited;Assert(primary.Calls==1&&backup.Calls==(expected?1:0),"unsafe backup routing");});
        await check("DeepL cache v3 parameter, stable flags and failed refresh retention",async()=>
        {
            var handler=new CaptureHandler("cache");using var client=new HttpClient(handler);var config=ProviderDescriptors.DefaultTranslation("DeepL") with{Endpoint="http://127.0.0.1:2/v2/translate",ApiKey="fixture"};
            var status=await DeepLLanguageCache.RefreshAsync(config,CancellationToken.None,testClient:client);Assert(handler.Uri!.PathAndQuery=="/v3/languages?resource=translate_text"&&status.Version=="v3","v3 resource query");await DeepLLanguageCache.RefreshAsync(config,CancellationToken.None,testClient:client);Assert(handler.Calls==1,"cache not reused");Assert(ProviderLanguages.ValidateTranslation(config,"en","zh-Hans")==null&&ProviderLanguages.ValidateTranslation(config,"en","ja")!=null,"direction/stable flags ignored");handler.Fail=true;try{await DeepLLanguageCache.RefreshAsync(config,CancellationToken.None,true,client);}catch(ProviderException){}Assert(DeepLLanguageCache.Status(config)==status,"failure destroyed good cache");
        });
    }
    sealed class CaptureHandler(string provider):HttpMessageHandler
    {
        public string Body="";public Uri? Uri;public int Calls;public bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)
        {
            Calls++;Uri=r.RequestUri;Body=r.Content==null?"":await r.Content.ReadAsStringAsync(token);
            var json=provider switch{"DeepL"=>"{\"translations\":[{\"text\":\"translated\"}]}","AzureTranslator"=>"[{\"translations\":[{\"text\":\"translated\"}]}]","GoogleTranslationBasic"=>"{\"data\":{\"translations\":[{\"translatedText\":\"translated\"}]}}","VolcengineMachineTranslation"=>"{\"code\":20000000,\"data\":{\"translation_list\":[{\"translation\":\"translated\"}]}}","cache"=>"[{\"lang\":\"en\",\"usable_as_source\":true,\"usable_as_target\":false,\"status\":\"stable\"},{\"lang\":\"zh-Hans\",\"usable_as_source\":false,\"usable_as_target\":true,\"status\":\"stable\"},{\"lang\":\"ja\",\"usable_as_source\":true,\"usable_as_target\":true,\"status\":\"beta\"}]",_=>"{\"choices\":[{\"message\":{\"content\":\"translated\"}}]}"};
            return new(Fail?HttpStatusCode.Unauthorized:HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
        }
    }
    sealed class CaptureAsr:IAsrProvider
    {
        public AsrConfiguration? Configuration;public IAsrSession CreateSession(AsrConfiguration c){Configuration=c;return new Session();}
        sealed class Session:IAsrSession
        {
            public Task ConnectAsync(CancellationToken t)=>Task.CompletedTask;public Task CompleteAsync(CancellationToken t)=>Task.CompletedTask;public Task SendAudioAsync(ReadOnlyMemory<byte> p,CancellationToken t)=>Task.CompletedTask;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
            public async IAsyncEnumerable<AsrResult> GetResultsAsync([EnumeratorCancellation]CancellationToken t){await Task.Yield();yield return new("s",1,"sample",true,TimeSpan.Zero,TimeSpan.FromSeconds(1),"zh");}
        }
    }
    sealed class StubTranslation(ProviderErrorKind? kind):ITranslationProvider
    {public int Calls;public Task<string> TranslateAsync(TranslationRequest r,CancellationToken t){Calls++;if(kind is { } k)throw new ProviderException(k,"synthetic");return Task.FromResult("backup");}}
}
