# antiDBT 第三阶段：多 ASR / MT API Provider 扩展

工作目录：`F:\Program\antiDBT`

现有原型：`F:\Program\antiDBT\prototype`

## 一、项目背景

antiDBT 已完成独立实时双语字幕原型，当前默认使用：

- 火山 ASR 2.0
- 豆包机器翻译

现有系统已经具备 Provider 可替换的核心架构。

本轮目标是在保持当前功能完整性和字幕 UI 的基础上，支持更多主流语音识别和翻译服务。

**必须实际开发、集成、测试，不要只生成接口定义、占位类或演示页面。**

开始前阅读：

- `prototype/docs/ARCHITECTURE.md`
- `prototype/docs/COMPLETION-MATRIX.md`
- `prototype/docs/VALIDATION.md`
- `reports/phase2/PROGRESS.md`

先审计现有实现，复用已经存在的能力，不重复造轮子。

## 二、目标架构

将 ASR 与 MT 完全解耦。

标准流程：

WASAPI 系统音频 → 音频预处理/VAD → 可配置 ASR Provider → 原文字幕 → 可配置 MT Provider → 翻译字幕 → UI

要求支持任意有效组合，例如：

- Groq Whisper + DeepL
- Groq Whisper + Google Cloud Translation
- Groq Whisper + Groq LLM
- Azure Speech + Azure Translator
- Azure Speech + DeepL
- Google Cloud STT + Groq LLM
- 火山 ASR 2.0 + DeepL

不能把识别服务和翻译服务强制绑定。

### Provider 接口

复用现有接口，必要时扩展：

- `IASRProvider`
- `ITranslationProvider`
- `IProviderFactory`
- `ProviderCapabilities`
- `ProviderConfiguration`

以上名称只是建议，以现有实际架构为准。

统一提供：

- Provider ID 和显示名称
- 所需认证方式
- 支持语言
- 流式/分段能力声明
- Partial / Final 能力
- 初始化、启动、停止和取消
- 错误处理
- 状态与延迟指标
- 资源释放

分段式 ASR 不得伪装成原生流式识别。

## 三、P0：新增语音识别服务

### 3.1 Groq Whisper

接入 Groq 官方语音转写 API。

优先支持：

- `whisper-large-v3-turbo`
- `whisper-large-v3`

功能：

- 用户自行填写 Groq API Key
- 用户选择模型
- 自动将音频转换为服务支持的输入格式
- VAD 分段后提交转写
- 正确处理音频分段边界和重叠文本
- 返回原语言转写，不使用仅翻译成英语的 audio/translations 端点替代多语言识别
- 尽可能降低分段提交造成的延迟
- 避免短片段重复请求、重复收费
- 支持取消、超时及有限重试

明确标记为分段转写模式。不得宣称它具有原生 Partial 流式能力。

### 3.2 Microsoft Azure AI Speech

使用微软官方 Speech SDK。

支持：

- Speech Key
- Region
- 连续语音识别
- Partial / Final
- 自动标点（服务支持时）
- 连接重建
- 运行中停止
- 设备和网络故障恢复

设置界面可以显示 Azure Speech，并注明属于微软语音服务。

禁止依赖没有正式授权的 Bing 网页接口或非公开认证令牌。

### 3.3 Google Cloud Speech-to-Text

接入官方 Google Cloud Speech-to-Text。

要求：

- 支持配置 Google Cloud Project、认证凭据和区域
- 优先采用适用于实时识别的官方 StreamingRecognize
- 使用官方 gRPC / SDK
- 支持 Partial、Final 和语言配置
- 正确处理流式连接时长限制、断线和重新连接
- 不使用简单轮询 REST API 伪装实时流式识别

Google 凭据需要安全存储，不能打包在程序发行文件内。

### 3.4 保留火山 ASR 2.0

原有火山实现保持可用。

本轮不改动现有协议算法，除非发现影响统一 Provider 接口的明确问题。

## 四、P0：新增翻译服务

### 4.1 DeepL API

接入官方 DeepL 文本翻译接口。

支持：

- DeepL API Key
- 官方支持的 API endpoint
- source_language
- target_language
- 语言映射
- 429 限流处理
- 认证失败、配额不足处理

正确区分 API 服务与普通 DeepL 网页服务。

### 4.2 Microsoft Azure Translator

接入官方 Azure Translator 文本翻译 API。

支持：

- Key
- Region（需要时）
- Endpoint
- 源语言与目标语言
- 正确的认证 Header
- 错误、配额、限流处理

UI 中可以标为 Microsoft Translator（Azure）。

不要使用需要抓取网页内容的非官方 Bing Translator 接口。

### 4.3 Google Cloud Translation

接入官方 Cloud Translation API。

优先支持稳定、适合短文本实时翻译的方式。

支持：

- Cloud Translation Basic / Advanced 中可可靠实现的模式
- 相应认证方式
- 源语言和目标语言
- HTML 实体与纯文本处理
- 配额错误及限流处理

不要混用 Basic API Key 和 Advanced OAuth/服务账户认证。

### 4.4 Groq LLM 翻译

通过 Groq 官方 Chat Completions / Responses 能力实现文本翻译，以当前官方支持情况为准。

功能：

- Groq API Key
- 用户可选择当前可用的文本模型
- 可自定义温度、输出 Token 上限等必要参数
- 默认保持低温度和简短输出
- 不输出解释、Markdown、寒暄或额外内容
- 保留人名、术语、语义、语气
- 允许维护少量短期上下文
- 控制 Token 使用量

