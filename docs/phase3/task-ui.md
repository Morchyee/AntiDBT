# Task: dynamic provider settings UI

Read first. User Phase3 spec sections6/7/8. Own ONLY src/AntiDbt.Desktop/SettingsWindow.cs, minimal related MainWindow.cs provider selection validation, DesktopSmoke.cs extra settings checks, and docs/phase3/task-ui-report.md. Root owns catalog/Core/storage/HTTP/SDKs. Do not edit other files or spawn agents. Root is not editing these files during task.

Existing UI and source-first caption/render/scroll/drag/size/font must remain. Replace hardcoded Volc settings with independent ASR and MT selectors.

Root API now exists in AntiDbt.Providers:
- ProviderDescriptors.Asr / Translation: lists ProviderDescriptor(Id,DisplayName,Capability,Partial,Final,Fields,Languages).
- ProviderField(Key,Label,Secret,Required,DefaultValue,Choices). Keys "ApiKey","Endpoint","Model","Language" map record properties. Other Secret fields map Secrets dict, nonsecret map Options.
- ProviderDescriptors.Find(id,bool asr), DefaultAsr(id), DefaultTranslation(id), Validate(AsrConfiguration)/Validate(TranslationConfiguration) -> string? completeness error, null means locally complete ONLY.
- FetchGroqModelsAsync(apiKey,token) -> live public model-list request; only invoke on explicit button, never automatically. Distinguish model-list request success from translation authentication/actual translation success.
- AppSettings now AsrProfiles Dictionary<string,AsrConfiguration>, TranslationProfiles equivalent; selected Asr/Translation retained for compatibility. SettingsStore root encrypts all profile secrets and migrates v1 to v2.

IDs ASR VolcengineAsr2/GroqWhisper/AzureSpeech/GoogleCloudSpeech; MT VolcengineMachineTranslation/DeepL/AzureTranslator/GoogleTranslationBasic/GroqLlm/OpenAiCompatible. Every ID has working implementation (SDK integration root wiring in progress), no placeholder controls. Two selectors cannot bind together.

Behaviors:
1. Snapshot current dynamic editors before switching provider, retain separate profiles in memory, restore selected existing config or descriptor defaults. Never carry one vendor's key into another profile. Preserve profiles on Result save.
2. PasswordBox all secrets, including service-account JSON (paste JSON as secret value, no file scan). Explicit "清除当前服务凭据" clears ApiKey and all selected Secrets only; no log/clipboard credentials.
3. Common translation timeout/debounce/context settings retained; audio device, target zh/en/font/source/topmost retained. Optional custom prompt/temperature/token max fields descriptor-driven.
4. Config check button is nonnetwork, status "配置完整；认证和真实请求尚未验证" or missing config error. Incomplete profiles may be saved, but MainWindow start must use descriptor Validate for both selected services (replace old HasCredentials helper requiring APIKey for every service; Google serviceaccount has no key).
5. Provider switch while running only happens through settings action which already stops/releases old pipeline. On save, clear old subtitle store generation (prevent old provider captions lingering before next start). Keep UI busy guard.
6. Display capability and languages text; Groq Whisper final-only segmented, 10s minimum billed request warning; SDK services native streaming. Seed LiveInterpret stays gated text, no fake selector.
7. Endpoint editors validate HTTPS/WSS, no userinfo/query/fragment; loopback only for test configs, normal UI secure URLs. Custom OpenAI URL explicit user-entered; no auto redirect. Root providers enforce host pinning. Do not fetch/test paid speech/translation automatically.
8. Groq LLM model editable/fetch button explicit. Model list may contain modalities; label selection carefully; root is filtering by current metadata.
9. Settings save errors must not turn into success. API key missing opens settings rather than throwing. ProviderException safe messages can be displayed; never expose arbitrary SDK exceptions.

Tests in existing DesktopSmoke: synthetic no credentials/network and testMode no settings persistence. Add programmatic independent selector switching/snapshot/secret deletion/restore and capability assertions via internal test seams. Existing WPF11 checks remain. Run actual WPF smoke when build available, coordinate with root to avoid compiling shared obj concurrently. Save report/evidence honest.
