# Existing-provider language specifications

Research date: 2026-10-15 (Asia/Shanghai). Snapshot ID: `languages-2026-10-15.1`.
Documentation and existing source inspection only; no builds, cloud requests, credentials, or authentication validation. A documented language is not proof that a user's subscription/model/region can execute it.

## Contract and compact maps

Keep registry identity separate from provider request codes. `supported`, `unsupported`, and `unknown` are distinct states. Unknown must not become supported through ISO-code normalization. A capability record needs provider, API/model, region, source/target direction, request code, script/locale limitation, auto mode, partial/final flags, snapshot version, and evidence URL. Retain invalid selections with their reason.

The registry below contains all 27 required identities. Norwegian means Bokmål for providers offering that variant; do not silently claim Nynorsk. Generic English uses the displayed `en-US` ASR default, and generic Portuguese the displayed `pt-BR` ASR default. These are explicit product defaults, not universal linguistic equivalence.

| Registry / Chinese label | Whisper request | Azure ASR request | Google V2 `long` request |
|---|---|---|---|
| zh-Hans 简体中文 | zh* | zh-CN | unsupported |
| zh-Hant 繁体中文 | zh* | zh-TW | unsupported |
| en 英语 | en | en-US | en-US |
| ja 日语 | ja | ja-JP | ja-JP |
| ko 韩语 | ko | ko-KR | ko-KR |
| fr 法语 | fr | fr-FR | fr-FR |
| de 德语 | de | de-DE | de-DE |
| es 西班牙语 | es | es-ES | es-ES |
| pt 葡萄牙语 | pt | pt-BR | pt-BR |
| ru 俄语 | ru | ru-RU | ru-RU |
| it 意大利语 | it | it-IT | it-IT |
| ar 阿拉伯语 | ar | ar-SA | ar-SA |
| hi 印地语 | hi | hi-IN | hi-IN |
| id 印度尼西亚语 | id | id-ID | id-ID |
| vi 越南语 | vi | vi-VN | vi-VN |
| th 泰语 | th | th-TH | th-TH |
| tr 土耳其语 | tr | tr-TR | tr-TR |
| nl 荷兰语 | nl | nl-NL | nl-NL |
| pl 波兰语 | pl | pl-PL | pl-PL |
| uk 乌克兰语 | uk | uk-UA | uk-UA |
| sv 瑞典语 | sv | sv-SE | sv-SE |
| da 丹麦语 | da | da-DK | da-DK |
| fi 芬兰语 | fi | fi-FI | fi-FI |
| no 挪威语 | no | nb-NO | no-NO |
| el 希腊语 | el | el-GR | unsupported |
| he 希伯来语 | he | he-IL | unsupported |
| ms 马来语 | ms | ms-MY | unsupported |

