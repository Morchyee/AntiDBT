# 国内翻译与兼容服务集成规格

核查日期：2026-10-15（Asia/Shanghai）。仅阅读公开官方文档、官方 SDK 源码及 NuGet；未读取凭据、未调用推理/翻译/模型列表接口、未安装依赖或构建。本文的“验证”指文档/源码证据，不代表账户开通或真实云端成功。实施建议明确区别于供应商保证。

## 百度通用翻译

官方[通用翻译接入文档](https://fanyi-api.baidu.com/doc_bd/21)的搜索索引可读取正文；普通 `/doc/21` 当前只返回导航，正文更新日期未公开取得。官方[产品页](https://api.fanyi.baidu.com/product/11)可作为申请入口。度厂版文档包含内部认证例外，外部用户不可套用该例外；应以自己的控制台权益为准。

请求地址 `https://fanyi-api.baidu.com/api/trans/vip/translate`；参数 `q/from/to/appid/salt/sign`。签名使用未 URL 编码的原始 UTF-8 文本：`MD5(appid + q + salt + secret)`，输出 32 位小写十六进制，之后才做表单编码。建议 HTTPS POST + FormUrlEncodedContent，避免文本/签名进入 URL 日志；C# 内置 MD5/HttpClient 足够，不需第三方百度包。不要记录 secret、签名原文或完整请求。

处理 HTTP 错误后仍须检查 JSON `error_code`：52001 超时、52002 系统故障；52003 未授权；54000 缺参、54001 签名错误；54003 限频、54005 长文本频繁（文档建议 3 秒后再试）；54004 余额不足；58000 IP 非法、58001 方向不支持、58002 服务关闭、90107 认证未生效。实施建议：鉴权/余额/方向不重试；瞬态故障由统一调度限次，避免 SDK 与外层叠加重试。账户 QPS 和文本额度按控制台权益，不写死全账户相同限额。[错误定义](https://fanyi-api.baidu.com/doc_bd/21)

## 27 种语言映射

下表百度代码来自上述官方常见/完整语言表；阿里代码来自[官方语言表](https://help.aliyun.com/zh/machine-translation/support/supported-languages-and-codes)（页面更新时间 2026-08-27）。百度标记“权益”的语言在完整表中有代码，但不属于常见列表，文档要求企业认证尊享版；代码存在不能等同当前账户可用。阿里文本表说明支持两两互译，但账户服务权限仍独立校验。

| UI 语言 | 百度 | 百度额外权益 | 阿里 |
|---|---|---|---|
| 简体中文 | zh | 否 | zh |
| 繁体中文 | cht | 否 | zh-tw |
| 英语 | en | 否 | en |
| 日语 | jp | 否 | ja |
| 韩语 | kor | 否 | ko |
| 法语 | fra | 否 | fr |
| 德语 | de | 否 | de |
| 西班牙语 | spa | 否 | es |
| 葡萄牙语 | pt | 否 | pt |
| 俄语 | ru | 否 | ru |
| 意大利语 | it | 否 | it |
| 阿拉伯语 | ara | 否 | ar |
| 印地语 | hi | 权益 | hi |
| 印尼语 | id | 权益 | id |
| 越南语 | vie | 否 | vi |
| 泰语 | th | 否 | th |
| 土耳其语 | tr | 权益 | tr |
| 荷兰语 | nl | 否 | nl |
| 波兰语 | pl | 否 | pl |
| 乌克兰语 | ukr | 权益 | uk |
| 瑞典语 | swe | 否 | sv |
| 丹麦语 | dan | 否 | da |
| 芬兰语 | fin | 否 | fi |
| 挪威语 | nor | 权益 | no |
| 希腊语 | el | 否 | el |
| 希伯来语 | heb | 权益 | he |
| 马来语 | may | 权益 | ms |

百度额外中文代码：粤语 `yue`、文言文 `wyw`；阿里粤语 `yue`。百度挪威变体另有 `nob/nno`，不可将通用挪威语静默改成其中之一。两家源自动检测为 `auto`；目标禁止 auto。英语 US/GB 在应用注册表保留，但这两家文档仅列 `en`，请求可显式归并语言级 en，并在 UI 说明不保证地区风格；中文繁简不可归并。地区专有中文（香港/台湾）不得推断为供应商独立支持代码。

## 阿里 TranslateGeneral 官方 .NET SDK

[NuGet AlibabaCloud.SDK.Alimt20181012](https://www.nuget.org/packages/AlibabaCloud.SDK.Alimt20181012/) 当前列表最新版 **2.4.2**，发布 2025-12-03，目标 .NET Standard 2.0 / .NET Framework 4.5。它有持续版本记录，不能将“近期维护”扩大为厂商维护 SLA。依赖 OpenApiClient、OpenApiUtil、EndpointUtil、TeaUtil、TeaXML、TeaFileform、Aliyun.Credentials、Tea；应固定版本并生成完整依赖清单。官方旧 [.NET 示例](https://www.alibabacloud.com/help/en/machine-translation/developer-reference/dotnet-sdk)仍展示 `Aliyun.Acs` 旧包，不混用旧/新命名空间。

[官方生成 Client.cs](https://raw.githubusercontent.com/aliyun/alibabacloud-csharp-sdk/master/alimt-20181012/core/Client.cs) 已核查：公开方法为 `TranslateGeneral(request)`、`TranslateGeneralAsync(request)`、`TranslateGeneralWithOptions(request, runtime)`、`TranslateGeneralWithOptionsAsync(request, runtime)`。命名空间 `AlibabaCloud.SDK.Alimt20181012`，请求模型在 `.Models`。它发送 HTTPS POST `/`，RPC Action=TranslateGeneral、Version=2018-10-12、AuthType=AK、formData，签名委托官方 OpenApiClient，勿自行手写不完整签名。Config 接收 AccessKeyId/AccessKeySecret；应用显式传入用户配置，禁止为了探测而读取 SDK 默认凭据链。

源码地区映射：`cn-hangzhou` → `mt.cn-hangzhou.aliyuncs.com`，`ap-southeast-1` → `mt.ap-southeast-1.aliyuncs.com`；显式 Endpoint 优先。推荐杭州默认，地区/端点一起保存，不凭产品名猜测 `alimt.*` 域名。参数 `FormatType="text"`、`Scene="general"`、SourceLanguage、TargetLanguage、SourceText；Context 可选。[通用调用指南](https://help.aliyun.com/zh/machine-translation/developer-reference/api-reference-machine-translation-universal-version-call-guide)说明返回 Code=200、Data.Translated、Data.DetectedLanguage、Message、RequestId。HTTP 成功仍须检查业务 Code/空 Data，勿把错误文本当译文。

取消限制：核查源码的上述异步方法没有 CancellationToken 参数。`WaitAsync(token)`仅取消等待，不能证明传输终止。建议有限并发、有限底层超时、取消后丢弃旧 generation 的结果并观察底层任务异常；UI 必须承认在途云请求可能完成/计费。如验收要求物理中止网络，需进一步验证官方传输扩展或 SDK 后续支持，不能声称已经实现。

[官方 RuntimeOptions 源码](https://raw.githubusercontent.com/aliyun/tea-util/master/csharp/core/Models/RuntimeOptions.cs)提供 Autoretry、MaxAttempts、ReadTimeout、ConnectTimeout、BackoffPolicy/Period；建议显式 Autoretry=false、MaxAttempts=1，由统一层处理有界重试，且不关闭证书验证。具体超时单位/底层默认值本轮未核查，实施前核对 OpenApiClient/Tea 相应版本，不凭属性名假设单位。SDK 异常与业务响应分层；[官方 FAQ](https://help.aliyun.com/zh/machine-translation/support/faq-1)确认 10001 超时可重试、10004 参数不正确不可盲重试。其余代码保留原始脱敏 Code/Message/RequestId，未知代码不默认瞬态。

许可：本轮 NuGet 正文未提供可确认的具体许可文本；尝试官方根 LICENSE/License.txt 地址未成功。**具体 2.4.2 nupkg 许可与各传递依赖许可待恢复包时核查**，不要拿 Java/Go SDK 的 Apache-2.0 代替 .NET 包证据。百度直接 HTTP 和兼容预设无需新增 SDK 依赖；服务使用仍受各官方服务协议约束。

## 腾讯 TokenHub：当前服务，不沿用旧 TMT 结论

[官方 API 使用说明](https://cloud.tencent.com/document/product/1823/130078)更新 2026-10-13 15:50:30：广州主域 `https://tokenhub.tencentmaas.com`，新加坡 `https://tokenhub-intl.tencentmaas.com`；备用域对应 `.cn`。Bearer API Key 鉴权；`GET /v1/models` 返回 id/name/status，状态包括 online/pre-offline。文档当前列出 Hy、DeepSeek、GLM、Kimi、MiniMax、Qwen、MiMo 等系列及翻译专用 Hy-MT2 系列；这是文档目录证据，不是账户实测可调用目录。不固化任何模型 ID；用户手填，或显式授权按钮刷新并缓存。

[语言模型调用概览](https://cloud.tencent.com/document/product/1823/130079)确认 `/v1/chat/completions`、广州 base `https://tokenhub.tencentmaas.com/v1`，新加坡对应 intl；密钥跨站不通。所有列出的语言模型支持 Chat Completions，Responses/Anthropic 因模型而异；模型须先在控制台开通，未开通可返回 402。可复用 OpenAICompatible provider，新增 TencentTokenHub 预设，不新增旧 TMT 签名客户端。翻译语言范围随所选模型变化；本轮没有逐模型 27 语言保证表，故标记模型语言能力未验证，不能宣称全部支持。

[错误码说明](https://cloud.tencent.com/document/product/1823/131595)提供 HTTP 与业务码；实施至少区分 400 参数、401 鉴权、402 开通/额度、429 限流、5xx 服务故障。401/402 不自动换收费备用；429 按 Retry-After/有限退避。API Key 创建/服务开通由用户在[官方控制台](https://console.cloud.tencent.com/tokenhub)完成；本轮没有登录，入口可达性/账户开放范围/赠额均未知，不能把公开新文档说成新用户真实开通成功。

## OpenAI 兼容预设与链接

| 预设 | 保存的 API base | 文档 / 申请 | 计费说明 |
|---|---|---|---|
| SiliconFlow | https://api.siliconflow.cn/v1 | [快速上手](https://api-docs.siliconflow.cn/docs/userguide/quickstart)，[密钥入口](https://cloud.siliconflow.cn/account/ak) | 平台控制台按所选模型确认；本轮未核查独立计费页 |
| DeepSeek | https://api.deepseek.com | [官方首次调用](https://api-docs.deepseek.com/guides/codex)，[平台](https://platform.deepseek.com/) | [官方价格](https://api-docs.deepseek.com/quick_start/pricing)；价格未摘录核验 |
| Groq | https://api.groq.com/openai/v1 | [兼容说明](https://console.groq.com/docs/openai)，[控制台](https://console.groq.com/) | [Billing FAQ](https://console.groq.com/docs/billing-faqs)；套餐未摘录核验 |
| Tencent TokenHub | https://tokenhub.tencentmaas.com/v1 | 上述官方 API/控制台 | 账户/模型开通与额度在控制台确认 |
| 自托管/其他 | 用户输入 | 服务管理员提供 | 用户确认 |

以上 base 来自各自官方示例，核查 2026-10-15；文档未显示更新日期者不补造。URL 组合需按预设明确规则，不能给 DeepSeek 无条件补 `/v1`，不能给已有 `/v1` 再追加 `/v1`。预设字段为 base、API key、model，model 默认留空。连接测试与一次翻译测试分开，经用户按钮授权才请求；“已填写”“鉴权成功”“翻译成功”分别展示。SiliconFlow [模型接口](https://api-docs.siliconflow.cn/docs/api/models-get)支持 `sub_type=chat`，不能把 audio/image 模型当文本模型。Groq 官方列出不支持 logprobs/logit_bias/top_logprobs/messages.name，n 若传需为 1；通用翻译请求宜只发 model/messages/stream=false，其他参数逐预设启用。

## 无真实密钥的测试夹具建议（合成，未执行）

```text
Baidu POST /api/trans/vip/translate
q=Hello&from=en&to=zh&appid=fixture-app&salt=fixture-salt&sign=<MD5 computed using fixture-secret>
Expected JSON: {"from":"en","to":"zh","trans_result":[{"src":"Hello","dst":"你好"}]}
Error JSON: {"error_code":"54001","error_msg":"Invalid Sign"}

Aliyun SDK request:
{FormatType:"text",Scene:"general",SourceLanguage:"ja",TargetLanguage:"zh",SourceText:"こんにちは"}
Synthetic response:
{"Code":200,"Message":"success","RequestId":"fixture-id","Data":{"Translated":"你好","DetectedLanguage":"ja"}}
Synthetic error: {"Code":10004,"Message":"fixture parameter error","RequestId":"fixture-id"}

OpenAI-compatible POST <preset-base>/chat/completions
Authorization: Bearer <fixture-key>
{"model":"<user-selected-model>","stream":false,"messages":[{"role":"system","content":"Translate from Japanese to Simplified Chinese. Return only the translation."},{"role":"user","content":"こんにちは"}]}
Synthetic response: {"choices":[{"message":{"role":"assistant","content":"你好"}}]}
```

请求参数测试应覆盖 en→zh、zh→en、ja→zh、zh→ja、de→en、fr→zh、en→es，百度特别验证 jp/fra/spa；繁体分别 cht/zh-tw；source auto 与 target auto 拒绝；缺少密钥/未知语言/账户受限语言不得发请求。签名测试用纯夹具密钥验证 URL 编码前签名以及换行/非 ASCII。全部建议是待实现测试，本文未运行代码。

未知项汇总：百度外部账户权益及精确限额、阿里包许可与底层物理取消/超时单位、各账户地区开通/权限、TokenHub 新用户账户可用性、逐 LLM 模型语言覆盖及全部计费页面内容。任何未知项不得用“全部语言支持”或“云验证通过”填补。
