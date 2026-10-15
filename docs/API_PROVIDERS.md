# ASR / MT 服务配置

更新：2026-10-15。适配器已实际实现；本地模拟通过不代表账户认证、云端效果或账单已验证。服务均可能需要开通权限、账单或付费额度，不能把试用额度视为永久免费。密钥和服务账户 JSON 只在软件设置中输入，不要发到聊天、写入源码或发行包。

## 使用与安全

设置中的“语音识别”和“翻译”独立选择，支持 4×6 种配置组合；语言、模型、区域和账户权限仍须匹配实际服务。未配置时会提示填写，默认仍为火山 ASR + 豆包机器翻译。保存、切换不会自动调用付费识别/翻译；点击主窗口“开始”才采集系统声音。Groq 模型列表按钮会显式联网，但成功仅证明列表请求成功。

“检查当前配置”检查必填字段和基本安全格式，不代表认证成功或真实请求成功。服务错误通过字幕状态显示。停止和切换服务会取消旧工作并隔离会话；不在收费厂商之间自动 fallback。翻译失败保留原文，不生成伪造译文。

每个 Provider 独立保存配置和密钥；DPAPI CurrentUser 封套位于便携目录 `data/settings.json`。公开配置 JSON 不包含密钥。删除凭据后保存会清除选中服务的密钥，其余服务仍保留。更换接口 scheme/主机/端口会清除旧凭据，须为新地址重新填写；同主机路径修改保留密钥。加密封套校验凭据与目的地址绑定，拒绝篡改后错误配对。DPAPI 不隔离同一个 Windows 用户运行的其他程序。

HTTP 禁止携带授权头自动重定向，生产设置要求 HTTPS/WSS，不接受 URL 用户信息、查询密钥或 fragment。官方 Provider 限定官方主机；自定义地址须由用户确认可信。当前没有单独代理编辑器，使用系统网络/代理配置；不支持兼容服务的任意私有认证、Responses、工具调用或 SSE 能力协商。

## 语音识别

| 服务 | 配置与开通方式 | 实时行为 / 适用场景 | 常见问题 |
|---|---|---|---|
| 火山 ASR 2.0 | 在火山语音控制台开通公开流式服务；填写 API Key、资源 ID，或原有 APP ID + Access Token；默认 bigmodel_async | 原生 WebSocket Partial/Final，沿用 Phase2 协议算法；适合原有国内服务配置 | Key 与通用 AK/SK 不同；资源权限、并发资源与语言必须匹配 |
| Groq Whisper | 在 Groq 控制台创建 API Key；选择 whisper-large-v3-turbo 或 whisper-large-v3；可填原语言 ISO 代码 | 本地能量 VAD 分段 → 内存 WAV → 官方 audio/transcriptions；**仅 Final，无原生 Partial**。适合接受分段等待的场景 | 最短段默认1秒，最长4秒，静音600ms，强制段重叠200ms；每次请求最低按10秒计费，短段可能增加成本。无语义 VAD，噪声/边界准确率需实测 |
| Azure Speech（微软语音） | 创建 Azure Speech 资源，从 Keys and Endpoint 获取 Speech Key + Region；填 BCP-47，如 en-US / zh-CN | 官方 Speech SDK 1.52.0 连续识别，Recognizing/Recognized → Partial/Final；服务默认标点，未添加独立标点开关 | 区域和 Key 不匹配、权限/语言错误、网络失败。SDK 原生库随便携目录提供，不能只复制 EXE |
| Google Cloud Speech | 启用 Speech-to-Text API/账单；创建最小权限服务账户（如 Speech Client），在设置粘贴服务账户 JSON；填 Project、region、BCP-47 | 官方 Google.Cloud.Speech.V2 1.8.0 / gRPC StreamingRecognize；Partial/Final；当前 long 模型，global 默认；约4分45秒主动断开后由核心重连 | API/账单/IAM/区域模型支持须匹配。只支持 service_account，限定官方 token_uri；不扫描 ADC、环境变量或凭据文件。重连不承诺语音无缝衔接 |

Groq 默认 16kHz/16bit/mono WAV，任务队列最多4项、结果队列256项，繁忙时丢旧保持新鲜度。静音不提交，不提交仅含重叠的尾段；只对相邻成功作业进行边界去重，避免丢段后误删文本。拉丁词边界和中文重叠均有回归测试，但重复自然语言、噪声和模型改写不能保证完全消除。

Azure/Google 原文在 Partial 到达时立即显示，译文由现有调度器更新；Groq 需要等分段转写完成才显示原文。三者真实延迟不能从本地测试推断。Google 每个音频消息保守限制15000字节，首次消息仅配置；主动重连会创建新 generation，旧响应不会覆盖新字幕，但未实现跨连接词级拼接和云端 bridging。

