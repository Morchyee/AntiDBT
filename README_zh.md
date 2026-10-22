# antiDBT - 独立实时双语字幕客户端

<p align="center">
  <b>专为 Windows 10/11 设计的独立桌面实时双语字幕工具</b><br>
  <sub>开源实时字幕客户端 · 支持本地流式语音识别与多 Provider 机器翻译组合</sub>
</p>

<p align="center">
  <a href="README.md"><b>简体中文</b></a> |
  <a href="README.zh-TW.md"><b>繁體中文</b></a> |
  <a href="README.en.md"><b>English</b></a> |
  <a href="README.ja.md"><b>日本語</b></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-0078D6.svg?logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4.svg?logo=dotnet" alt=".NET 10.0">
  <img src="https://img.shields.io/badge/Release-v0.5.5-green.svg" alt="Release">
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License">
  <img src="https://img.shields.io/badge/CI-Passing-brightgreen.svg?logo=githubactions" alt="CI">
</p>

---

## 项目简介

antiDBT 是一个专为 Windows 10/11 设计的独立桌面实时双语字幕客户端。通过 Windows WASAPI 系统音频回环捕获系统当前播放的声音，结合低延迟语音识别（ASR）与机器翻译（MT）服务，在桌面呈现低延迟、排版自然的双语对照字幕。

软件适用于外语视频播放、技术演讲、远程会议、网课及外语直播等视听场景。

---

## 核心特性

### 1. 字幕状态机与语义排序演进（Phase 5.5.5）
- **动态时间线保序**：针对网络抖动或异步请求导致的到达乱序问题，采用基于音频时间锚点的动态排序机制，消除字幕跳变与闪烁。
- **粘性翻译上下文（Sticky Translation）**：在语音识别流式修正期间，平滑保留前序句子的有效译文上下文，避免在新译文到达前界面闪烁或出现译文短暂空白。
- **分组修订一致性隔离**：当语音识别前缀发生语义合并、拆分或修正时，精确回收过期翻译请求，确保展示视口与底层数据状态严格同步。
- **智能分句与段落衔接**：结合声学静音阈值与标点断句算法，对连续语音自然聚合成段，长停顿时平滑划分段落。

### 2. 可靠性与稳定性硬化
- **停止尾句完整排空**：点击停止后进入有界排空阶段（Bounded Drain），已入队的音频片段与对应的终态转写及翻译完整呈现后再安全退出，避免截断尾句。
- **有时效的有界音频缓冲**：设置有界缓冲队列与超时丢弃机制，在网络发生严重拥堵时主动丢弃超期音频块，避免延迟持续滚雪球。
- **自适应重试与退避**：精准识别 HTTP 429 限流并遵循服务商返回的 `Retry-After` 头，避免无效重复请求与额度消耗。

### 3. 可调半透明悬浮窗
- **0%～100% 背景不透明度调节**：在设置中实时滑动调节背景透明度，立即生效。
- **背景与文字独立渲染**：即便背景完全透明，字幕文字依然保持高对比度实体渲染，清晰易读。
- **透视与防穿透保护**：支持透过半透明背景看清后方视频画面；窗口区域保持正常鼠标事件响应，避免意外穿透点击后方窗口。

### 4. 视口排版与平滑滚动
- **视口全高自适应**：拉长窗口时历史字幕区域自动填充可用空间，根据宽度自然折行，文字不溢出、不裁切。
- **像素级平滑滚动**：支持滚轮与触摸板平滑位移。
- **智能跟随锁**：向上滚动浏览历史时自动暂停滚动跟随；滚动到底部或点击底部浮标时恢复自动跟随，新字幕到达时不强行打断用户回看。

### 5. 模块化 Provider 架构与本地凭据安全
- **语音识别（ASR）**：支持内置离线流式识别（Sherpa-ONNX Zipformer）、Groq Whisper、火山引擎语音 ASR 2.0、Azure Speech SDK 等。
- **机器翻译（MT）**：支持 DeepL API、OpenAI 兼容端点（Groq、Ollama、DeepSeek、GPT 等）、火山引擎翻译、Azure Translator、Google 翻译等。
- **本地 DPAPI 加密存储**：所有 API Key 与敏感配置均经由 Windows DPAPI 基于当前用户账户加密存储于本地 `data/settings.json`，不上传云端。

