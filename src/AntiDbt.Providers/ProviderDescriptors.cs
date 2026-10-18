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
        new("SherpaOnnx","本地流式识别 (sherpa-onnx)","本地离线模型推理；原生流式 Partial + Final；无需云端 API Key 与网络请求",true,true,[F("Model","模型目录路径",value:"models/sherpa-onnx",required:true),F("numThreads","推理线程数",value:"2",choices:["1","2","4","8"]),F("decodingMethod","解码方式",value:"greedy_search",choices:["greedy_search","modified_beam_search"]),F("Language","识别语言（空为自动/模型默认）"),F("rule1TrailingSilence","长停顿静音阈值秒",value:"2.4"),F("rule2TrailingSilence","原生短停顿静音秒",value:"0.5"),F("rule3UtteranceLength","原生最长分句秒",value:"9.0"),F("sentenceSilenceMs","分句静音毫秒",value:"500"),F("sentencePreferredSeconds","建议分句秒",value:"5"),F("sentenceMaxSeconds","最大连续片段秒",value:"9"),F("sentenceMaxChars","建议英文字符数",value:"120"),F("sentenceStabilityMs","稳定边界毫秒",value:"300"),F("sentenceVadThreshold","分句能量阈值",value:"0.008")],["中英双语、英语及模型实际支持的语言"]),
        new("HybridStreamingAsr","混合识别模式 (实验性)","本地流式实时预览 (sherpa-onnx) + 云端整句高精终态 (Groq Whisper)；毫秒级上屏与高准确率结合",true,true,[F("Model","本地模型目录路径",value:"models/sherpa-onnx",required:true),F("ApiKey","Groq API Key（用于云端高精 Final；未填则使用纯本地终态）",secret:true),F("cloudEndpoint","云端 Whisper 端点",value:"https://api.groq.com/openai/v1/audio/transcriptions"),F("cloudModel","云端 Whisper 模型",value:"whisper-large-v3-turbo",choices:["whisper-large-v3-turbo","whisper-large-v3"]),F("numThreads","本地推理线程数",value:"2",choices:["1","2","4","8"]),F("Language","识别语言（空为自动/模型默认）"),F("cloudTimeoutSeconds","云端终态超时秒",value:"5"),F("sentenceSilenceMs","分句静音毫秒",value:"500"),F("sentencePreferredSeconds","建议分句秒",value:"5"),F("sentenceMaxSeconds","最大连续片段秒",value:"9"),F("sentenceMaxChars","建议英文字符数",value:"120"),F("sentenceStabilityMs","稳定边界毫秒",value:"300"),F("sentenceVadThreshold","分句能量阈值",value:"0.008")],["中英双语、英语及模型实际支持的语言"]),
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
        Llm("OpenAiCompatible","OpenAI 兼容（多服务商）","https://api.openai.com/v1"),
        new("BaiduTranslate","百度翻译开放平台","通用文本翻译；部分语言需要账户高级权益",false,true,[F("Endpoint","官方 HTTPS 地址",value:"https://fanyi-api.baidu.com/api/trans/vip/translate",required:true),F("appId","APPID",required:true),F("ApiKey","密钥",true,true),F("extendedLanguages","已确认开通高级语言权益",value:"false",choices:["false","true"])],["统一语言目录；高级语言以账户权益为准"]),
        new("AliyunMT","阿里云机器翻译","官方 SDK TranslateGeneral；取消等待后在途调用可能继续计费",false,true,[F("Endpoint","官方地域端点",value:"https://mt.cn-hangzhou.aliyuncs.com",required:true,choices:["https://mt.cn-hangzhou.aliyuncs.com","https://mt.ap-southeast-1.aliyuncs.com"]),F("region","地域",value:"cn-hangzhou",required:true,choices:["cn-hangzhou","ap-southeast-1"]),F("accessKeyId","AccessKey ID（加密保存）",true,true),F("ApiKey","AccessKey Secret",true,true)],["统一语言目录的27种主要语言"])
    };
    static ProviderDescriptor Llm(string id,string name,string url)=>new(id,name,"Chat Completions 文本翻译；语言能力须按所选模型确认",false,true,[F("Endpoint","Base URL（HTTPS，禁止自动重定向）",value:url,required:true),F("ApiKey","API Key",true,true),F("Model","文本模型 ID（手动输入或显式获取）",required:true),F("modelLanguages","已确认模型语言（逗号分隔，如 zh-Hans,en,ja）"),F("temperature","温度",value:"0"),F("sendTemperature","发送温度参数",value:"false",choices:["false","true"]),F("maxTokens","最大输出 Token",value:"512"),F("tokenLimitParameter","Token 上限参数",value:"none",choices:["none","max_tokens","max_completion_tokens"]),F("allowLocalHttp","明确允许本机 HTTP 测试端点",value:"false",choices:["false","true"]),F("prompt","可选提示词，支持 {target}/{source}")],["模型相关；未声明不保证支持"]);
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
        if(endpoint.Length>0&&(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||uri.Scheme!=(d.Id=="VolcengineAsr2"?"wss":"https")&&!(d.Id=="OpenAiCompatible"&&options.GetValueOrDefault("allowLocalHttp")=="true"&&uri.Scheme=="http"&&uri.IsLoopback)))return "服务地址无效，请使用正确的安全协议；自托管本机 HTTP 需要明确启用";
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
