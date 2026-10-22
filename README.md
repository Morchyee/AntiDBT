# antiDBT - Standalone Real-Time Bilingual Subtitles Client

<p align="center">
  <b>Independent Real-Time Bilingual Subtitle Overlay Tool Designed for Windows 10/11</b><br>
  <sub>Open-Source Live Captioning Client · Supports Local Streaming ASR & Multi-Provider Machine Translation</sub>
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

## Introduction

antiDBT is an independently developed real-time bilingual subtitle floating overlay application designed for Windows 10 and 11. By capturing system audio output using the Windows WASAPI loopback mechanism, combined with low-latency automatic speech recognition (ASR) and machine translation (MT) services, it renders responsive, well-formatted bilingual subtitles on your desktop.

The software is designed for watching foreign videos, technical presentations, international conferences, webinars, and live streams.

---

## Core Features

### 1. Subtitle State Machine & Semantic Ordering (Phase 5.5.5)
- **Dynamic Timeline Ordering**: Solves packet arrival reordering caused by network jitter or asynchronous request execution. Subtitles are dynamically sorted using acoustic time anchors to prevent flickering or jitter.
- **Sticky Translation Lifecycle**: Smoothly retains existing valid translation context during active speech streaming revisions, eliminating blank translation gaps before final results arrive.
- **Group Revision Consistency**: Obsolete or superseded translation requests are cleanly evicted when source speech prefixes undergo semantic merges or splits, ensuring the active viewport remains strictly synchronized with the underlying state.
- **Stable Sentence Segmentation & Paragraph Joining**: Combines acoustic silence detection and punctuation heuristics to join continuous speech into coherent paragraphs while neatly splitting at prolonged pauses.

### 2. Reliability & Stability Hardening
- **Zero Trailing Sentence Loss on Stop**: Stopping capture enters a bounded drain period where queued audio chunks, delayed final transcriptions, and corresponding translations are fully rendered before shutting down.
- **Bounded Buffer with Freshness Expiration**: Employs a bounded audio queue with freshness expiration thresholds to prune outdated audio chunks during network backpressure, preventing latency snowballing.
- **Adaptive Rate-Limit Backoff**: Identifies HTTP 429 status codes and honors server `Retry-After` headers, avoiding wasted retries and excessive quota consumption.

### 3. Adjustable Semi-Transparent Floating Overlay
- **0% to 100% Background Opacity**: Adjust background transparency freely via a slider in the appearance settings with immediate effect.
- **Decoupled Text Rendering**: Subtitle text and interface controls remain fully opaque with sharp contrast regardless of background opacity.
- **Visual Passthrough with Hit-Testing**: Background content remains visible through the translucent window, while mouse hit-testing remains active across the window area to prevent accidental clicks through to background applications.

### 4. Viewport Layout & Smooth Scrolling
- **Dynamic Full-Height Layout**: Expanding the window vertically expands the subtitle history viewport to utilize available space, reflowing text naturally according to width without clipping.
- **Sub-Pixel Kinetic Scrolling**: Supports smooth scrolling using mouse wheels or precision touchpads.
- **Smart Auto-Follow Lock**: Scrolling up to inspect past subtitles automatically pauses auto-scrolling; scrolling back to the bottom or clicking the bottom badge resumes auto-following without forcibly snapping the view while reading.

### 5. Modular Provider Architecture & Local Credential Security
- **Speech Recognition (ASR)**: Built-in local offline streaming recognition (Sherpa-ONNX Zipformer), Groq Whisper, Volcengine ASR 2.0, Azure Speech SDK, etc.
- **Machine Translation (MT)**: DeepL API, OpenAI-compatible endpoints (Groq, Ollama, DeepSeek, GPT, etc.), Volcengine MT, Azure Translator, Google Translate, etc.
- **Local DPAPI Encryption**: All API keys and credentials are encrypted using Windows DPAPI bound to the current Windows user account and saved in `data/settings.json`, never uploaded to the cloud.

---

## Local English Speech Recognition (Sherpa-ONNX Zipformer)

The official full release (`antiDBT-v0.5.5-win-x64-full.zip`) includes the pre-validated default local English speech recognition model: **`sherpa-onnx-streaming-zipformer-en-2023-06-21`** (INT8 quantized).

### Advantages & Limitations
- **Advantages**:
  - Transcription accuracy is markedly superior to the older 20M model, accurately recognizing complex sentence structures and conversational vocabulary.
  - Maintains near-instantaneous streaming partial preview latency.
  - Runs entirely on standard x64 CPUs with a stable memory footprint (~260 MB observed in real use), requiring no ASR API keys and incurring zero cloud usage costs.
- **Limitations**:
  - English speech recognition only; spoken Chinese or other languages cannot be transcribed by this model.
  - Audio quality directly affects transcription; heavy background noise, simultaneous speakers, or strong accents may degrade accuracy.
  - Chinese translation operates as an independent stage and requires configuring a translation service (such as Groq, DeepL, or local Ollama).

### Local ASR vs. Cloud Translation
antiDBT strictly separates the processing pipeline into two independent stages:
1. **ASR Stage (Audio -> Text)**: When using local Sherpa-ONNX, audio is processed locally on CPU with zero network traffic and zero API keys.
2. **MT Stage (Source Text -> Target Text)**:
   - When source and target languages are both set to `en` (pass-through mode), no translation service is invoked, providing 100% offline operation.
   - When target language is set to `zh`, recognized text is transmitted via HTTPS to the configured translation service.

