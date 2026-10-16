# antiDBT · 独立实时双语字幕客户端

<p align="center">
  <b>极简 · 低延迟 · 高度自定义的 Windows 桌面实时双语字幕工具</b><br>
  <sub>独立开源复刻项目 · 不依赖任何特定厂商私有接口 · 支持多 Provider 自由组合</sub>
</p>

<p align="center">
  <a href="README.md"><b>简体中文</b></a> |
  <a href="README.zh-TW.md"><b>繁體中文</b></a> |
  <a href="README.en.md"><b>English</b></a> |
  <a href="README.ja.md"><b>日本語</b></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-0078D6.svg?logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4.svg?logo=dotnet" alt=".NET 9.0">
  <img src="https://img.shields.io/badge/Release-v0.5.3--beta-green.svg" alt="Release">
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License">
  <img src="https://img.shields.io/badge/CI-Passing-brightgreen.svg?logo=githubactions" alt="CI">
</p>

---

## 📖 项目简介

**antiDBT** 是一个专为 Windows 10/11 设计与实现的独立桌面实时双语字幕悬浮窗客户端。通过 Windows WASAPI 系统音频回环捕获电脑正在播放的声音，结合低延迟语音识别（ASR）与机器翻译（MT）服务，在屏幕上呈现纯净、低延迟、自然排版的双语对照字幕。

本项目采用纯净室独立开发路线，**不复用豆包官方的专有代码、非公开协议或凭据**，专为生肉视频观看、外语直播翻译、跨国远程会议、网课学习等高频视听场景打造。

---

## ✨ 核心特性

### 1. 🛡️ 可靠性与稳定性硬化 (Phase 5.3 专项演进)
- **停止尾句零丢失**：点击“停止”时进入有界排空期（Bounded Drain），已入队的音频片段与延迟 Final 转写及对应翻译完整呈现后再安全关闭，彻底杜绝丢末尾句。
- **音频时效性有界缓冲与超期丢弃**：引入名义容量为 50 个音频块（每块 6,400 字节，在 16kHz/16-bit 单声道下对应 200ms 音频，队列名义容量约 10 秒）的有界缓冲区与 2.0 秒音频块过期清理阈值；发生网络瞬态拥堵导致积压时，超期音频块自动丢弃并记录断续统计，杜绝延迟无限滚雪球。
- **幽灵译文清除与草稿兼容校验**：当 ASR 将原文修正为空或发生语义颠覆时，强制清空失效的历史译文；建立严格的前缀扩展与修订版本隔离校验，迟到旧结果无法污染当前字幕。
- **韩文词间空格精准保护**：彻底修复多音节韩文片段在流式流转中词间空格被误删的问题，韩语听译语义清晰连贯。
- **连续稳定期重试预算与限流退避**：单次发送音频成功不再清空重试预算，只有建立连接后持续健康收发才重置预算；自动识别 HTTP 429 并严格遵循服务商返回的 `Retry-After` 冷却期，杜绝无效重试与多余扣费。
- **细分状态指示与静音诊断**：
  - 🟢 聆听中 / 🟡 连接中 / 🔴 异常 / ⚪ 就绪。
  - 翻译服务异常但识别正常时展示琥珀色指示（#FFB300），保持“停止”按钮语义正常可用。
  - 音频连续静音超过 3.0s 明确诊断为“已连接但无音频信号”，绝不将设备静音误判为网络断线。

### 2. 🎨 真正的可调半透明悬浮窗 (Phase 5.2 核心特性)
- **0%～100% 背景不透明度自由调节**：在“设置 → 外观”中通过滑块随时调整，免重启无感知实时生效。
- **背景与文字彻底解耦**：原文、译文、按钮及图标始终保持 100% 实体高对比度清晰渲染，告别全局模糊。
- **真实透视与防点击穿透**：12px 现代平滑圆角，透过背景看清后方视频；即便调至 0% 透明度，底层鼠标命中测试依然有效，绝不意外穿透误触后方窗口。

