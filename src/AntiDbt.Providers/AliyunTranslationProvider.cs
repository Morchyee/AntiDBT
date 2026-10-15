using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AntiDbt.Core;
using AlibabaCloud.SDK.Alimt20181012.Models;

namespace AntiDbt.Providers;

public sealed class AliyunTranslationProvider : ITranslationProvider
{
    readonly TranslationConfiguration configuration;
    readonly Uri? loopbackEndpoint;
    readonly ProviderHttp http=new();
    static readonly SemaphoreSlim PhysicalCalls=new(2,2);
    public AliyunTranslationProvider(TranslationConfiguration configuration)=>this.configuration=configuration;
    internal AliyunTranslationProvider(TranslationConfiguration configuration,Uri loopbackEndpoint):this(configuration)
    {
        if(!loopbackEndpoint.IsLoopback||loopbackEndpoint.Scheme!="http"||loopbackEndpoint.AbsolutePath!="/"||loopbackEndpoint.Query.Length>0||loopbackEndpoint.UserInfo.Length>0)throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        this.loopbackEndpoint=loopbackEndpoint;
    }
    public async Task<string> TranslateAsync(TranslationRequest request,CancellationToken token)
    {
        var c=configuration with{Provider="AliyunMT"};
        if(ProviderLanguages.ValidateTranslation(c,request.SourceLanguage,request.TargetLanguage)!=null)throw ProviderHttp.Failure(ProviderErrorKind.UnsupportedLanguage);
        if(string.IsNullOrWhiteSpace(request.Text)||request.Text.Length>5000)throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        var accessKeyId=c.Secrets.GetValueOrDefault("accessKeyId","");ProviderHttp.Key(accessKeyId);ProviderHttp.Key(c.ApiKey);
        var region=c.Options.GetValueOrDefault("region","cn-hangzhou");
        if(region is not ("cn-hangzhou" or "ap-southeast-1"))throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        var endpoint=ProviderHttp.Endpoint(c.Endpoint,"mt."+region+".aliyuncs.com");
        if(endpoint.IsLoopback||endpoint.AbsolutePath!="/"||!endpoint.IsDefaultPort)throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        endpoint=loopbackEndpoint??endpoint;
        // Official request model and public ACS3 signer; own transport is necessary
        // because Tea follows redirects and has no public switch to disable them.
        var model=new TranslateGeneralRequest{FormatType="text",Scene="general",SourceLanguage=ProviderLanguages.MtSourceCode(c,request.SourceLanguage),TargetLanguage=ProviderLanguages.MtTargetCode(c,request.TargetLanguage),SourceText=request.Text};
        using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"FormatType",model.FormatType},{"Scene",model.Scene},{"SourceLanguage",model.SourceLanguage},{"TargetLanguage",model.TargetLanguage},{"SourceText",model.SourceText}});
        var payload=await form.ReadAsByteArrayAsync(token);var hash=Convert.ToHexStringLower(SHA256.HashData(payload));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(c.TimeoutSeconds,1,120)));
        bool acquired=false;
        try
        {
            await PhysicalCalls.WaitAsync(deadline.Token);acquired=true;
            using var doc=await http.JsonAsync(()=>
            {
                var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["host"]=endpoint.Authority,["x-acs-action"]="TranslateGeneral",["x-acs-version"]="2018-10-12",
                    ["x-acs-date"]=DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture),
                    ["x-acs-signature-nonce"]=Guid.NewGuid().ToString("N"),["x-acs-content-sha256"]=hash
                };
                var signed=new Tea.TeaRequest{Method="POST",Pathname="/",Query=new(),Headers=headers};
                var authorization=AlibabaCloud.OpenApiUtil.Client.GetAuthorization(signed,"ACS3-HMAC-SHA256",hash,accessKeyId,c.ApiKey);
                var message=new HttpRequestMessage(HttpMethod.Post,endpoint){Content=new ByteArrayContent(payload)};
                message.Content.Headers.ContentType=new("application/x-www-form-urlencoded");
                foreach(var h in headers)message.Headers.TryAddWithoutValidation(h.Key,h.Value);
                message.Headers.TryAddWithoutValidation("Authorization",authorization);return message;
            },c.TimeoutSeconds,deadline.Token,false,false,ClassifyResponse);
            var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("Code",out var code))throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);
            var codeText=code.ValueKind==JsonValueKind.String?code.GetString():code.ValueKind==JsonValueKind.Number?code.GetRawText():null;
            if(string.IsNullOrEmpty(codeText))throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);
            if(codeText!="200")throw ProviderHttp.Failure(BusinessKind(codeText));
            var translated=root.GetProperty("Data").GetProperty("Translated").GetString();
            if(string.IsNullOrWhiteSpace(translated))throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);
            return translated;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(OperationCanceledException){throw ProviderHttp.Failure(ProviderErrorKind.Timeout);}
        catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException){throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);}
        finally{if(acquired)PhysicalCalls.Release();}
    }
    static ProviderErrorKind ClassifyResponse(System.Net.HttpStatusCode status,byte[] body)
    {
        try
        {
            using var doc=JsonDocument.Parse(body);
            if(doc.RootElement.ValueKind==JsonValueKind.Object&&doc.RootElement.TryGetProperty("Code",out var code))
            {
                var kind=BusinessKind(code.ToString());
                if(kind!=ProviderErrorKind.Service||code.ToString()=="10012")return kind;
            }
        }
        catch(JsonException){}
        return (int)status switch{401 or 403=>ProviderErrorKind.Authentication,400 or 404=>ProviderErrorKind.Configuration,429=>ProviderErrorKind.RateLimited,_=>ProviderErrorKind.Service};
    }
    internal static ProviderErrorKind BusinessKind(string code)=>code switch
    {
        "10001" or "RequestTimeout" or "Timeout"=>ProviderErrorKind.Timeout,
        "10004" or "InvalidParameter" or "MissingParameter"=>ProviderErrorKind.Configuration,
        "InvalidAccessKeyId.NotFound" or "InvalidAccessKeyId" or "SignatureDoesNotMatch" or "Forbidden" or "Unauthorized" or "401" or "403" or "10010"=>ProviderErrorKind.Authentication,
        "Throttling" or "Throttling.User" or "Throttling.Api" or "429"=>ProviderErrorKind.RateLimited,
        "QuotaExceeded" or "InsufficientBalance" or "10013"=>ProviderErrorKind.Quota,
        "UnsupportedLanguage" or "10005"=>ProviderErrorKind.UnsupportedLanguage,
        "ModelNotFound"=>ProviderErrorKind.ModelUnavailable,
        _=>ProviderErrorKind.Service
    };
}