### Official Model Download & Manual Installation
If using a slim package without bundled models:
- **Official Download URL**: https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-en-2023-06-21.tar.bz2
- **Installation Path**: Extract and place into `antiDBT\models\sherpa-onnx-streaming-zipformer-en-2023-06-21\`.
- **Required Files**:
  - `encoder-epoch-99-avg-1.int8.onnx`
  - `decoder-epoch-99-avg-1.int8.onnx`
  - `joiner-epoch-99-avg-1.int8.onnx`
  - `tokens.txt`

---

## Translation Service Configuration

Open the settings panel via the gear icon in the top right:

### 1. Groq (Fast LLM-Based Translation)
1. Obtain an API key from https://console.groq.com.
2. Under Translation Provider, select `OpenAiCompatible`.
3. Set Endpoint to `https://api.groq.com/openai/v1/chat/completions`.
4. Set Model to `llama-3.3-70b-versatile` or `llama-3.1-8b-instant`.
5. Enter your API Key, set Source Language to `en`, Target Language to `zh`, and save.

### 2. DeepL (High-Quality Machine Translation)
1. Register at https://www.deepl.com and obtain an API key (supports DeepL API Free and Pro).
2. Under Translation Provider, select `DeepL`.
3. Enter your API Key (the software automatically switches endpoints based on the `:fx` suffix).
4. Set Source Language to `en`, Target Language to `zh`, and save.

### 3. Local LLM (Ollama, LM Studio)
1. Start your local Ollama service (e.g. `ollama run qwen2.5:7b`).
2. Enable "Allow Local Loopback" in antiDBT settings.
3. Select `OpenAiCompatible`, set Endpoint to `http://127.0.0.1:11434/v1/chat/completions`, and enter the model name for complete offline translation.

---

## System Requirements

- **Operating System**: Windows 10 64-bit (Version 1903 / Build 18362 or higher) or Windows 11 64-bit.
- **Audio Architecture**: Any soundcard or audio device supporting WASAPI loopback capture (Shared Mode required).
- **Runtime Environment**: The release package bundles a self-contained .NET 10 x64 runtime and Sherpa-ONNX native binaries. **End users do not need to install .NET SDK, .NET Runtime, Python, Go, CUDA, or C++ build tools.**

---

## Download, Installation & Upgrade

### Initial Setup & Launch
1. Download the official stable release from [GitHub Releases](https://github.com/Morchyee/AntiDBT/releases):
   - Package: `antiDBT-v0.5.5-win-x64-full.zip`
   - Checksum: `antiDBT-v0.5.5-win-x64-full.sha256`
2. Extract the archive into a non-restricted local folder (e.g., `D:\Tools\antiDBT\`).
3. Enter the extracted `antiDBT` folder and launch `antiDBT.exe`.
4. Open settings to verify your audio output device and provider configurations, save, and click "Start".

### Verifying Checksum (PowerShell)
```powershell
Get-FileHash -Path "antiDBT-v0.5.5-win-x64-full.zip" -Algorithm SHA256
```

### Upgrading
1. Completely exit any running antiDBT process.
2. Extract the new version into a clean directory.
3. To retain existing configurations and credentials, copy `data/settings.json` from the old version into the new version's `data/` directory (on the same machine and user account). Avoid in-place overwrites while the software is active.

---

## Building from Source & Testing

The project is built using C# 14 and .NET 10 WPF, with `global.json` locking the SDK version to guarantee deterministic builds.

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10/11 x64
- PowerShell

### Commands
```powershell
# Clone repository
git clone https://github.com/Morchyee/AntiDBT.git
cd AntiDBT/prototype

# Release build
dotnet build AntiDbt.sln -c Release

# Run complete 12-suite automated verification
pwsh -File scripts/verify-all.ps1 -SkipRestore

# Build distribution package
pwsh -File scripts/build-candidate-package.ps1 -Version "0.5.5"
```

---

## Verification & Operational Boundaries

### Automated Verification
- All 12 test suites (Core, Windows, CloudSpeech, Providers, Domestic, WPF Desktop Smoke, Diagnostics) comprising 125 unit and regression tests pass at 100%.
- Includes regression validation for Phase 5.5.5 state machine consistency, dynamic timeline ordering, and sticky translation lifecycles.
- Validated with real Zipformer INT8 model execution on streaming audio with verified transcription accuracy.

### Operational Boundaries
- **WASAPI Exclusive Mode**: If an audio player claims exclusive WASAPI ownership, system loopback cannot capture the audio; switch output to Shared Mode.
- **Cloud Service Outages**: The client enforces strict timeouts and backoff strategies, but third-party API availability and quotas remain subject to provider operations.
- **Specialized Audio Interfaces**: Dedicated ASIO hardware or virtual audio buses (e.g., Voicemeeter) may require manual configuration of the loopback recording source.

---

## Privacy, License & Disclaimer

- **Privacy**: antiDBT contains no central servers, telemetry tracking, or background analytics. Audio and text data are processed locally or routed directly to user-configured API endpoints.
- **Credential Storage**: API credentials are encrypted locally using Windows DPAPI and cannot be decrypted under different user accounts.
- **Open-Source License**: antiDBT source code is licensed under the [MIT License](LICENSE).
- **Third-Party Notices**:
  - Sherpa-ONNX (Apache-2.0)
  - ONNX Runtime (MIT)
  - NAudio (MIT)
  - Microsoft Cognitive Services Speech SDK (Runtime License)
  - Google Cloud Speech Client (Apache-2.0)
  - Zipformer model weights and model card are distributed under Apache-2.0.
  - Detailed third-party notices are provided in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- **Disclaimer**: Software is provided "AS IS" without warranty of any kind. Subtitle transcription and translation outputs depend on upstream model capabilities and should be used for reference purposes only.
