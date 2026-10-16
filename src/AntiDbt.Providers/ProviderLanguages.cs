using System.Runtime.CompilerServices;
using AntiDbt.Core;

namespace AntiDbt.Providers;

public sealed record LanguageChoice(LanguageDefinition Language,bool Enabled,string Reason="")
{
    public string Id=>Language.Id;
    public string Name=>Language.Name;
}
public sealed record ProviderLanguageCapabilities(string ProviderId,bool AutoSource,bool Partial,bool Final,string Snapshot,string Note);

// Versioned provider tables: evidence and limits are in docs/phase4/LANGUAGE-SPECS.md and DOMESTIC-SPECS.md.
public static class ProviderLanguages
{
    public const string Snapshot="languages-2026-10-15.1";
    static readonly Dictionary<string,string> SpeechLocales=new()
    {
        ["zh-Hans"]="zh-CN",["zh-Hant"]="zh-TW",["en"]="en-US",["en-US"]="en-US",["en-GB"]="en-GB",
        ["ja"]="ja-JP",["ko"]="ko-KR",["fr"]="fr-FR",["de"]="de-DE",["es"]="es-ES",["pt"]="pt-BR",["pt-BR"]="pt-BR",["pt-PT"]="pt-PT",
        ["ru"]="ru-RU",["it"]="it-IT",["ar"]="ar-SA",["hi"]="hi-IN",["id"]="id-ID",["vi"]="vi-VN",["th"]="th-TH",["tr"]="tr-TR",
        ["nl"]="nl-NL",["pl"]="pl-PL",["uk"]="uk-UA",["sv"]="sv-SE",["da"]="da-DK",["fi"]="fi-FI",["no"]="nb-NO",["el"]="el-GR",["he"]="he-IL",["ms"]="ms-MY"
    };
    static readonly HashSet<string> BaiduExtended=new(){"hi","id","tr","uk","no","he","ms"};
    static bool IsLlm(string id)=>id is "GroqLlm" or "OpenAiCompatible";
    static string Base(string id)=>id switch {"en-US" or "en-GB"=>"en","pt-BR" or "pt-PT"=>"pt",_=>id};
    public static ProviderLanguageCapabilities DescribeAsr(AsrConfiguration c)=>c.Provider switch
    {
        "VolcengineAsr2"=>new(c.Provider,true,true,true,Snapshot,"当前 async 模型为中英混合识别；不支持强制语种或保证繁体输出。"),
        "SherpaOnnx" or "SherpaOnnxStreaming" or "HybridStreamingAsr"=>new(c.Provider,true,true,true,Snapshot,"本地 sherpa-onnx 离线流式识别/混合识别；原生支持中英双语、英语及模型实际支持的语言。"),
        "GroqWhisper"=>new(c.Provider,true,false,true,Snapshot,"Whisper 多语言分段转写；中文使用 zh，不能保证强制繁体字形。"),
        "AzureSpeech"=>new(c.Provider,false,true,true,Snapshot,"当前连续识别适配器使用固定语言；未启用候选语言自动检测。"),
        "GoogleCloudSpeech"=>new(c.Provider,false,true,true,Snapshot,"当前 V2 long、global/us/eu 能力表；中文、希腊语、希伯来语、马来语不可用。"),
        _=>new(c.Provider,false,false,false,Snapshot,"未接入的语音服务。")
    };
    public static string? ValidateAsr(AsrConfiguration c,string source)
    {
        source=LanguageRegistry.Normalize(source);
        if(LanguageRegistry.Find(source)==null)return "当前源语言未在统一语言目录中。";
        var cap=DescribeAsr(c);
        if(c.Provider is "SherpaOnnx" or "SherpaOnnxStreaming" or "HybridStreamingAsr")return null;
        if(c.Provider=="GroqWhisper"&&c.Model is not ("whisper-large-v3-turbo" or "whisper-large-v3"))return "此 Whisper 模型未验证多语言能力。";
        if(source=="auto")return cap.AutoSource?null:"当前识别适配器未实现自动语言检测，请明确选择源语言。";
        if(c.Provider=="VolcengineAsr2")return "火山 async 当前采用中英混合识别，audio.language 不支持强制指定；请选择自动检测。";
        if(c.Provider=="GroqWhisper")return c.Model is "whisper-large-v3-turbo" or "whisper-large-v3"?null:"此 Whisper 模型未验证多语言能力。";
        if(c.Provider=="AzureSpeech")return SpeechLocales.ContainsKey(source)?null:"当前 Azure 识别语言未确认支持。";
        if(c.Provider=="GoogleCloudSpeech")
        {
            if(c.Model.Length>0&&c.Model!="long")return "当前 Google 适配器只实现 long 模型。";
            if(c.Options.GetValueOrDefault("region","global") is not ("global" or "us" or "eu"))return "此 Google 区域与 long 的语言组合尚未确认；不会自动转移数据区域。";
            if(source is "zh-Hans" or "zh-Hant" or "el" or "he" or "ms")return "此语言不在当前 Google V2 long 能力表中；请改用支持它的识别服务。";
            return SpeechLocales.ContainsKey(source)?null:"此 Google long 识别语言尚未确认。";
        }
        return "所选识别服务尚未接入语言能力系统。";
    }
    public static IReadOnlyList<LanguageChoice> AsrChoices(AsrConfiguration config)=>LanguageRegistry.All.Select(x=>new LanguageChoice(x,ValidateAsr(config,x.Id)==null,ValidateAsr(config,x.Id)??"")).ToArray();
    public static IReadOnlyList<LanguageChoice> TargetChoices(TranslationConfiguration config)=>LanguageRegistry.All.Where(x=>x.Id!="auto").Select(x=>new LanguageChoice(x,ValidateTranslation(config,"auto",x.Id)==null,ValidateTranslation(config,"auto",x.Id)??"")).ToArray();
    public static string AsrCode(AsrConfiguration config,string source)
    {
        if(ValidateAsr(config,source) is { } reason)throw new ProviderException(ProviderErrorKind.UnsupportedLanguage,reason);
        source=LanguageRegistry.Normalize(source);
        if(source=="auto")return "";
        if(config.Provider is "SherpaOnnx" or "SherpaOnnxStreaming" or "HybridStreamingAsr")return source is "zh-Hans" or "zh-Hant" ? "zh" : Base(source);
        if(config.Provider=="GroqWhisper")return source is "zh-Hans" or "zh-Hant"?"zh":Base(source);
        var code=SpeechLocales[source];
        return config.Provider=="GoogleCloudSpeech"&&source=="no"?"no-NO":code;
    }
    public static string? ValidateTranslation(TranslationConfiguration config,string source,string target)
    {
        source=LanguageRegistry.Normalize(source);target=LanguageRegistry.Normalize(target);
        if(LanguageRegistry.Find(source)==null||LanguageRegistry.Find(target)==null||target=="auto")return "此翻译方向不在统一语言目录中，目标语言不能自动检测。";
        if(config.Provider is not ("VolcengineMachineTranslation" or "DeepL" or "AzureTranslator" or "GoogleTranslationBasic" or "GroqLlm" or "OpenAiCompatible" or "BaiduTranslate" or "AliyunMT"))return "翻译服务尚未接入语言能力系统。";
        if(config.Provider=="BaiduTranslate"&&config.Options.GetValueOrDefault("extendedLanguages","false")!="true"&&(BaiduExtended.Contains(Base(source))||BaiduExtended.Contains(Base(target))))return "百度该语言需要额外账户权益；请先确认已开通，再启用高级语言权益。";
        if(config.Provider=="DeepL"&&DeepLLanguageCache.Validate(config,source,target) is { } deepLError)return deepLError;
        if(IsLlm(config.Provider))
        {
            var declared=config.Options.GetValueOrDefault("modelLanguages","").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(LanguageRegistry.Normalize).ToHashSet();
            if(!declared.Contains(target)||(source!="auto"&&!declared.Contains(source)))return "当前模型的所选语言能力未确认，请在模型语言声明中选择已确认支持的语言，或先执行显式测试。";
        }
        return null;
    }
    public static string MtSourceCode(TranslationConfiguration config,string source)=>MtCode(config,source,false);
    public static string MtTargetCode(TranslationConfiguration config,string target)=>MtCode(config,target,true);
    static string MtCode(TranslationConfiguration config,string language,bool target)
    {
        var id=LanguageRegistry.Normalize(language);
        if(LanguageRegistry.Find(id)==null||target&&id=="auto")throw new ProviderException(ProviderErrorKind.UnsupportedLanguage,"服务请求使用了未支持的语言标识。");
        if(id=="auto")return config.Provider is "BaiduTranslate" or "AliyunMT"?"auto":"";
        var basic=Base(id);
        return config.Provider switch
        {
            "DeepL"=>id switch {"zh-Hans"=>target?"ZH-HANS":"ZH","zh-Hant"=>target?"ZH-HANT":"ZH","en"=>target?"EN-US":"EN","en-US"=>target?"EN-US":"EN","en-GB"=>target?"EN-GB":"EN","pt"=>target?"PT-PT":"PT","pt-BR"=>target?"PT-BR":"PT","pt-PT"=>target?"PT-PT":"PT","no"=>"NB",_=>basic.ToUpperInvariant()},
            "AzureTranslator"=>id switch {"zh-Hans"=>"zh-Hans","zh-Hant"=>"zh-Hant","no"=>"nb","pt-PT"=>"pt-PT",_=>basic},
            "GoogleTranslationBasic"=>id switch {"zh-Hans"=>"zh-CN","zh-Hant"=>"zh-TW",_=>basic},
            "VolcengineMachineTranslation"=>id switch {"zh-Hans"=>"zh","zh-Hant"=>"zh-Hant","no"=>"nb",_=>basic},
            "BaiduTranslate"=>id switch {"zh-Hans"=>"zh","zh-Hant"=>"cht","ja"=>"jp","ko"=>"kor","fr"=>"fra","es"=>"spa","ar"=>"ara","vi"=>"vie","uk"=>"ukr","sv"=>"swe","da"=>"dan","fi"=>"fin","no"=>"nor","he"=>"heb","ms"=>"may",_=>basic},
            "AliyunMT"=>id switch {"zh-Hans"=>"zh","zh-Hant"=>"zh-tw",_=>basic},
            "GroqLlm" or "OpenAiCompatible"=>id,
            _=>throw new ProviderException(ProviderErrorKind.Configuration,"未知翻译服务。")
        };
    }
    public static string LanguageLabel(string id)=>LanguageRegistry.Find(id) is { } language?$"{language.Name} ({language.Id})":"自动识别的语言";
    public static string? ValidateSettings(AppSettings settings,bool credentials=true)
    {
        settings=LanguageSettings.Upgrade(settings);
        if(ValidateAsr(settings.Asr,settings.SourceLanguage) is { } asrError)return asrError;
        if(credentials&&ProviderDescriptors.Validate(settings.Asr) is { } missingAsr)return missingAsr;
        if(LanguageRegistry.Same(settings.SourceLanguage,settings.TargetLanguage))return null;
        if(ValidateTranslation(settings.Translation,settings.SourceLanguage,settings.TargetLanguage) is { } mtError)return mtError;
        if(credentials&&ProviderDescriptors.Validate(settings.Translation) is { } missingMt)return missingMt;
        if(settings.FallbackEnabled)
        {
            if(!settings.FallbackCostConsent)return "备用翻译尚未确认可能产生的额外费用。";
            if(settings.FallbackTranslationProviderId==settings.Translation.Provider||!settings.TranslationProfiles.TryGetValue(settings.FallbackTranslationProviderId,out var backup))return "请选择不同且已配置的备用翻译服务。";
            if(ValidateTranslation(backup,settings.SourceLanguage,settings.TargetLanguage) is { } backupError)return "备用服务："+backupError;
            if(credentials&&ProviderDescriptors.Validate(backup) is { } missingBackup)return "备用服务："+missingBackup;
        }
        return null;
    }
}

internal sealed class LanguageAwareAsrProvider(IAsrProvider provider) : IAsrProvider
{
    public IAsrSession CreateSession(AsrConfiguration configuration)
    {
        var canonical=LanguageRegistry.Normalize(configuration.Language);
        var mapped=configuration with{Language=ProviderLanguages.AsrCode(configuration,canonical)};
        return new Session(provider.CreateSession(mapped),canonical);
    }
    sealed class Session(IAsrSession inner,string language):IAsrSession
    {
        public Task ConnectAsync(CancellationToken token)=>inner.ConnectAsync(token);
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm,CancellationToken token)=>inner.SendAudioAsync(pcm,token);
        public Task CompleteAsync(CancellationToken token)=>inner.CompleteAsync(token);
        public async IAsyncEnumerable<AsrResult> GetResultsAsync([EnumeratorCancellation]CancellationToken token)
        {
            await foreach(var result in inner.GetResultsAsync(token))yield return result with{Language=language=="auto"?LanguageRegistry.Normalize(result.Language):language};
        }
        public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
}
