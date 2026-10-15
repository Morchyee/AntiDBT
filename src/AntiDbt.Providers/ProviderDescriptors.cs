using System.Net.Http.Headers;
using System.Text.Json;
using AntiDbt.Core;
namespace AntiDbt.Providers;

public sealed record ProviderField(string Key,string Label,bool Secret=false,bool Required=false,string DefaultValue="",string[]? Choices=null);
public sealed record ProviderDescriptor(string Id,string DisplayName,string Capability,bool Partial,bool Final,ProviderField[] Fields,string[] Languages);
public static class ProviderDescriptors
{
    static ProviderField F(string key,string label,bool secret=false,bool required=false,string value="",string[]? choices=null)=>new(key,label,secret,required,value,choices);
    public static IReadOnlyList<ProviderDescriptor> Asr {get;}=new ProviderDescriptor[]
    {
        new("VolcengineAsr2","火山 ASR 2.0","原生流式；Partial + Final；公开协议",true,true,[F("Endpoint","WebSocket 地址",value:"wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async",required:true),F("ApiKey","API Key",true),F("Model","模型",value:"bigmodel",required:true),F("Language","语言（空为服务默认）"),F("resourceId","资源 ID",value:"volc.seedasr.sauc.duration",required:true),F("appId","旧版 APP ID"),F("accessKey","旧版 Access Token",true)],["服务支持的语言；默认自动"]),
        new("GroqWhisper","Groq Whisper","本地能量 VAD 分段转写；仅 Final，无原生 Partial。每次请求最低计费10秒，短分段可能增加费用。",false,true,[F("Endpoint","转写地址",value:"https://api.groq.com/openai/v1/audio/transcriptions",required:true),F("ApiKey","Groq API Key",true,true),F("Model","Whisper 模型",value:"whisper-large-v3-turbo",required:true,choices:["whisper-large-v3-turbo","whisper-large-v3"]),F("Language","原语言 ISO 代码（空为自动）"),F("minSegmentMs","最短段毫秒",value:"1000"),F("maxSegmentMs","最长段毫秒",value:"4000"),F("silenceMs","静音切分毫秒",value:"600"),F("overlapMs","强制切分重叠毫秒",value:"200"),F("vadThreshold","能量阈值（非语义 VAD）",value:"0.008"),F("timeoutSeconds","请求超时秒",value:"30")],["Whisper 官方多语言列表"]),
        new("AzureSpeech","Azure Speech（微软语音）","官方 Speech SDK 连续识别；Partial + Final",true,true,[F("ApiKey","Speech Key",true,true),F("region","Azure Region",required:true),F("Language","BCP-47 语言",value:"en-US",required:true)],["Azure Speech 官方语言列表"]),
        new("GoogleCloudSpeech","Google Cloud Speech","官方 V2 gRPC StreamingRecognize；Partial + Final；约4分45秒主动重连",true,true,[F("project","Google Cloud Project ID",required:true),F("region","区域",value:"global",required:true),F("Language","BCP-47 语言",value:"en-US",required:true),F("Model","识别模型",value:"long",required:true),F("serviceAccountJson","服务账户 JSON（加密；不读取环境凭据）",true,true)],["Google STT V2 官方模型/区域语言列表"])
    };
    public static IReadOnlyList<ProviderDescriptor> Translation {get;}=new ProviderDescriptor[]
    {
        new("VolcengineMachineTranslation","豆包机器翻译","HTTP 完整片段翻译；公开接口无上下文字段",false,true,[F("Endpoint","HTTPS 地址",value:"https://openspeech.bytedance.com/api/v3/machine_translation/matx_translate",required:true),F("ApiKey","API Key",true),F("resourceId","资源 ID",value:"volc.speech.mt",required:true),F("appId","旧版 APP ID"),F("accessKey","旧版 Access Token",true)],["zh","en","官方支持列表"]),
        new("DeepL","DeepL API","官方 Free/Pro API；支持短上下文，不使用网页翻译",false,true,[F("Endpoint","API Free / Pro 地址",value:"https://api-free.deepl.com/v2/translate",required:true,choices:["https://api-free.deepl.com/v2/translate","https://api.deepl.com/v2/translate"]),F("ApiKey","DeepL API Key",true,true)],["DeepL 官方语言列表"]),
        new("AzureTranslator","Microsoft Translator（Azure）","官方 Azure 文本翻译；区域按资源类型填写",false,true,[F("Endpoint","Translate endpoint",value:"https://api.cognitive.microsofttranslator.com/translate",required:true),F("ApiKey","Translator Key",true,true),F("region","Region（资源需要时必填）")],["Azure Translator 官方语言列表"]),
        new("GoogleTranslationBasic","Google Cloud Translation Basic","V2 API Key 模式；与 Speech 服务账户认证独立",false,true,[F("Endpoint","Basic V2 地址",value:"https://translation.googleapis.com/language/translate/v2",required:true),F("ApiKey","受限 Google API Key",true,true)],["Google Translation 官方语言列表"]),
        Llm("GroqLlm","Groq LLM","https://api.groq.com/openai/v1"),
        Llm("OpenAiCompatible","自定义 OpenAI 兼容","https://api.openai.com/v1")
    };
    static ProviderDescriptor Llm(string id,string name,string url)=>new(id,name,"Chat Completions 文本翻译；模型由用户填写/获取，短上下文；兼容能力取决于服务",false,true,[F("Endpoint","Base URL（HTTPS，禁止自动重定向）",value:url,required:true),F("ApiKey","API Key",true,true),F("Model","文本模型 ID（不预设过时列表）",required:true),F("temperature","温度",value:"0"),F("maxTokens","最大输出 Token",value:"512"),F("prompt","可选提示词，支持 {target}/{source}")],["由模型支持的语言"]);
    public static ProviderDescriptor Find(string id,bool asr)=>(asr?Asr:Translation).FirstOrDefault(x=>x.Id==id)??throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
    public static AsrConfiguration DefaultAsr(string id)
    {
        var d=Find(id,true);return new(){Provider=id,Endpoint=Default(d,"Endpoint"),Model=Default(d,"Model"),Language=Default(d,"Language"),Options=Options(d)};
    }
    public static TranslationConfiguration DefaultTranslation(string id)
    {
        var d=Find(id,false);return new(){Provider=id,Endpoint=Default(d,"Endpoint"),Model=Default(d,"Model"),Options=Options(d)};
    }
    static string Default(ProviderDescriptor d,string key)=>d.Fields.FirstOrDefault(x=>x.Key==key)?.DefaultValue??"";
    static Dictionary<string,string> Options(ProviderDescriptor d)=>d.Fields.Where(x=>!x.Secret&&x.Key is not ("Endpoint" or "Model" or "Language")).ToDictionary(x=>x.Key,x=>x.DefaultValue);
    public static string? Validate(AsrConfiguration c)=>Validate(Find(c.Provider,true),c.ApiKey,c.Endpoint,c.Model,c.Language,c.Options,c.Secrets);
    public static string? Validate(TranslationConfiguration c)=>Validate(Find(c.Provider,false),c.ApiKey,c.Endpoint,c.Model,"",c.Options,c.Secrets);
    static string? Validate(ProviderDescriptor d,string key,string endpoint,string model,string language,Dictionary<string,string> options,Dictionary<string,string> secrets)
    {
        if(endpoint.Length>0&&(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||uri.Scheme!=(d.Id=="VolcengineAsr2"?"wss":"https")))return "服务地址无效，请使用正确的安全协议";
        foreach(var f in d.Fields)
        {
            string value=f.Key switch{"ApiKey"=>key,"Endpoint"=>endpoint,"Model"=>model,"Language"=>language,_=>f.Secret?secrets.GetValueOrDefault(f.Key,""):options.GetValueOrDefault(f.Key,"")};
            if(f.Required&&string.IsNullOrWhiteSpace(value))return "请填写 "+f.Label;
            if(f.Key!="serviceAccountJson"&&(value.Contains('\r')||value.Contains('\n'))&&f.Key!="prompt")return "配置字段含无效换行";
        }
        if(d.Id is "VolcengineAsr2" or "VolcengineMachineTranslation" &&string.IsNullOrWhiteSpace(key)&&(!options.TryGetValue("appId",out var app)||string.IsNullOrWhiteSpace(app)||!secrets.TryGetValue("accessKey",out var access)||string.IsNullOrWhiteSpace(access)))return "请填写 API Key 或旧版 APP ID / Access Token";
        return null;
    }
    public static async Task<IReadOnlyList<string>> FetchGroqModelsAsync(string apiKey,CancellationToken token)
    {
        ProviderHttp.Key(apiKey);using var doc=await new ProviderHttp().JsonAsync(()=>{var r=new HttpRequestMessage(HttpMethod.Get,"https://api.groq.com/openai/v1/models");r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",apiKey);return r;},15,token);
        try{return TextModelCandidates(doc.RootElement);}
        catch(Exception e)when(e is KeyNotFoundException or InvalidOperationException){throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);}
    }
    internal static IReadOnlyList<string> TextModelCandidates(JsonElement root)=>root.GetProperty("data").EnumerateArray().Select(x=>x.GetProperty("id").GetString()??"").Where(x=>x.Length>0&&!new[]{"whisper","tts","orpheus","prompt-guard","safeguard","compound"}.Any(kind=>x.Contains(kind,StringComparison.OrdinalIgnoreCase))).Distinct().Order().ToArray();
}