Whisper column: [OpenAI tokenizer language inventory](https://raw.githubusercontent.com/openai/whisper/main/whisper/tokenizer.py), with [Groq transcription models and language parameter](https://console.groq.com/docs/speech-to-text). Azure column: [Speech-to-text locale table](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/language-support?tabs=stt). Google column: [V2 model/locale/location table](https://docs.cloud.google.com/speech-to-text/docs/speech-to-text-supported-languages). Google supported entries here are a conservative `global`/`us`/`eu`, `long` snapshot; do not apply them to arbitrary regional endpoints.

`*` Whisper Chinese is one `zh` language token. Neither `zh-Hant` nor `zh-TW` is an ASR language token; transcription cannot promise to force Traditional output. Preserve the user's script preference separately and label that limitation. Both Groq `whisper-large-v3` and `whisper-large-v3-turbo` use this map; neither is a live partial-results API.

Variants: `en-US` and `en-GB` remain distinct registry selections; Azure/Google send those exact locales, Whisper sends `en`. `pt-BR`/`pt-PT` send exact ASR locales, Whisper `pt`. Azure `zh-HK` is Cantonese Traditional, not an alias for Taiwanese Mandarin. Google Chinese codes `cmn-Hans-CN`, `cmn-Hant-TW` and Cantonese `yue-Hant-HK` belong to other models in the matrix; adding them to fixed `long` requests does not enable Chinese.

## Volc ASR: endpoint matters

Existing implementation uses `bigmodel_async` and `model_name=bigmodel`. Official [streaming API](https://docs.volcengine.com/docs/DoubaoVoice/LargemodelstreamingautomaticspeechrecognitionAPI?lang=zh) documents `audio.language` as supported only by `bigmodel_nostream`, not second-pass recognition. Default recognition includes Chinese/English and domestic dialects. Therefore the versioned conservative async map is: `zh-Hans -> omitted audio.language`, `en/en-US/en-GB -> omitted audio.language`, with limitation `mixed Chinese/English model; source not forced`. `zh-Hant` cannot promise Traditional output and should be unavailable as a forced script-specific ASR choice. All other 24 registry identities are unsupported in the async map pending endpoint-specific official confirmation.

Do not copy the 13-language foreign-model table from `bigmodel_nostream` or file recognition onto `bigmodel_async`. The [official release notes](https://www.volcengine.com/docs/6561/162929?lang=en) explicitly identify those endpoints. New `enable_auto_lang` documentation describes small-language model detection; its applicability to the existing async model is unknown, so do not set unrestricted auto=true. An optional source mode may be described precisely as `中英混合识别`, not detection across all registry languages. The wire protocol does support changing partial hypotheses and final utterances; partial=true, final=true.

## Translation maps for the four fixed MT APIs

All rows below are source and target supported, subject to the DeepL model/account qualification below. These tables apply to text translation, not document translation, glossary support, speech translation, or language detection coverage.

| Registry | Volc speech MT | DeepL source / target | Azure Translator | Google Basic |
|---|---|---|---|---|
| zh-Hans | zh | ZH / ZH-HANS | zh-Hans | zh-CN |
| zh-Hant | zh-Hant | ZH / ZH-HANT | zh-Hant | zh-TW |
| en | en | EN / EN-US (explicit default) | en | en |
| ja | ja | JA | ja | ja |
| ko | ko | KO | ko | ko |
| fr | fr | FR | fr | fr |
| de | de | DE | de | de |
| es | es | ES | es | es |
| pt | pt | PT / PT-PT (explicit default) | pt | pt |
| ru | ru | RU | ru | ru |
| it | it | IT | it | it |
| ar | ar | AR | ar | ar |
| hi | hi | HI | hi | hi |
| id | id | ID | id | id |
| vi | vi | VI | vi | vi |
| th | th | TH | th | th |
| tr | tr | TR | tr | tr |
| nl | nl | NL | nl | nl |
| pl | pl | PL | pl | pl |
| uk | uk | UK | uk | uk |
| sv | sv | SV | sv | sv |
| da | da | DA | da | da |
| fi | fi | FI | fi | fi |
| no | nb | NB | nb | no |
| el | el | EL | el | el |
| he | he | HE | he | he |
| ms | ms | MS | ms | ms |

Sources: [Volc speech MT endpoint and exact 32-language table](https://docs.volcengine.com/docs/DoubaoVoice/MachineTranslationLargeModel-APIAccessDocumentation?lang=zh), [DeepL official SDK codes and direction comments](https://raw.githubusercontent.com/DeepL/deepl-dotnet/main/DeepL/LanguageCode.cs), [Azure Text Translator language table](https://learn.microsoft.com/en-us/azure/ai-services/translator/language-support), [Google Cloud Translation languages](https://docs.cloud.google.com/translate/docs/languages).

Volc here is `/api/v3/machine_translation/matx_translate`, resource `volc.speech.mt`, not the separately signed `TranslateText` API. All 27 registry languages occur in its mutually translatable 32-language inventory. Source omission/empty means automatic detection; target is mandatory. `en-US/en-GB -> en`, `pt-BR/pt-PT -> pt`: variant-specific output is unsupported. Do not import `zh-Hant-hk`/`zh-Hant-tw` from a different Volc product.

DeepL: `en-US/en-GB` source both map to `EN`, target exact `EN-US/EN-GB`; Portuguese source `PT`, targets `PT-BR/PT-PT`. Chinese source is `ZH` for either script; targets preserve scripts. These directions are independently corroborated by [DeepL's official integration guide](https://github.com/DeepL/deepl-api-prompts/blob/main/mcp.md). HI/HE/MS/TH/VI are documented SDK languages, not automatically unsupported based on an old 30-language table. Their compatibility with a selected legacy latency model, endpoint plan, or beta feature is unknown unless the resource query/model documentation confirms it; use the current API's default model selection, or classify an explicitly forced incompatible model as unknown/unavailable. Do not equate SDK constants with account authentication.

Azure MT and Google Basic map English US/GB to `en`; no documented dialect-specific English MT output in these maps. Azure `pt` means Brazil; preserve explicit `pt-PT` when selected. Google Basic does not promise PT country variants from generic `pt`. Google lists Hebrew `he`, with legacy `iw` compatibility; prefer `he` for Basic, not Google's separate Speech `iw-IL` code.

## Auto, events and regions

| Provider mode | Source auto at wire level | Partial | Final | Existing-code limitation |
|---|---|---|---|---|
| Volc async ASR | only default mixed model; unrestricted auto unknown | yes | yes | currently forwards arbitrary audio.language |
| Groq transcription, both Whisper models | omit language | no | yes | preserve VAD/cooldown; never audio/translations |
| Azure continuous Speech SDK | candidate-set LID after explicit implementation | yes | yes | currently fixed SpeechRecognitionLanguage |
| Google V2 streaming long | candidate-set detection after explicit implementation | yes | yes | currently exactly one LanguageCodes item |
| Volc speech MT | omit/empty source_language | no | yes | keep target mandatory |
| DeepL text | omit source_lang | no | yes | auto-detection coverage may be resource-specific |
| Azure text MT | omit from | no | yes | no target auto |
| Google Basic | omit source | no | yes | no target auto |
| Groq/custom chat MT | prompt-based inference, model capability unknown | not ASR partial | yes | model/endpoint-specific quality unknown |

[Azure LID documentation](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/language-identification) requires a candidate list (up to four at-start, ten continuous), not arbitrary-language detection. Do not include different locales of the same language as competing LID candidates. Existing C# continuous recognizer can be extended using AutoDetectSourceLanguageConfig; merely omitting a fixed locale currently becomes English in SpeechValidation.

[Google multiple-language detection](https://docs.cloud.google.com/speech-to-text/docs/multiple-languages) is a preview candidate-set feature (up to three), limited to `global`, `us`, `eu` and documented models `long`, `short`, `telephony`; it is not `language_codes=[auto]` or an empty list. Current streaming applicability should be validated before advertising auto in this implementation; keep auto=false until the transport handles it. Do not confuse AutoDetectDecodingConfig (audio encoding) with language detection.

Google `global` endpoint is `speech.googleapis.com`; `us`/`eu` endpoints are `us-speech.googleapis.com`/`eu-speech.googleapis.com`, and recognizer location must match. Other syntactically accepted regions in existing validation are not evidence that `long` exists there. Conservative map rejects an unknown `(region,model,locale)` tuple rather than redirecting data to global.

Azure subscription region must equal the Speech resource region. [Speech regions](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/regions) lists real-time support, separately from fast/batch/custom/TTS. Conservative examples verified in that table: `eastasia`, `southeastasia`, `japaneast`, `eastus`, `eastus2`, `westeurope`, `northeurope`. This is intentionally not an exhaustive region allowlist; unlisted regions remain unknown until the real-time row is checked. A locale table's language region (e.g. en-US) is not an Azure deployment region.

## Dynamic language cache

DeepL documentation now supports [v3 resource-specific languages](https://developers.deepl.com/docs/languages/using-the-languages-api): `GET /v3/languages?resource=translate_text`, authenticated on api.deepl.com or api-free.deepl.com. Preserve raw entries `lang`, `name`, `usable_as_source`, `usable_as_target`, `status`, `features`; use stable entries by default. Its documentation recommends caching up to one hour. [v2 migration](https://developers.deepl.com/docs/languages/migrating-from-v2-languages) also documents separate `/v2/languages?type=source` and `?type=target`; preserve those arrays and do not infer source from target.

Suggested local format (design only): `schemaVersion=1`, `snapshotId`, `provider`, `endpointHost`, `resource`, `apiVersion`, `fetchedAtUtc`, `expiresAtUtc`, `evidenceUrls`, `rawSource`, `rawTarget` or `rawLanguages`, `normalizedCapabilities`. Cache keys include endpoint host/resource/API version/model constraints, not a secret or secret hash. Never serialize headers, credentials, or response request metadata containing secrets. Use atomic replacement after complete successful parsing. Retain the last good cache on failure and show its age; use this dated documented fallback if absent. A refresh must use an explicit authorized application operation, not an agent-discovered private key. No query was executed during this research.

Azure's public [Languages API](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/v3/languages) provides `translation` codes without authentication, with ETag caching; do not confuse its detection dictionary with translation direction. Google Basic [list languages](https://docs.cloud.google.com/translate/docs/basic/discovering-supported-languages) requires configured project authentication/API key; its raw language codes and request model belong in the cache. Requests here were documentation only.

## LLM capability and inspected configuration defects

Groq/custom OpenAI-compatible endpoints do not define one universal supported-language list. [Groq models](https://console.groq.com/docs/models) is a model inventory, not certification of all language pairs. Use unknown source/target quality for unverified manual models; allow deliberate model-specific user selection with a visible unknown-capability reason, rather than pretending every language is supported or silently using English. Construct prompts with human-readable language plus exact registry script/region. Models/list response, an HTTP success, and OpenAI wire compatibility do not prove translation quality, script fidelity, or detection ability. ASR auto remains gated separately.

Source inspection found:

- `ProviderHttp.Language` splits every code at '-'/'_', losing script and country; Google Basic Traditional Chinese is therefore currently damaged, and generic normalization cannot be the common mapping policy.
- DeepL already uppercases target and gives generic en/pt defaults, but source/target maps and dynamic-language cache must be independent.
- Azure Speech/Google Speech `SpeechValidation.Language` silently falls back to en-US on blank input; reject or implement supported auto explicitly instead.
- Google transport fixes `Model="long"`, sets one locale, and permits syntactically valid unverified regions; validation must match this exact matrix, including rejecting Chinese/Greek/Hebrew/Malay for this model.
- Volc ASR currently sends audio.language for nonblank configuration despite endpoint restriction; output language metadata cannot truthfully assert a forced source if the endpoint ignores it.
- Existing Volc MT is the speech MT API, so Norwegian is `nb`, not a generic `no` pass-through.

All listed restrictions are documentation-based. Real subscription access, multilingual recognition accuracy, mixed-language output, script reliability, native hardware behavior, and account-specific model availability remain unverified.