---

## 本地英文语音识别（Sherpa-ONNX Zipformer）

正式完整版（`antiDBT-v0.5.5-win-x64-full.zip`）内置了官方验证的默认本地英文识别模型：**`sherpa-onnx-streaming-zipformer-en-2023-06-21`**（INT8 量化）。

### 优势与局限
- **优势**：
  - 识别准确率相较旧版 20M 模型有显著提升，能够完整转录复杂句子与日常语汇。
  - 维持毫秒级流式初态预览响应速度。
  - 完全在本地 CPU 上运行，内存占用平稳（实际运行约为 260 MB 级别），无需配置 ASR API Key，无云端调用费用。
- **局限**：
  - 本模型为英文专用流式识别模型，仅支持英文输入，不支持中文语音识别。
  - 重度依赖音频清晰度；复杂背景噪音、重叠人声或强口音场景下准确率会有所下降。
  - 中文翻译属于独立处理阶段，仍需配合配置翻译服务（如 Groq、DeepL 或本地 Ollama）。

### 本地 ASR 与云端翻译的区别
antiDBT 将处理流程严格划分为独立的两个阶段：
1. **ASR 阶段（音频 -> 文本）**：若选用本地 Sherpa-ONNX，音频完全在本地 CPU 推理，零网络流量，零 API Key 依赖。
2. **MT 阶段（原文 -> 译文）**：
   - 若源语言与目标语言均设为 `en`（直通模式），系统不调用任何翻译服务，实现 100% 离线使用。
   - 若目标语言设为 `zh`，系统将识别出的文本通过 HTTPS 请求发送至配置的翻译服务。

### 官方模型来源与手动安装
若使用未携带模型的轻量版本，可手动配置模型：
- **官方下载地址**：https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-en-2023-06-21.tar.bz2
- **解压路径**：解压后将文件夹置于 `antiDBT\models\sherpa-onnx-streaming-zipformer-en-2023-06-21\`。
- **关键文件清单**：
  - `encoder-epoch-99-avg-1.int8.onnx`
  - `decoder-epoch-99-avg-1.int8.onnx`
  - `joiner-epoch-99-avg-1.int8.onnx`
  - `tokens.txt`
详细操作可参阅 [本地语音识别配置指南](docs/LOCAL_ASR_SETUP.zh-CN.md)。

---

## 常用翻译服务配置

点击主窗口右上角设置图标（⚙）打开设置面板进行配置：

### 1. Groq（低延迟大模型翻译）
1. 前往 https://console.groq.com 获取 API Key。
2. 翻译服务选择 `OpenAiCompatible`。
3. 接口端点填写：`https://api.groq.com/openai/v1/chat/completions`。
4. 模型名称填写：`llama-3.3-70b-versatile` 或 `llama-3.1-8b-instant`。
5. 填入 API Key，源语言设为 `en`，目标语言设为 `zh`，保存即可。

### 2. DeepL（专业机器翻译）
1. 前往 https://www.deepl.com 注册获取 API Key（支持 DeepL API Free 与 Pro）。
2. 翻译服务选择 `DeepL`。
3. 填入 API Key（程序根据密钥末尾 `:fx` 自动切换免费版与专业版端点）。
4. 源语言设为 `en`，目标语言设为 `zh`，保存即可。

### 3. 本地大模型（Ollama 等）
1. 启动本地 Ollama 服务（例如运行 `ollama run qwen2.5:7b`）。
2. 在设置中开启“允许本地回环”。
3. 翻译服务选择 `OpenAiCompatible`，端点填写 `http://127.0.0.1:11434/v1/chat/completions`，填入对应模型名即可完全离线翻译。

---

## 系统要求

- **操作系统**：Windows 10 64 位（版本 1903 / 内部版本 18362 及以上）或 Windows 11 64 位。
- **音频架构**：支持 WASAPI 回环捕获的声卡或音频设备（需工作在共享模式 Shared Mode）。
- **运行依赖**：发布包内置独立自包含的 .NET 10 x64 运行时与 Sherpa-ONNX 原生动态库。**终端用户无需安装 .NET SDK、.NET Runtime、Python、Go、CUDA 或 C++ 编译环境。**

---

## 下载、安装与升级

