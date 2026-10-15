using AntiDbt.Core;

namespace AntiDbt.Providers;

public sealed record ApiPreset(string Id,string Name,string BaseUrl,string ApplicationUrl,string BillingUrl,string Note);
public sealed record ProviderGuide(string ApplicationUrl,string DocumentationUrl,string BillingUrl,string Note);
public static class ServiceGuides
{
    public static IReadOnlyList<ApiPreset> Presets {get;}=Array.AsReadOnly(new[]
    {
        new ApiPreset("SiliconFlow","硅基流动","https://api.siliconflow.cn/v1","https://cloud.siliconflow.cn/account/ak","https://cloud.siliconflow.cn/","按账户中当前模型确认语言及计费。"),
        new("DeepSeek","DeepSeek","https://api.deepseek.com","https://platform.deepseek.com/","https://api-docs.deepseek.com/quick_start/pricing","模型 ID 手动填写，不固定版本。"),
        new("Groq","Groq","https://api.groq.com/openai/v1","https://console.groq.com/","https://console.groq.com/docs/billing-faqs","文本模型与 Whisper ASR 分开配置。"),
        new("TencentTokenHub","腾讯 TokenHub（广州）","https://tokenhub.tencentmaas.com/v1","https://console.cloud.tencent.com/tokenhub","https://console.cloud.tencent.com/tokenhub","需在控制台开通当前模型；不使用旧 TMT。"),
        new("TencentTokenHubIntl","腾讯 TokenHub（新加坡）","https://tokenhub-intl.tencentmaas.com/v1","https://console.cloud.tencent.com/tokenhub","https://console.cloud.tencent.com/tokenhub","国际站密钥、地域和模型权限须单独确认。"),
        new("Other","其他 / 自部署","","","","手动填写完整 Base URL；仅本机可明确启用 HTTP。")
    });
    public static TranslationConfiguration ApplyPreset(TranslationConfiguration config,string presetId)
    {
        var preset=Presets.FirstOrDefault(x=>x.Id==presetId)??throw new ArgumentException("未知兼容服务预设。");
        var options=new Dictionary<string,string>(config.Options){["preset"]=preset.Id,["modelLanguages"]="",["sendTemperature"]="false",["tokenLimitParameter"]="none"};
        // Selecting a new service never carries credentials/model declarations to its destination.
        return config with{Provider="OpenAiCompatible",Endpoint=preset.BaseUrl,Model="",ApiKey="",Secrets=new(),Options=options};
    }
    public static ProviderGuide For(string id)=>id switch
    {
        "BaiduTranslate"=>new("https://fanyi-api.baidu.com/","https://fanyi-api.baidu.com/doc/21","https://fanyi-api.baidu.com/","APPID + 密钥；高级语言须确认权益，免费额度以账户为准。"),
        "AliyunMT"=>new("https://www.aliyun.com/product/ai/alimt","https://help.aliyun.com/zh/machine-translation/","https://help.aliyun.com/zh/machine-translation/product-overview/billing-methods","AccessKey ID / Secret 均加密；在途 SDK 请求可能继续完成。"),
        "DeepL"=>new("https://www.deepl.com/pro-api","https://developers.deepl.com/docs/","https://www.deepl.com/pro-api","可选 Free/Pro 服务，其他 MT 无需 DeepL 密钥。"),
        "GroqWhisper" or "GroqLlm"=>new("https://console.groq.com/","https://console.groq.com/docs/","https://console.groq.com/docs/billing-faqs","Whisper 为分段 Final-only；文本模型语言由所选模型决定。"),
        "AzureSpeech" or "AzureTranslator"=>new("https://portal.azure.com/","https://learn.microsoft.com/en-us/azure/ai-services/","https://azure.microsoft.com/pricing/","Speech 与 Translator 资源及密钥独立；区域须匹配资源。"),
        "GoogleCloudSpeech" or "GoogleTranslationBasic"=>new("https://console.cloud.google.com/","https://cloud.google.com/docs","https://cloud.google.com/pricing","Speech 使用显式服务账户；Translation Basic 使用独立受限 API Key。"),
        "VolcengineAsr2" or "VolcengineMachineTranslation"=>new("https://console.volcengine.com/speech/","https://www.volcengine.com/docs/6561","https://console.volcengine.com/speech/","当前火山 async 仅中英混合模式；LiveInterpret 仍为未验证可选项。"),
        _=>new("","","","API 兼容不等于模型多语言质量保证；请确认所选服务的模型和计费。")
    };
}
