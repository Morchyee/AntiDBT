# antiDBT · Standalone Real-Time Bilingual Subtitles Client

<p align="center">
  <b>Minimalist · Ultra-Low Latency · Highly Customizable Real-Time Bilingual Subtitles Floating Desktop App for Windows</b><br>
  <sub>Independent Open-Source Live Captioning Product · Mix & Match ASR & MT Providers</sub>
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
  <img src="https://img.shields.io/badge/Release-v0.5.5--beta.4-green.svg" alt="Release">
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License">
  <img src="https://img.shields.io/badge/CI-Passing-brightgreen.svg?logo=githubactions" alt="CI">
</p>

---

## 📖 Introduction

**antiDBT** is an independently engineered real-time bilingual subtitle floating overlay client designed for Windows 10 and 11. By capturing desktop system audio through Windows WASAPI loopback, combined with voice activity detection (VAD), modern speech-to-text (ASR), and machine translation (MT) services, it renders clean, ultra-responsive bilingual subtitles directly onto your screen.

antiDBT is an independently developed, open-source live captioning product. It is tailored for watching raw foreign videos, international live streams, cross-border virtual meetings, and online learning.

---

## ✨ Key Features

### 1. 🛡️ Reliability & Stability Hardening (Phase 5.3 Focus)
- **Zero Trailing Sentence Loss on Stop**: Stopping capture triggers a bounded drain window where accepted audio blocks, delayed final transcripts, and pending translations are completed and rendered before closing.
- **Audio Freshness Bounded Buffer & Expiry Pruning**: Bounded queue with 50-chunk nominal capacity (each 6,400-byte PCM chunk represents 200ms audio at 16kHz/16-bit mono, totaling ~10s buffer capacity) and a 2.0-second chunk staleness expiration threshold; stale chunks overflowing due to network bottlenecks are discarded with accurate discontinuity telemetry, preventing latency snowballing.
- **Ghost Translation Cleanup & Draft Validation**: Stale provisional translations are immediately evicted when source speech revisions clear or invalidate text. Strict prefix-extension compatibility checks guarantee delayed stale responses never pollute active subtitles.
- **Korean Inter-Word Spacing Preservation**: Resolves syllable-level agglutination issues in streaming buffers, ensuring natural spacing and readable Korean translations.
- **Continuous Health Retry Budget & Rate-Limit Backoff**: Consecutive connection failures respect a bounded retry budget reset only after continuous healthy streaming. Automatic HTTP 429 extraction strictly honors server `Retry-After` headers, preventing redundant billing and rate throttling.
- **Granular Status Indication & Silence Diagnostics**:
  - 🟢 Listening / 🟡 Connecting / 🔴 Error / ⚪ Idle.
  - When speech recognition works but MT fails, a distinct amber dot (`#FFB300`) signals translation degradation while keeping the Stop button fully functional.
  - Continuous silence (>3.0s) reports "Connected but no audio signal", clearly decoupling hardware mute from network connectivity failures.

### 2. 🎨 Truly Adjustable Semi-Transparent Floating Overlay (Phase 5.2)
- **0% to 100% Background Opacity Slider**: Adjust overlay opacity instantly under `Settings → Appearance` with live, non-blocking preview.
- **Complete Decoupling of Text and Background**: Subtitle text, controls, and status indicators maintain 100% solid, razor-sharp contrast, eliminating blurry fonts.
- **Real Visual Passthrough & Click-Through Protection**: 12px modern rounded corners reveal background videos clearly while maintaining mouse hit-testing—even at 0% opacity, mouse clicks never accidentally penetrate the window.

### 3. 📐 Elastic Viewport & Dynamic Height Auto-Fill
- **Vertical Auto-Expansion**: Stretching the window vertically expands the subtitle history viewport to fill the full available height, presenting more conversation context on larger monitors.
- **Fluid Horizontal Reflow**: Resizing horizontally reflows bilingual text naturally according to available width without clipping or overflowing.
- **Smart Overlay Anchor**: The "↓ Bottom" quick-scroll badge stays anchored neatly at the bottom-right corner of the subtitle viewport.

### 4. 📜 Silky Smooth Pixel-Level Scrolling & Smart Follow Lock
- **Sub-Pixel Kinetic Scrolling**: Enjoy smooth precision scrolling with mouse wheels or precision touchpads without jarring page-skips.
- **Follow-Lock Protection**: Scrolling up to inspect past subtitles automatically pauses auto-scroll. Scrolling back to bottom or clicking "↓ Bottom" resumes it. New arriving subtitles never forcibly snap your view to the bottom while reading.

### 5. ⚙️ Non-Intrusive Settings & Localhost Security Policy
- **Seamless Settings Interaction**: Opening or canceling the settings dialog never halts active capture or wipes subtitle history. Visual modifications apply immediately; core pipeline reconfigurations trigger an isolated, smooth single restart.
- **Secure Local LLM Support**: Supports connecting directly to local loopback endpoints (Ollama, LocalAI, LM Studio, or OpenAI-compatible servers at `http://127.0.0.1` / `localhost`) when explicitly allowed, while strictly enforcing HTTPS for remote endpoints.