模型列表应以官方当前可用模型为准，不把过时的模型 ID 写死。

翻译失败时保留英文原文。

### 4.5 保留豆包机器翻译

原有 Provider 不得因为新增服务而失效。

## 五、P1：自定义 OpenAI 兼容 API

增加可选的通用 LLM 翻译 Provider。

配置：

- Base URL
- API Key
- Model
- 可选自定义翻译提示词
- 请求超时
- 合理的上下文长度
- 可选代理配置（复用现有安全网络配置）

兼容官方 OpenAI 风格的文本生成接口，但不得假设所有兼容服务支持相同能力。

必须区分模型不可用、认证失败、网络失败和响应格式错误。

限制自定义 URL 的风险，禁止意外发送凭据到不同主机，不允许不安全的自动重定向携带 Authorization。

## 六、设置 UI 重构

设置界面必须提供两个独立的选择器。

### 语音识别

选择框：

火山 ASR 2.0 / Groq Whisper / Azure Speech / Google Cloud Speech

根据选中的 Provider 动态显示：

- API Key
- 模型
- Region
- Project
- 其他必要配置

### 翻译服务

选择框：

豆包机器翻译 / DeepL / Microsoft Translator / Google Cloud Translation / Groq LLM / 自定义 OpenAI 兼容

根据 Provider 动态显示对应设置。

要求：

1. API Key 默认隐藏。
2. 支持保存、修改和删除凭据。
3. 沿用已有的安全加密存储方案。
4. 不向日志打印密钥、授权 Header。
5. 支持配置有效性检查。
6. 区分“配置完整”“认证成功”“真实请求成功”。
7. 显示所选 Provider 的识别或翻译能力。
8. 分段 ASR 和流式 ASR 应明确区分。
9. 未配置的 Provider 可显示，但不能假装已连接。
10. 切换 Provider 时正确取消旧任务、释放旧连接。
11. 不因切换服务导致字幕残留、错位或重复。

## 七、实时性能与稳定性

重点确保新增 API 不破坏实时字幕体验。

### ASR

- 流式识别优先处理 Partial。
- Final 必须正确确认语音单元。
- 分段式识别合理结合 VAD。
- 避免音频队列无界增长。
- 防止服务切换后旧结果进入新会话。

### MT

- 新识别结果尽快送入翻译。
- Preview 与 Final 优先级正确。
- 相同文本避免无意义重复请求。
- 不允许旧译文覆盖新译文。
- 保留正确的字幕 ID 关联。
- 慢速提供方不能永久阻塞整个系统。
- 429 遵守 Retry-After 和冷却时间。
- 设置合理的超时、重试次数和并发上限。

不同 Provider 可有不同调度策略，但最终输出必须兼容现有字幕核心。

### 失败处理

支持：

- API Key 错误
- 401 / 403 / 429 / 5xx
- 连接超时
- 网络中断与恢复
- 服务端关闭连接
- 模型不可用
- 语言不支持
- 用户主动停止
- 运行中切换服务

默认不在多个可能收费的服务之间偷偷自动切换。未来如实现自动 Fallback，必须让用户明确启用并设置优先级。

## 八、测试

必须新增对应 Provider 测试。

至少覆盖：

- Provider 初始化
- 错误凭据
- 空响应
- 正常识别和翻译
- Partial / Final
- 音频分段
- HTTP 状态错误
- 超时、取消及重试
- Provider 动态切换
- 旧响应乱序
- API Key 安全存储
- 旧配置迁移
- 不同 ASR + MT 组合
- 字幕显示与 IPC 兼容
- 程序正常退出无残留进程

所有支持的 Provider 都必须有模拟服务测试。

没有真实 API Key 时可以完成协议、解析及故障模拟测试，但不能宣称真实云服务调用已经通过。

测试需要区分：

- 单元测试
- Mock 协议测试
- 本机集成测试
- 真实云服务验证
- 长时稳定性验证

## 九、用户文档

新增 `docs/API_PROVIDERS.md`。

每个 Provider 写明：

- 具体用途
- 官方网站和 API 文档
- 获取 API Key 的方式
- 需要填写的配置项
- 是否支持流式 Partial
- 是否需要付费账户或开通账单
- 常见错误
- 适用场景

不要声称所有 API 都免费。

明确说明 Groq Whisper、Azure Speech、Google Speech 的实时行为差异。

## 十、交付要求

1. 完整代码修改，而非仅接口占位。
2. 保持 antiDBT 项目独立。
3. 不修改或破坏原始豆包安装目录。
4. 保留原有火山和豆包 Provider。
5. 保留字幕 UI、事件关联及 IPC 语义。
6. 不提交或打包真实凭据。
7. 通过核心、协议、Windows 和 UI 回归测试。
8. 更新 COMPLETION-MATRIX、ARCHITECTURE、VALIDATION、PROGRESS。
9. 在独立开发提交中保留清晰的修改记录。
10. 生成新的 Windows x64 便携版到独立目录，不覆盖旧发行包。
11. 可实际启动 EXE，验证 UI 和无凭据启动。
12. 不自动推送、发布或覆盖任何既有交付物。

先审计现有 Provider 能力和配置格式，再按 P0 顺序实现。

如果一次执行无法完成所有 P0 功能，分批实现并逐批测试；每个尚未实现的 Provider 必须明确标记，不能显示成已完成。

**最终目标：用户可以自由选择自己持有的 ASR API 和机器翻译 API，组合运行实时双语字幕，无须依赖火山和豆包账号。**