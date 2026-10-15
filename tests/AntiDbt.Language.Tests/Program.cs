using AntiDbt.Core;
using System.Text.Json;

var checks=new List<object>();int failures=0;
void Assert(bool value,string why){if(!value)throw new InvalidOperationException(why);}
void Check(string name,Action body){try{body();checks.Add(new{name,pass=true});}catch(Exception e){failures++;checks.Add(new{name,pass=false,error=e.Message});}}
Check("registry includes required Chinese-named languages",()=>{
    var required=new[]{"zh-Hans","zh-Hant","en","ja","ko","fr","de","es","pt","ru","it","ar","hi","id","vi","th","tr","nl","pl","uk","sv","da","fi","no","el","he","ms"};
    foreach(var id in required){var language=LanguageRegistry.Find(id);Assert(language is not null&&language.Name.Any(c=>c>255),"required language missing "+id);}
    Assert(LanguageRegistry.All.Select(x=>x.Id).Distinct().Count()==LanguageRegistry.All.Count,"duplicate registry ID");
});
Check("canonical aliases preserve script and English variants",()=>{Assert(LanguageRegistry.Normalize("zh")=="zh-Hans"&&LanguageRegistry.Normalize("zh-TW")=="zh-Hant"&&LanguageRegistry.Normalize("zh-HK")=="zh-Hant","Chinese scripts");Assert(LanguageRegistry.Normalize("en-GB")=="en-GB"&&LanguageRegistry.Normalize("en-US")=="en-US","English variants");Assert(LanguageRegistry.Normalize("ja-JP")=="ja"&&LanguageRegistry.Normalize("")=="auto","legacy/auto");});
Check("legacy language upgrade retains profiles and secrets",()=>{
    var profile=new AsrConfiguration{Provider="GroqWhisper",Language="zh-CN",ApiKey="synthetic-key",Options=new(){["keep"]="yes"},Secrets=new(){["keep"]="synthetic-secret"}};
    var old=new AppSettings{Asr=profile,TargetLanguage="en",Translation=new(){Provider="DeepL",ApiKey="synthetic-mt"},AsrProfiles=new(){[profile.Provider]=profile},FontSize="large"};
    var upgraded=LanguageSettings.Upgrade(old);Assert(upgraded.SourceLanguage=="zh-Hans"&&upgraded.TargetLanguage=="en","upgrade languages");Assert(upgraded.Asr.ApiKey==old.Asr.ApiKey&&upgraded.Asr.Options["keep"]=="yes"&&upgraded.Translation.ApiKey==old.Translation.ApiKey&&upgraded.FontSize=="large","upgrade lost setting/key");
});
Check("selection persists actual ASR source and MRU bounded without modifying original",()=>{
    var old=LanguageSettings.Upgrade(new AppSettings{Asr=new(){Provider="GroqWhisper",Language=""},TargetLanguage="zh"});
    var next=LanguageSettings.Select(old,"zh-Hant","en-GB");Assert(next.SourceLanguage=="zh-Hant"&&next.Asr.Language=="zh-Hant"&&next.TargetLanguage=="en-GB","selection only visual");Assert(old.Asr.Language==""&&old.TargetLanguage=="zh-Hans","original mutated");
    foreach(var lang in LanguageRegistry.All.Where(x=>x.Id!="auto"))next=LanguageSettings.Select(next,lang.Id,"en");Assert(next.RecentLanguages.Count<=12&&next.RecentLanguages.Distinct().Count()==next.RecentLanguages.Count,"unbounded MRU");
});
Check("target auto and unknown selections reject without changes",()=>{var settings=LanguageSettings.Upgrade(new AppSettings());foreach(var target in new[]{"auto","made-up-language"}){try{LanguageSettings.Select(settings,"en",target);throw new Exception("invalid target accepted");}catch(ArgumentException){}}Assert(settings.SourceLanguage=="auto","invalid selection mutated");});
Check("fallback defaults require independent opt in and consent",()=>{var settings=new AppSettings();Assert(!settings.FallbackEnabled&&!settings.FallbackCostConsent&&settings.FallbackTranslationProviderId=="","fallback default unsafe");});
await LanguageCases.RunAsync(async(name,body)=>{try{await body();checks.Add(new{name,pass=true});}catch(Exception e){failures++;checks.Add(new{name,pass=false,error=e.GetType().Name+": "+e.Message});}});
await LanguagePipelineCases.RunAsync(async(name,body)=>{try{await body();checks.Add(new{name,pass=true});}catch(Exception e){failures++;checks.Add(new{name,pass=false,error=e.GetType().Name+": "+e.Message});}});
Directory.CreateDirectory("artifacts/phase4");await File.WriteAllTextAsync("artifacts/phase4/language-tests.json",JsonSerializer.Serialize(new{scope="neutral settings, mapped HTTP handler requests and synthetic providers; no cloud",total=checks.Count,failures,checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{total=checks.Count,failures,checks}));return failures==0?0:1;
