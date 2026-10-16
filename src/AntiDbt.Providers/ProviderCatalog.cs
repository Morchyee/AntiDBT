using AntiDbt.Core;
namespace AntiDbt.Providers;

public static class ProviderCatalog
{
    public static AppSettings DefaultSettings() => new()
    {
        Asr = new()
        {
            Provider = "VolcengineAsr2",
            Endpoint = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async",
            Model = "bigmodel",
            Language = "",
            Options = new()
            {
                ["resourceId"] = "volc.seedasr.sauc.duration"
            }
        },
        Translation = new()
        {
            Provider = "VolcengineMachineTranslation",
            Endpoint = "https://openspeech.bytedance.com/api/v3/machine_translation/matx_translate",
            Options = new()
            {
                ["resourceId"] = "volc.speech.mt"
            }
        }
    };
    public static IAsrProvider CreateAsr(AsrConfiguration config) => new LanguageAwareAsrProvider(config.Provider switch
    {
        "VolcengineAsr2"=>new VolcengineAsrProvider(),
        "GroqWhisper"=>new GroqWhisperProvider(),
        "SherpaOnnx" or "SherpaOnnxStreaming"=>new SherpaOnnxStreamingAsrProvider(),
        "HybridStreamingAsr"=>new HybridStreamingAsrProvider(),
        "AzureSpeech"=>new AntiDbt.CloudSpeech.AzureSpeechProvider(),
        "GoogleCloudSpeech"=>new AntiDbt.CloudSpeech.GoogleSpeechProvider(),
        _=>throw new NotSupportedException("所选语音服务尚未支持；LiveInterpret 国内协议仍待验证。")
    });
    public static ITranslationProvider CreateTranslator(TranslationConfiguration config) => new ValidatedTranslationProvider(config,config.Provider switch
    {
        "VolcengineMachineTranslation"=>new VolcengineMachineTranslationProvider(config),
        "BaiduTranslate"=>new BaiduTranslationProvider(config),
        "AliyunMT"=>new AliyunTranslationProvider(config),
        _=>new HttpTranslationProvider(config)
    });
    public static ITranslationProvider CreateTranslator(AppSettings settings)
    {
        settings=LanguageSettings.Upgrade(settings);
        if(LanguageRegistry.Same(settings.SourceLanguage,settings.TargetLanguage))return new PassthroughTranslationProvider();
        var primary=CreateTranslator(settings.Translation);
        if(!settings.FallbackEnabled)return primary;
        if(!settings.FallbackCostConsent||settings.FallbackTranslationProviderId==settings.Translation.Provider||!settings.TranslationProfiles.TryGetValue(settings.FallbackTranslationProviderId,out var backup))throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        return new FallbackTranslationProvider(primary,CreateTranslator(backup));
    }
}

internal static class ProviderConfiguration
{
    internal static Uri Endpoint(string value, bool socket)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("服务地址无效。");
        bool local = uri.IsLoopback;
        if (uri.Scheme != (socket ? "wss" : "https") && !(local && uri.Scheme == (socket ? "ws" : "http")))
            throw new ArgumentException("服务地址必须使用安全协议；仅本机测试允许明文连接。");
        if (!local && !uri.Host.Equals("openspeech.bytedance.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请使用公开 openspeech 服务地址。");
        return uri;
    }
    internal static Dictionary<string, string> Headers(string apiKey, Dictionary<string, string> options, Dictionary<string, string> secrets, string resource)
    {
        var headers = new Dictionary<string, string> { ["X-Api-Resource-Id"] = options.GetValueOrDefault("resourceId", resource), ["X-Api-Request-Id"] = Guid.NewGuid().ToString() };
        if (!string.IsNullOrWhiteSpace(apiKey))
            headers["X-Api-Key"] = apiKey;
        else if (options.TryGetValue("appId", out var appId) && secrets.TryGetValue("accessKey", out var accessKey) && !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(accessKey))
        {
            headers["X-Api-App-Key"] = appId;
            headers["X-Api-Access-Key"] = accessKey;
        }
        else
            throw new ArgumentException("请填写 API Key，或旧版 APP ID 和 Access Token。");
        if (headers.Values.Any(v => string.IsNullOrWhiteSpace(v) || v.Contains('\r') || v.Contains('\n')))
            throw new ArgumentException("服务配置无效。");
        return headers;
    }
}