### 3. 📐 弹性视口与空间全高自适应
- **纵向全高动态填充**：窗口向下拉长时，历史字幕区域全高自动填充，大屏可浏览更多历史上下文，消除无意义留白死区。
- **横向自然折行重排**：横向拖动缩放时，原文与译文依据最新视口宽度自然折行，文字绝不溢出或裁切。
- **「↓ 回到底部」智能锚定**：浮动悬徽精准锚定于字幕视口右下方（Overlay），不再随窗口高度漂移至窗口中央。

### 4. 📜 丝滑像素级滚动与智能跟随
- **像素级滚动平滑阻尼**：支持鼠标滚轮与笔记本触控板微小 Delta 丝滑位移，消除跳页顿挫感。
- **智能跟随锁**：向上滚动翻阅历史时**自动暂停滚动跟随**；回到最底或点击「↓ 回到底部」时**自动恢复跟随**。新字幕到达或窗口缩放时**绝不强拉底部**，保障回看体验不受打扰。

### 5. ⚙️ 非侵入式设置面板与端点安全
- **无感设置体验**：打开或取消设置面板**不停止音频采集、不清空历史字幕**；仅改变外观参数原地生效；仅在修改核心采集参数时执行单次受控平滑重启。
- **本地大模型安全接入**：在显式启用本地回环允许时，支持直接接入本地部署的 Ollama、LocalAI、LM Studio 或 OpenAI 兼容端点（`http://127.0.0.1` / `localhost`）；针对外部云端端点严格要求 HTTPS 并拒绝携带敏感 userinfo。

### 6. 📊 细分时延度量模型 (Telemetry)
- 内置轻量级线程安全时延度量收集器，细分记录 `QueueWaitMs`（队列等待）、`AsrServiceMs`（语音识别耗时）、`MtDebounceWaitMs`（去抖等待）、`MtServiceMs`（翻译耗时）及 `UiApplyMs`（界面呈现耗时）。
- 一键脱敏导出 JSON 诊断样例，默认严禁包含任何密钥、Token、请求签名、原文文本、译文或原始音频。

### 7. 🔌 模块化 Provider 架构
- **语音识别（ASR）**：支持 Groq Whisper（超低延迟）、火山语音 ASR 2.0、Azure Speech SDK、Google Cloud Speech V2 等。
- **机器翻译（MT）**：支持 DeepL API、OpenAI 兼容大模型（Ollama、DeepSeek、GPT-4o、Claude 等）、Groq LLM、火山 MT、Azure Translator、Google Basic、百度翻译、阿里云翻译等。

### 8. 🔒 本地凭据安全
- 所有 API Key、Secret、密码均经由 **Windows DPAPI（Data Protection API）** 采用当前用户专属加密密钥存储于本地 `data/settings.json`，绝不上云、绝不向第三方泄露。

---

## 🖥️ 运行环境要求

- **操作系统**：Windows 10 / Windows 11 (x64)
- **音频架构**：支持 WASAPI 回环采集的声卡/音频设备（系统默认扬声器/耳机）
- **便携包运行时**：发布包内置独立自包含 .NET 9 运行时，**终端用户无需单独安装 .NET SDK 或运行库**。.NET 10 LTS 处于独立分支评估与适配阶段。

---

## 🚀 快速上手