官方依据：[火山公开协议说明](PROVIDER-SPECS.md)、[Groq 语音转写](https://console.groq.com/docs/speech-to-text)、[Azure Speech 快速开始](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/get-started-speech-to-text)、[Azure 语言支持](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/language-support)、[Google StreamingRecognize](https://docs.cloud.google.com/speech-to-text/v2/docs/streaming-recognize)、[Google V2 配额](https://docs.cloud.google.com/speech-to-text/v2/quotas)、[Google 区域/模型](https://docs.cloud.google.com/speech-to-text/v2/docs/speech-to-text-supported-languages)。型号/区域语言以官方当前支持列表为准，界面不冒充本机已验证的完整语言清单。

## 文本翻译

| 服务 | 配置与认证 | 行为 / 适用场景 | 常见错误 |
|---|---|---|---|
| 豆包机器翻译 | 火山公开 MT 权限；API Key + volc.speech.mt，或旧认证 | 沿用原有完整片段 HTTP 翻译；公开接口不发送上下文 | 认证/资源权限/业务状态错误；原有适配器安全通用提示未全面转为新增错误枚举 |
| DeepL API | 订阅 DeepL **API** Free/Pro，在账户取得 Authentication Key；对应 api-free.deepl.com / api.deepl.com | text 数组、source_lang/target_lang；自动源语言时省略 source；支持短 context。适合常规文本机器翻译 | 401/403认证，429限流，456配额；网页会员不等于 API 权限。en 目标映射 EN-US，pt 映射 PT-PT |
| Microsoft Translator（Azure） | 创建 Translator 资源取得 Key，资源要求时填 Region | Ocp-Apim-Subscription-Key / Region；v3 Translate。当前支持全球 api.cognitive.microsofttranslator.com 地址 | 资源类型/Region/权限不匹配；未实现 VNET 自定义域名与资源专用地址，不能直接把它们当全球端点 |
| Google Cloud Translation Basic | 启用 Translation API/账单，创建并限制 API Key | V2 Basic key 模式，format=text，解码 translatedText HTML 实体；独立于 STT 服务账户 | Key 限制、账单/配额和语言错误；**未实现 Advanced OAuth/服务账户 MT**，不得粘贴 JSON 代替 Key |
| Groq LLM | Groq API Key；点击显式模型列表按钮或填写当前可用文本模型 ID | 官方 Chat Completions；低温度0、最多512输出Token、简短翻译提示与少量上下文 | 模型下架/未授权、token限额、429/配额。列表排除已知语音/守卫/compound模型，其余仅候选，能力仍须确认 |
| 自定义 OpenAI 兼容 | HTTPS Base URL、Key、Model；可选提示词、温度、输出上限、超时、上下文 | Base URL 后接 /chat/completions（完整该路径也可）；支持 choices[0].message.content 文本。适合用户自己的兼容服务 | 不同服务可能不支持 temperature/max_tokens/相同模型；不自动试探其他接口或厂商。修改目的主机须重新填Key |

目标语言 UI 沿用 zh/en；ASR 源语言由服务结果或配置传递。Azure 将 zh/zh-CN/zh-SG/zh-Hans 映射 zh-Hans，zh-TW/zh-HK/zh-Hant 映射 zh-Hant；Google Basic 目前主要按语言基础代码映射，未提供区域方言/繁简目标完整选择；DeepL en/pt 特例和原有豆包别名映射单独处理。语言不支持必须显示错误，不以未知翻译替代。

翻译统一沿用固定默认350ms合并、2并发、128缓存/待办、精确 revision/generation 防过期覆盖。Final 会唤醒该片段合并等待并抢占旧预览，排队时优先于预览；超时可取消；HTTP最多两次尝试，429遵守 Retry-After（并发冷却不会缩短），明确配额不足不重试。5xx仅 MT允许一次重试；不自动重提网络失败的 Whisper，避免不确定的重复计费。长冷却可能先触发本次请求超时，但后续同一实例及 Groq 自动重连的新会话仍等待剩余冷却。取消已提交的预览不能回滚服务已产生的计费。LLM以指令要求不输出解释/Markdown，未以未经验证的模型效果保证代替测试。

官方依据：[DeepL 请求](https://developers.deepl.com/api-reference/translate/request-translation)、[Azure Translator v3](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/v3/translate)、[Google Basic v2](https://docs.cloud.google.com/translate/docs/reference/rest/v2/translate)、[Groq API Reference](https://console.groq.com/docs/api-reference)、[Groq 当前模型](https://console.groq.com/docs/models)。费用/可用额度以各控制台当前规则为准。

## 验证与故障判断

新增 Provider 的结构化错误区分配置、认证、限流、配额、模型、语言、超时、网络、响应格式与服务故障；显示消息不回显服务响应正文、Key 或授权头。401/403不重试；服务器5xx、用户主动取消、断线、空响应各有本地故障夹具。Google gRPC ResourceExhausted 保守视为配额，不声称已区分所有限流细类。

测试层次及实际结果见 [VALIDATION](VALIDATION.md)。SDK gateway/SDK响应对象测试不是实际 Google gRPC 服务器，也不是 Azure 云事件实测。所有真实服务的认证、识别/翻译质量、地区可达性、付费额度、首字/p95延迟和小时级稳定性仍需用户在软件内配置后验收。Seed LiveInterpret 2.0 继续门控，不显示为已实现。
