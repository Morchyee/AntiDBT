using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using AntiDbt.Core;

namespace AntiDbt.Providers;

public sealed record LanguageCacheStatus(string Version,DateTimeOffset FetchedAtUtc,DateTimeOffset ExpiresAtUtc,int SourceCount,int TargetCount);
public static class DeepLLanguageCache
{
    sealed record Snapshot(LanguageCacheStatus Status,HashSet<string> Sources,HashSet<string> Targets,JsonElement[] Raw);
    static readonly ConcurrentDictionary<string,Snapshot> Cache=new();
    static readonly ConcurrentDictionary<string,SemaphoreSlim> Gates=new();
    static string Key(Uri endpoint)=>endpoint.GetLeftPart(UriPartial.Authority)+"/translate_text";
    public static LanguageCacheStatus? Status(TranslationConfiguration config)
    {return Uri.TryCreate(config.Endpoint,UriKind.Absolute,out var endpoint)&&Cache.TryGetValue(Key(endpoint),out var value)?value.Status:null;}
    internal static string? Validate(TranslationConfiguration config,string source,string target)
    {
        if(!Uri.TryCreate(config.Endpoint,UriKind.Absolute,out var endpoint)||!Cache.TryGetValue(Key(endpoint),out var value)||value.Status.ExpiresAtUtc<=DateTimeOffset.UtcNow)return null;
        if(source!="auto"&&!value.Sources.Contains(ProviderLanguages.MtSourceCode(config,source))||!value.Targets.Contains(ProviderLanguages.MtTargetCode(config,target)))return "DeepL 最近获取的官方语言目录不支持该方向。";
        return null;
    }
    // Network is never triggered by a selector. Only an explicit settings operation refreshes.
    public static async Task<LanguageCacheStatus> RefreshAsync(TranslationConfiguration config,CancellationToken token,bool force=false,HttpClient? testClient=null)
    {
        if(config.Provider!="DeepL")throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        var endpoint=ProviderHttp.Endpoint(config.Endpoint,"api.deepl.com","api-free.deepl.com");
        if(testClient!=null&&!endpoint.IsLoopback)throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        ProviderHttp.Key(config.ApiKey);var key=Key(endpoint);var gate=Gates.GetOrAdd(key,_=>new(1));
        await gate.WaitAsync(token);
        try
        {
            if(!force&&Cache.TryGetValue(key,out var previous)&&previous.Status.ExpiresAtUtc>DateTimeOffset.UtcNow)return previous.Status;
            var http=new ProviderHttp(testClient);
            Task<JsonDocument> Get(string path)=>http.JsonAsync(()=>{var message=new HttpRequestMessage(HttpMethod.Get,new Uri(endpoint.GetLeftPart(UriPartial.Authority)+path));message.Headers.Authorization=new AuthenticationHeaderValue("DeepL-Auth-Key",config.ApiKey);return message;},15,token,false);
            HashSet<string> sources=new(StringComparer.OrdinalIgnoreCase),targets=new(StringComparer.OrdinalIgnoreCase);var raw=new List<JsonElement>();var version="v3";
            try
            {
                using var doc=await Get("/v3/languages?resource=translate_text");
                foreach(var item in doc.RootElement.EnumerateArray())
                {
                    var code=item.GetProperty("lang").GetString()??throw new JsonException();
                    if(item.GetProperty("status").GetString()!="stable")continue;
                    if(item.GetProperty("usable_as_source").GetBoolean())sources.Add(code);
                    if(item.GetProperty("usable_as_target").GetBoolean())targets.Add(code);
                    raw.Add(item.Clone());
                }
            }
            catch(ProviderException e) when(e.Kind==ProviderErrorKind.ModelUnavailable)
            {
                version="v2";
                foreach(var type in new[]{"source","target"})
                {
                    using var doc=await Get("/v2/languages?type="+type);
                    foreach(var item in doc.RootElement.EnumerateArray()){var code=item.GetProperty("language").GetString()??throw new JsonException();(type=="source"?sources:targets).Add(code);raw.Add(item.Clone());}
                }
            }
            if(sources.Count==0||targets.Count==0)throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);
            var now=DateTimeOffset.UtcNow;var status=new LanguageCacheStatus(version,now,now.AddHours(1),sources.Count,targets.Count);
            Cache[key]=new(status,sources,targets,raw.ToArray());return status;
        }
        catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException){throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);}
        finally{gate.Release();}
    }
}
