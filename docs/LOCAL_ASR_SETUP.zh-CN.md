# antiDBT 本地英文语音识别配置指南

适用版本：集成 sherpa-onnx Streaming Zipformer 英文模型的 antiDBT Windows x64 版本（v0.5.5 及以上）。

本指南说明本地语音识别（ASR）的配置与使用。本地 ASR 负责将系统播放的英文语音转录为英文文本；若需呈现中文字幕，需另行配置机器翻译（MT）服务。

---

## 方案 A：使用内置模型的完整发布包（推荐）

1. 从官方 GitHub Releases 页面下载 `antiDBT-v0.5.5-win-x64-full.zip`。
2. 解压整个压缩包到本地目录（如 `D:\Tools\antiDBT\`）。请勿直接在压缩包内双击运行。
3. 打开解压后的 `antiDBT` 文件夹，运行 `antiDBT.exe`。
4. 打开设置面板：
   - **语音识别 Provider**：选择 `本地流式识别 (sherpa-onnx)`（`SherpaOnnx`）。
   - **模型目录**：保持默认 `models/sherpa-onnx` 即可，程序会自动扫描程序同级 `models` 目录下的 Zipformer 模型子目录。也可以显式填写相对路径 `models/sherpa-onnx-streaming-zipformer-en-2023-06-21`。
   - **识别语言**：填写 `en`（或留空使用模型默认）。
5. 翻译配置：
   - **仅需英文字幕**：源语言选 `English`（`en`），目标语言选 `English`（`en`），此时程序工作在直通模式，无需配置任何翻译服务或 API 密钥。
   - **需要中文翻译**：目标语言选择 `简体中文`（`zh`），并在翻译服务中配置 Groq、DeepL 等翻译 Provider（配置方法见下文）。
6. 保存设置后，点击主界面“开始”按钮，播放英文视频或音频测试识别。

完整发布包已预先内置运行所需的 .NET 10 运行时、Sherpa-ONNX 原生库及量化模型，无需用户安装 Python、Go、CUDA 或额外开发工具。

---

## 完整版目录结构

```text
antiDBT/
├── antiDBT.exe
├── sherpa-onnx-c-api.dll
├── onnxruntime.dll
├── Microsoft.CognitiveServices.Speech.core.dll
├── docs/
│   └── LOCAL_ASR_SETUP.zh-CN.md
├── licenses/
│   ├── upstream/
│   └── packages/
├── models/
│   └── sherpa-onnx-streaming-zipformer-en-2023-06-21/
│       ├── encoder-epoch-99-avg-1.int8.onnx
│       ├── decoder-epoch-99-avg-1.int8.onnx
│       ├── joiner-epoch-99-avg-1.int8.onnx
│       ├── tokens.txt
│       └── README.md
├── README.md
├── THIRD-PARTY-NOTICES.md
└── LICENSE
```

注意：请勿随意重命名或单独移动 `models/sherpa-onnx-streaming-zipformer-en-2023-06-21` 目录下的 INT8 模型文件及 `tokens.txt`。

---

## 方案 B：手动下载与安装模型

若使用未携带模型的轻量版本，可自行下载官方模型文件手动放入：

1. 从 sherpa-onnx 官方发布仓库下载模型压缩包：
   https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-en-2023-06-21.tar.bz2
2. 使用解压工具（如 7-Zip）解压 `.bz2` 得到 `.tar`，再次解压得到文件夹 `sherpa-onnx-streaming-zipformer-en-2023-06-21`。
3. 将该文件夹完整放置于 `antiDBT.exe` 同级的 `models/` 目录下。
4. 确认目录及文件结构如下：
   - `antiDBT\models\sherpa-onnx-streaming-zipformer-en-2023-06-21\encoder-epoch-99-avg-1.int8.onnx`
   - `antiDBT\models\sherpa-onnx-streaming-zipformer-en-2023-06-21\decoder-epoch-99-avg-1.int8.onnx`
   - `antiDBT\models\sherpa-onnx-streaming-zipformer-en-2023-06-21\joiner-epoch-99-avg-1.int8.onnx`
   - `antiDBT\models\sherpa-onnx-streaming-zipformer-en-2023-06-21\tokens.txt`
5. 启动 antiDBT，语音识别选择 `本地流式识别 (sherpa-onnx)`，程序将自动识别并加载该模型。

---

## 本地 ASR 与云端翻译的区别

- **本地语音识别（ASR）**：使用本地 CPU 进行深度学习推理（Sherpa-ONNX Zipformer），全程在本地设备运行，不产生网络请求，不消耗云端 API 额度，无需配置 ASR API Key。
- **云端机器翻译（MT）**：当需要将识别出的英文翻译为中文或其他语言时，字幕系统将文本通过 HTTPS 发送至用户配置的翻译服务（如 DeepL、Groq、火山等）。翻译服务需要用户自行配置对应的 API Key。若需纯离线环境，可配置本地部署的 Ollama / OpenAI 兼容端点（如 `http://127.0.0.1:11434`）。

