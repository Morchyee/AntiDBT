namespace AntiDbt.Core;

public sealed record LanguageDefinition(string Id, string Name, string Sample);

// Language identity and Chinese labels only. Vendor codes/capabilities belong in adapters.
public static class LanguageRegistry
{
    public static IReadOnlyList<LanguageDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new LanguageDefinition("auto","自动检测","Hello, world."),
        new("zh-Hans","中文（简体）","你好，世界。"),new("zh-Hant","中文（繁體）","你好，世界。"),
        new("en","英语","Hello, world."),new("en-US","英语（美国）","Hello, world."),new("en-GB","英语（英国）","Hello, world."),
        new("ja","日语","こんにちは、世界。"),new("ko","韩语","안녕하세요, 세계."),new("fr","法语","Bonjour le monde."),
        new("de","德语","Hallo Welt."),new("es","西班牙语","Hola, mundo."),new("pt","葡萄牙语","Olá, mundo."),
        new("pt-BR","葡萄牙语（巴西）","Olá, mundo."),new("pt-PT","葡萄牙语（葡萄牙）","Olá, mundo."),
        new("ru","俄语","Привет, мир."),new("it","意大利语","Ciao, mondo."),new("ar","阿拉伯语","مرحبا بالعالم."),
        new("hi","印地语","नमस्ते दुनिया।"),new("id","印尼语","Halo, dunia."),new("vi","越南语","Xin chào thế giới."),
        new("th","泰语","สวัสดีชาวโลก"),new("tr","土耳其语","Merhaba dünya."),new("nl","荷兰语","Hallo wereld."),
        new("pl","波兰语","Witaj świecie."),new("uk","乌克兰语","Привіт, світе."),new("sv","瑞典语","Hej världen."),
        new("da","丹麦语","Hej verden."),new("fi","芬兰语","Hei maailma."),new("no","挪威语","Hei verden."),
        new("el","希腊语","Γεια σου κόσμε."),new("he","希伯来语","שלום עולם."),new("ms","马来语","Helo dunia.")
    });
    public static string Normalize(string? id)
    {
        var text=(id??"").Trim().Replace('_','-');
        var existing=All.FirstOrDefault(x=>x.Id.Equals(text,StringComparison.OrdinalIgnoreCase));
        if(existing is not null)return existing.Id;
        return text.ToLowerInvariant() switch
        {
            "" or "auto" or "und"=>"auto",
            "zh" or "zh-cn" or "zh-sg" or "zh-hans-cn" or "cmn-hans-cn"=>"zh-Hans",
            "zh-tw" or "zh-hk" or "zh-hant-tw" or "cmn-hant-tw"=>"zh-Hant",
            "ja-jp"=>"ja","ko-kr"=>"ko","fr-fr"=>"fr","de-de"=>"de","es-es"=>"es",
            "ru-ru"=>"ru","it-it"=>"it","ar-sa" or "ar-xa"=>"ar","hi-in"=>"hi","id-id"=>"id",
            "vi-vn"=>"vi","th-th"=>"th","tr-tr"=>"tr","nl-nl"=>"nl","pl-pl"=>"pl","uk-ua"=>"uk",
            "sv-se"=>"sv","da-dk"=>"da","fi-fi"=>"fi","nb" or "nb-no" or "no-no"=>"no",
            "el-gr"=>"el","he-il" or "iw"=>"he","ms-my"=>"ms",
            _=>text // Preserve unknown legacy selection visibly; never silently change it to English.
        };
    }
    public static LanguageDefinition? Find(string? id) => All.FirstOrDefault(x=>x.Id==Normalize(id));
    public static bool Same(string? left,string? right)
    {
        // Different scripts/locales are deliberate user choices, not an automatic passthrough.
        return Normalize(left)!="auto"&&Normalize(left)==Normalize(right);
    }
}

public static class LanguageSettings
{
    public static AppSettings Upgrade(AppSettings settings) => settings with
    {
        SourceLanguage=LanguageRegistry.Normalize(string.IsNullOrWhiteSpace(settings.SourceLanguage)?(settings.Asr.Provider.Length==0?"":settings.Asr.Language):settings.SourceLanguage),
        TargetLanguage=LanguageRegistry.Normalize(settings.TargetLanguage),
        RecentLanguages=(settings.RecentLanguages??new()).Select(LanguageRegistry.Normalize).Where(x=>x!="auto"&&LanguageRegistry.Find(x)!=null).Distinct().Take(12).ToList(),
        Theme=settings.Theme is "System" or "Dark" or "Light"?settings.Theme:"System",
        BackgroundOpacity=double.IsNaN(settings.BackgroundOpacity) ? 0.80 : Math.Clamp(settings.BackgroundOpacity, 0.0, 1.0)
    };
    public static AppSettings Select(AppSettings original,string source,string target)
    {
        source=LanguageRegistry.Normalize(source);target=LanguageRegistry.Normalize(target);
        if(LanguageRegistry.Find(source)==null||LanguageRegistry.Find(target)==null||target=="auto")throw new ArgumentException("请选择有效的源语言和目标语言；目标语言不能自动检测。");
        var asr=original.Asr with{Language=source};
        var profiles=new Dictionary<string,AsrConfiguration>(original.AsrProfiles);
        if(asr.Provider.Length>0)profiles[asr.Provider]=asr;
        return original with{SourceLanguage=source,TargetLanguage=target,Asr=asr,AsrProfiles=profiles,RecentLanguages=new[]{source,target}.Concat(original.RecentLanguages??new()).Where(x=>x!="auto").Distinct().Take(12).ToList()};
    }
}