### 1. 下载与运行
1. 前往 [GitHub Releases](https://github.com/Morchyee/AntiDBT/releases) 下载最新版本的 `antiDBT-v0.5.3-beta-win-x64.zip`。
2. 解压压缩包到本地任意目录（例如 `D:\Tools\antiDBT\`）。
3. 双击解压目录中的 `antiDBT.exe` 即可启动。

### 2. 配置翻译与语音识别服务
1. 点击窗口右上角的设置图标 **⚙**。
2. **语音识别**：选择所希望使用的 Provider（推荐 `GroqWhisper` 或 `VolcengineAsr2`），输入 API Key。
3. **翻译服务**：选择翻译 Provider（推荐 `DeepL` 或 `OpenAiCompatible` 并配合高速模型），填入 API Key 与端点。
4. **外观设置**：可切换“暗色 / 浅色 / 跟随系统”主题，并拖动**“背景不透明度”**滑块调整悬浮窗透明度（建议 70%～85%）。
5. 点击“保存设置”。

### 3. 开始使用
- 点击工具栏的 **“开始”** 按钮。
- 电脑播放任何视频、直播、播客、会议或网课音频，主窗口即可实时浮现双语字幕。
- 想要体验排版而暂不消耗 API 额度？在主窗口右键选择**“本地演示”**，将以本地合成事件测试排版与滚动。

---

## 🛠️ 源码构建与测试

本项目采用 C# 13 与 .NET 9 WPF 编写，已配置 `global.json` 锁定 SDK 9.0.319 以实现确定性构建。

### 依赖环境
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (9.0.319 推荐)
- Windows 10/11 x64
- PowerShell 7 (`pwsh`)

### 构建命令
```powershell
# 克隆仓库
git clone https://github.com/Morchyee/AntiDBT.git
cd AntiDBT

# 编译整个解决方案 (Release)
dotnet build AntiDbt.sln -c Release

# 运行全量 12 项自动化验证套件 (涵盖单元测试、协议契约、WPF UI自动化、性能基准)
pwsh -File scripts/verify-all.ps1 -SkipRestore

# 打包构建 self-contained 单文件自包含便携包
pwsh -File scripts/build-candidate-package.ps1 -Version "0.5.3-beta"
```

---

## ⚠️ 当前已知限制与测试边界说明

1. **云端凭据依赖**：实际语音识别与机器翻译需要用户自行在对应厂商平台申请 API Key（如 Groq、DeepL、火山引擎、OpenAI 等）。
2. **测试与真实服务边界**：本地全量回归测试与 CI 烟测通过（100% PASS）验证了核心状态机、本地音频回环、UI 契约及错误恢复逻辑的正确性，**不等于各第三方云端服务商的实际在线可用性、配额健康度或全天候长期不间断运行保证**。实际在线体验依赖于所选云端服务的网络环境与厂商服务端状态。
3. **WASAPI 独占音频限制**：若播放软件启用了 WASAPI 独占输出模式（Exclusive Mode），系统回环录音可能无法采集，请切换为共享模式（Shared Mode）。
4. **防火墙配置**：首次运行如有 Windows Defender 防火墙网络提示，请点击允许，以便向所配置的 API 发起网络请求。

---

## 🛡️ 隐私与数据安全说明

- **零数据上报与遥测**：antiDBT 没有设置任何专有的中心服务器，不包含任何数据埋点、用户跟踪或后台遥测。
- **纯点对点请求**：采集到的音频片段和识别文本仅直接发送至**用户在设置中显式配置的第三方 API 端点**。
- **本地凭据加密**：所有敏感参数通过 Windows DPAPI 绑定当前登录用户的 Windows 账号加密存储于本地，即使复制了 `settings.json` 也无法在其他机器或用户下解密。

---

## ⚖️ 免责与权利声明 (Disclaimer)

1. **独立开源声明**：**antiDBT** 是一个独立开发的技术研究与辅助工具开源项目，并非北京字节跳动科技有限公司或“豆包（Doubao）”官方产品，与上述实体无任何隶属、合作、关联或背书关系。
2. **纯净实现**：本项目所有业务代码、界面布局、数据流管线均为独立从零编写，未提取或包含任何闭源私有二进制库、专有资源文件或私有凭据。
3. **使用规范**：用户使用本软件调用第三方 API 时产生的网络流量、费用及对各服务商服务条款（ToS）的遵守，由使用者自行承担。

---

## 📄 开源许可证

本项目源码基于 [MIT License](LICENSE) 协议开源。第三方开源依赖与声明请参阅 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