### 6. 📊 Diagnostic Latency Telemetry
- Built-in lightweight thread-safe collector tracking `QueueWaitMs`, `AsrServiceMs`, `MtDebounceWaitMs`, `MtServiceMs`, and `UiApplyMs`.
- One-click anonymized export providing median/p95 percentiles without sensitive API keys, tokens, transcripts, or audio data.

### 7. 🔌 Modular Provider Ecosystem
- **Speech Recognition (ASR)**: Groq Whisper (ultra-fast cloud), Volcengine ASR 2.0, Azure Speech SDK, Google Cloud Speech V2, etc.
- **Machine Translation (MT)**: DeepL API, OpenAI-compatible LLMs (Ollama, DeepSeek, GPT-4o, Claude, etc.), Groq LLM, Volcengine MT, Azure Translator, Google Translate, Baidu Translate, Alibaba Cloud MT.

### 8. 🔒 Local Credential Protection
- All API keys, secrets, and connection parameters are encrypted using **Windows DPAPI (Data Protection API)** bound to your Windows user identity and saved to `data/settings.json`. Keys never leave your machine.

---

## 🖥️ System Requirements

- **Operating System**: Windows 10 / Windows 11 (x64)
- **Audio Architecture**: Any soundcard/device supporting WASAPI loopback (standard system output, speakers, headphones)
- **Portable Runtime**: The binary release includes a self-contained .NET 10 runtime; **no separate .NET SDK or runtime installation is required for end users**. This release uses .NET 10.

---

## 🚀 Quick Start

### 1. Download & Launch
1. Download the latest `antiDBT-v0.5.5-beta.4-win-x64-full.zip` from [GitHub Releases](https://github.com/Morchyee/AntiDBT/releases).
2. Extract the archive into any preferred directory (e.g. `D:\Tools\antiDBT\`).
3. Run `antiDBT.exe`.

### 2. Configure Providers
1. Click the **⚙** gear icon on the top right.
2. **Speech Recognition**: Select your preferred ASR provider (e.g. `GroqWhisper` or `VolcengineAsr2`) and enter your API Key.
3. **Translation**: Select your MT provider (e.g. `DeepL` or `OpenAiCompatible`), entering credentials and target model.
4. **Appearance**: Choose Dark/Light theme and set your background opacity (recommended: 70% ~ 85%).
5. Click **Save Settings**.

### 3. Start Captioning
- Click **Start** in the toolbar.
- Play any video, stream, podcast, or virtual meeting audio on your PC to view realtime bilingual captions.
- Want to preview typography without consuming API quotas? Right-click the window and select **Local Demo** for synthetic testing.

---

## 🛠️ Building & Verification from Source

Written in C# 14 and .NET 10 WPF. The repository contains a `global.json` locking the toolchain to SDK 10.0.402 for deterministic builds.

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.402 recommended)
- Windows 10/11 x64
- PowerShell 7 (`pwsh`)

### Build Commands
```powershell
# Clone the repository
git clone https://github.com/Morchyee/AntiDBT.git
cd AntiDBT

# Build Release binaries
dotnet build AntiDbt.sln -c Release

# Run the complete 12-suite automated verification suite
pwsh -File scripts/verify-all.ps1 -SkipRestore

# Package a self-contained portable distribution with SHA256 checksums
pwsh -File scripts/build-candidate-package.ps1 -Version "0.5.3-beta"
```

---

## ⚠️ Known Limitations & Testing Boundaries

1. **Cloud API Credentials**: Active speech recognition and translation require valid API keys from your chosen providers.
2. **Testing vs. Live Service Boundaries**: The 100% pass rate in our local 12-suite regression harness and CI smoke tests proves internal state machine, audio loopback, and UI contract correctness, but **does not guarantee external cloud provider uptime, network resilience, or quota availability during 24/7 continuous operation**. Live performance depends on external API health and network connectivity.
3. **WASAPI Exclusive Mode**: If an audio player or DAW runs in Exclusive Mode, WASAPI loopback capture may be blocked; ensure shared audio mode is selected.
4. **Firewall Prompt**: Allow antiDBT through Windows Defender Firewall when prompted so that outbound API requests can connect.

---

## 🛡️ Privacy & Data Security

- **Zero Tracking**: antiDBT has no central telemetry servers, tracking scripts, or analytics collection.
- **Direct P2P Requests**: Audio blocks and text flow exclusively between your computer and the third-party API endpoints you explicitly configure.
- **DPAPI Encryption**: Secrets are encrypted at the OS level using your local Windows credentials.

---

## ⚖️ Disclaimer

1. **Independent Product**: **antiDBT** is an independently developed, open-source live captioning product.
2. **Usage Compliance**: Users are responsible for their own network traffic, API consumption, costs, and compliance with the Terms of Service of their chosen third-party providers.

---

## 📄 License

antiDBT is licensed under the [MIT License](LICENSE). Third-party dependencies and notices are documented in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
