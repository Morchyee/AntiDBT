using System.Net.Http.Headers;
using AntiDbt.Core;

namespace AntiDbt.Providers;

public sealed record ProviderProbeResult(bool Success,string Message,string Translation="");
public static class ProviderDiagnostics
{
    public static async Task<ProviderProbeResult> TestConnectionAsync(TranslationConfiguration config,string source,string target,CancellationToken token)
    {
        try
        {
            if(ProviderDescriptors.Validate(config) is { } invalid)return new(false,invalid);
            if(config.Provider is "OpenAiCompatible" or "GroqLlm")
            {var models=await FetchModelsAsync(config,token);return new(true,$"模型目录请求成功（{models.Count} 项）；不代表所填模型的翻译调用已通过。");}
            if(config.Provider=="DeepL")
            {var cache=await DeepLLanguageCache.RefreshAsync(config,token,true);return new(true,$"语言目录请求成功（{cache.Version}）；翻译调用仍需样例验收。");}
            var result=await TestTranslationAsync(config,source,target,token);
            return result.Success?result with{Message="鉴权及公开样例调用成功；不代表所有语言或稳定性已通过。"}:result;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(ProviderException e){return new(false,e.Message);}
        catch(Exception){return new(false,"连接检查失败，请检查字段、权限或网络。");}
    }
    public static async Task<IReadOnlyList<string>> FetchModelsAsync(TranslationConfiguration config,CancellationToken token)
    {
        if(config.Provider is not ("OpenAiCompatible" or "GroqLlm"))throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        var endpoint=ProviderHttp.Endpoint(config.Endpoint);ProviderHttp.Key(config.ApiKey);
        if(endpoint.Scheme=="http"&&config.Options.GetValueOrDefault("allowLocalHttp")!="true")throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        var baseUrl=endpoint.ToString().TrimEnd('/');
        if(baseUrl.EndsWith("/chat/completions",StringComparison.OrdinalIgnoreCase))baseUrl=baseUrl[..^17];
        using var doc=await new ProviderHttp().JsonAsync(()=>{var r=new HttpRequestMessage(HttpMethod.Get,baseUrl+"/models");r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",config.ApiKey);return r;},15,token,false);
        try{return ProviderDescriptors.TextModelCandidates(doc.RootElement);}
        catch(Exception e)when(e is KeyNotFoundException or InvalidOperationException){throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);}
    }
    // These methods must only be invoked by explicit user buttons, never validation or startup.
    public static async Task<ProviderProbeResult> TestTranslationAsync(TranslationConfiguration config,string source,string target,CancellationToken token)
    {
        try
        {
            source=LanguageRegistry.Normalize(source);target=LanguageRegistry.Normalize(target);
            var language=LanguageRegistry.Find(source)??throw ProviderHttp.Failure(ProviderErrorKind.UnsupportedLanguage);
            if(LanguageRegistry.Find(target)==null||target=="auto")throw ProviderHttp.Failure(ProviderErrorKind.UnsupportedLanguage);
            if(config.Provider is "OpenAiCompatible" or "GroqLlm")
            {
                var options=new Dictionary<string,string>(config.Options){["modelLanguages"]=string.Join(',',new[]{source,target}.Where(x=>x!="auto"))};
                config=config with{Options=options};
            }
            var provider=ProviderCatalog.CreateTranslator(config);
            var translated=await provider.TranslateAsync(new(0,"explicit-public-sample",1,language.Sample,source,target,Array.Empty<string>()),token);
            return new(true,"样例请求返回译文；不代表全部语言质量或长期稳定性已验收。",translated);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(ProviderException e){return new(false,e.Message);}
        catch(Exception){return new(false,"样例请求失败，请检查配置、权限或网络；未记录服务器正文。");}
    }
    public static async Task<ProviderProbeResult> TestAsrConnectionAsync(AsrConfiguration config,string source,CancellationToken token)
    {
        try
        {
            if(ProviderDescriptors.Validate(config) is { } error)return new(false,error);
            await using var session=ProviderCatalog.CreateAsr(config).CreateSession(config with{Language=source});
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
            await session.ConnectAsync(deadline.Token);
            await session.CompleteAsync(deadline.Token);
            await foreach(var result in session.GetResultsAsync(deadline.Token)){}
            return new(true,"连接初始化及空流关闭完成；未发送语音，识别结果和完整鉴权仍需真实音频验收。");
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(ProviderException e){return new(false,e.Message);}
        catch(OperationCanceledException){return new(false,"连接检查超时；未证明认证成功。");}
        catch(Exception){return new(false,"连接检查失败，请检查服务配置和网络；未记录 SDK 诊断或凭据。");}
    }
}