---

## 常用翻译服务配置方法

### 1. Groq (推荐，低延迟大语言模型翻译)
1. 访问 https://console.groq.com 注册账号并创建 API Key。
2. 在 antiDBT 设置面板中，翻译 Provider 选择 `OpenAiCompatible`（或 `Groq`）。
3. 端点填写：`https://api.groq.com/openai/v1/chat/completions`。
4. 模型填写：`llama-3.3-70b-versatile` 或 `llama-3.1-8b-instant`。
5. 填入 API Key，源语言设为 `en`，目标语言设为 `zh`。

### 2. DeepL (高质量机器翻译)
1. 访问 https://www.deepl.com 注册开发者账号并获取 API 密钥（以 `:fx` 结尾为免费版，无后缀为付费版）。
2. 在 antiDBT 设置面板中，翻译 Provider 选择 `DeepL`。
3. 填入 API Key（程序自动根据密钥后缀匹配免费版或专业版端点）。
4. 源语言设为 `en`，目标语言设为 `zh`。

---

## 常见问题与排障

**1. 提示“本地语音识别模型目录不存在”？**
检查 `antiDBT.exe` 所在目录下是否存在 `models/sherpa-onnx-streaming-zipformer-en-2023-06-21` 文件夹，并确认其中包含 `tokens.txt` 以及 `.onnx` 模型文件。建议使用相对路径，避免写死绝对盘符。

**2. 只有英文原文，没有中文翻译？**
ASR 与 MT 属于两条独立管线。出现英文原文证明本地语音识别工作正常。无中文翻译通常因翻译服务配置未完成、API Key 无效、网络受阻或服务商额度耗尽导致。请检查设置中的翻译配置及错误状态提示。

**3. 该模型支持中文语音识别吗？**
不支持。`sherpa-onnx-streaming-zipformer-en-2023-06-21` 是专为英文训练的流式识别模型，仅支持英文输入。

**4. 无法捕获系统播放声音？**
检查 Windows 声音设置，确认播放软件正在通过“系统默认音频设备”输出。若播放软件启用了 WASAPI 独占模式（Exclusive Mode），其他程序无法进行回环采集，需切换为共享模式。

**5. 中文目录与便携性支持**
程序通过 Windows 应用程序清单启用了进程级 UTF-8 代码页支持，能够直接在包含中文字符的路径（如 `D:\常用软件\双语字幕\antiDBT\`）下稳定加载 Sherpa-ONNX 原生模型与运行库。

---

## 许可证与来源信息

- **模型名称**：sherpa-onnx-streaming-zipformer-en-2023-06-21
- **上游发布**：https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models
- **模型文档**：https://k2-fsa.github.io/sherpa/onnx/pretrained_models/online-transducer/zipformer-transducer-models.html
- **上游代码与权重**：icefall pruned-transducer-stateless7-streaming, Apache-2.0 许可证。
- 详细归属与文件哈希见 `licenses/upstream/ZIPFORMER-2023-06-21-SOURCES.json` 与 `THIRD-PARTY-NOTICES.md`。