### 首次下载与运行
1. 前往 [GitHub Releases](https://github.com/Morchyee/AntiDBT/releases) 下载正式稳定版：
   - 完整包：`antiDBT-v0.5.5-win-x64-full.zip`
   - 校验文件：`antiDBT-v0.5.5-win-x64-full.sha256`
2. 使用系统自带解压功能或 7-Zip 将压缩包解压至非系统受限目录（例如 `D:\Tools\antiDBT\`）。
3. 进入解压生成的 `antiDBT` 文件夹，双击 `antiDBT.exe` 启动。
4. 首次启动请进入设置确认音频设备与 Provider 配置，保存后点击“开始”即可。

### 校验文件完整性（PowerShell）
```powershell
Get-FileHash -Path "antiDBT-v0.5.5-win-x64-full.zip" -Algorithm SHA256
```
将计算出的哈希值与发布的 `.sha256` 文件内容核对。

### 软件升级
1. 完全退出正在运行的旧版本 antiDBT。
2. 将新版本 ZIP 解压至新目录。
3. 若需继承历史配置与凭据，可将旧版本目录下的 `data/settings.json` 复制到新版本的 `data/` 目录中（前提为在同一台计算机的同一 Windows 用户下操作，因 DPAPI 绑定用户账号）。请勿在运行状态下原地覆盖文件。

---

## 源码构建与测试

本项目采用 C# 14 与 .NET 10 WPF 构建，使用 `global.json` 锁定 SDK 版本以保证确定性构建。

### 开发依赖
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10/11 x64
- PowerShell

### 构建与测试命令
```powershell
# 克隆仓库
git clone https://github.com/Morchyee/AntiDBT.git
cd AntiDBT/prototype

# Release 编译
dotnet build AntiDbt.sln -c Release

# 运行全套 12 项自动化验证套件
pwsh -File scripts/verify-all.ps1 -SkipRestore

# 构建正式分发安装包
pwsh -File scripts/build-candidate-package.ps1 -Version "0.5.5"
```

---

## 验证结论与运行边界

### 自动化验证与测试基准
- 全套 12 套测试（Core、Windows、CloudSpeech、Providers、Domestic、WPF Desktop Smoke、Diagnostics 等）共计 125 项单元及回归测试 100% 通过。
- 包含针对 Phase 5.5.5 状态机一致性、时间线动态保序、粘性翻译生命周期及高并发请求回归。
- 包含真实 Zipformer INT8 模型加载与音频转录回归，转录准确率与事件流转正常。

### 运行边界与注意事项
- **WASAPI 独占模式限制**：若第三方播放器启用了 WASAPI 独占输出（Exclusive Mode），系统回环录音无法采集该音频流，需将其切换为共享模式（Shared Mode）。
- **云端服务稳定性**：客户端通过了严格的契约校验与错误容灾测试，但第三方云端 API 的可用性、网络连接状况及调用额度由各服务商自身决定。
- **特殊音频设备**：专业音频接口（ASIO 独占驱动）、虚拟跳线软件（如 Voicemeeter 独占总线）可能需要针对性配置回环输入源。

---

## 隐私、许可证与免责声明

- **隐私安全**：antiDBT 没有中央服务器，不收集任何用户使用数据、埋点数据或遥测信息。采集的音频与文本仅在本地处理，或严格根据用户配置直接与第三方 API 端点通信。
- **凭据加密**：所有 API 密钥均通过 Windows DPAPI 本地加密存储，无法在其他用户或机器环境下解密。
- **开源许可证**：antiDBT 源码采用 [MIT 许可证](LICENSE) 发布。
- **第三方组件归属**：
  - Sherpa-ONNX（Apache-2.0）
  - ONNX Runtime（MIT）
  - NAudio（MIT）
  - Microsoft Cognitive Services Speech SDK（商业运行时许可）
  - Google Cloud Speech 客户端（Apache-2.0）
  - Zipformer 模型权重与模型卡采用 Apache-2.0 许可证分发，归属原始训练作者所有。
  - 详细第三方许可清单见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
- **免责声明**：本软件按“现状”（AS IS）提供，不包含任何明示或暗示的商业保证。由于语音环境、口音及机器翻译算法的局限性，转录及翻译结果仅供参考。
