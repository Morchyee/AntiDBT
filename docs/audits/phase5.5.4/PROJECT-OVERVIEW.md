# Project Overview

Audit date: 2026-10-19. Source baseline: `main`, `765caa53264fbb8ce8fc07faf92c19d2790d769b`. Repository root: `D:/Desktop/AntiDBT-Gemini/antiDBT/prototype`; the requested parent directory is not the Git root. This audit creates documentation only. Current source version is `0.5.5-beta.4`, assembly/file version `0.5.5.4` ([Directory.Build.props:8](../../../Directory.Build.props#L8)). The published tag identifies `0ecc3fc`; current `main` additionally contains README and CI smoke-test changes.

## Purpose and implemented scope

antiDBT is a Windows desktop system-audio captioning application. It captures render-endpoint loopback audio, recognizes speech, translates changing segments, and renders bilingual history in a translucent WPF window. It is a desktop client, not a hosted transcription service.

| Implemented capability | Primary source | Qualification |
|---|---|---|
| System audio capture and device selection | [AudioSources.cs:170](../../../src/AntiDbt.Windows/AudioSources.cs#L170) | WASAPI render loopback; not microphone capture |
| Local streaming recognition | [SherpaOnnxStreamingAsrProvider.cs:117](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L117) | Actual language coverage depends on the installed model; distributed model is English |
| Cloud ASR and hybrid correction | [ProviderCatalog.cs:29](../../../src/AntiDbt.Providers/ProviderCatalog.cs#L29) | Six registered ASR choices; hybrid is experimental |
| Partial/Final revisions and local sentence splitting | [StreamingSentenceManager.cs:39](../../../src/AntiDbt.Core/StreamingSentenceManager.cs#L39) | Local/hybrid policy differs from cloud-provider segmentation |
| Context, lookahead, display fallback and grouped translation | [TranslationScheduler.cs:123](../../../src/AntiDbt.Core/TranslationScheduler.cs#L123), [SubtitleStore.cs:219](../../../src/AntiDbt.Core/SubtitleStore.cs#L219) | Context support is provider dependent; race and lifecycle findings remain |
| Eight translation choices, optional backup, same-language passthrough | [ProviderCatalog.cs:39](../../../src/AntiDbt.Providers/ProviderCatalog.cs#L39), [ProviderDescriptors.cs:20](../../../src/AntiDbt.Providers/ProviderDescriptors.cs#L20) | Cloud MT requires independent configuration; no bundled offline MT model |
| Unified history, follow/manual navigation, wrapping and free resize | [MainWindow.cs:315](../../../src/AntiDbt.Desktop/MainWindow.cs#L315), [MainWindow.cs:933](../../../src/AntiDbt.Desktop/MainWindow.cs#L933) | Legacy two-view placeholder exists only while history is empty |
| Theme, font, opacity, UI language, click-through and subtitle export | [MainWindow.cs:803](../../../src/AntiDbt.Desktop/MainWindow.cs#L803), [MainWindow.cs:1257](../../../src/AntiDbt.Desktop/MainWindow.cs#L1257) | UI language labels are not equivalent to ASR language support |
| Per-provider settings and protected credentials | [SettingsStore.cs:94](../../../src/AntiDbt.Windows/SettingsStore.cs#L94) | Windows CurrentUser DPAPI; encrypted settings are not portable between users |

## Stack and dependency graph inventory

| Project/component | Responsibility | Direct project/package dependencies | Entry evidence |
|---|---|---|---|
| `AntiDbt.Core` | Contracts, bounded queue, pipeline lifecycle, scheduling, segmentation, revision store/projection, telemetry, export | .NET 10 BCL; no package references | [Core.csproj:1](../../../src/AntiDbt.Core/AntiDbt.Core.csproj#L1) |
| `AntiDbt.Windows` | WASAPI/WAV sources, PCM conversion, settings envelope and DPAPI | Core; `NAudio.Wasapi` 2.3.0 | [Windows.csproj:1](../../../src/AntiDbt.Windows/AntiDbt.Windows.csproj#L1) |
| `AntiDbt.CloudSpeech` | Azure and Google SDK transports plus common speech-session lifecycle | Core; `Microsoft.CognitiveServices.Speech` 1.52.0; `Google.Cloud.Speech.V2` 1.8.0 | [CloudSpeech.csproj:1](../../../src/AntiDbt.CloudSpeech/AntiDbt.CloudSpeech.csproj#L1) |
| `AntiDbt.Providers` | ASR/MT factories, concrete adapters, HTTP policies, language mappings, service diagnostics | Core, CloudSpeech; `org.k2fsa.sherpa.onnx` 1.13.8; `AlibabaCloud.SDK.Alimt20181012` 2.4.2 | [Providers.csproj:1](../../../src/AntiDbt.Providers/AntiDbt.Providers.csproj#L1) |
| `AntiDbt.Desktop` | Composition root, windows, state/actions, programmatic WPF templates, smoke harnesses | Core, Windows, Providers; WPF | [Desktop.csproj:3](../../../src/AntiDbt.Desktop/AntiDbt.Desktop.csproj#L3) |
| Core subcomponents | `CaptionPipeline`, `TranslationScheduler`, `SubtitleStore`, `SubtitleProjection`, `StreamingSentenceManager`, `PipelineTelemetryCollector`, `SubtitleExporter` | Contracts connect capture, ASR and MT through interfaces | [Contracts.cs:22](../../../src/AntiDbt.Core/Contracts.cs#L22) |
| Desktop support | `SettingsWindow.ProviderEditor`, `LanguagePicker`, `DesktopTheme`, `ScrollRelay`, `UiText`, demo providers | Descriptor/profile system and WPF visual tree | [SettingsWindow.cs:242](../../../src/AntiDbt.Desktop/SettingsWindow.cs#L242) |
| Test executables | Core, Windows, CloudSpeech, Providers, Multi, Language, Domestic, Diagnostics; separate Sticky.Live | Custom console assertions; some ASP.NET loopback fixtures | [AntiDbt.sln:1](../../../AntiDbt.sln#L1), [verify-all.ps1:140](../../../scripts/verify-all.ps1#L140) |

There are five production projects and nine test/diagnostic projects on disk. Eight test/diagnostic projects are in the 13-project solution; `AntiDbt.Sticky.Live` is separate. The default verification script derives 12 suites by running some executables more than once. Suite count is not project count or live-provider coverage.

## Repository surfaces

| Path | Role and audit treatment |
|---|---|
| `src/` | Primary implementation; 59 tracked files at the baseline, including project files and manifest |
| `tests/` | Definitions and selected recorded test outputs; 44 tracked files. Historical logs are evidence artifacts, not test definitions |
| `scripts/`, `.github/workflows/` | 13 tracked PowerShell scripts and one Windows CI workflow |
| `docs/` | Existing design/phase/provider documents; checked for drift but not used as implementation proof |
| `licenses/`, `THIRD-PARTY-NOTICES.md` | Distribution licensing texts, provenance and model hash manifests |
| `models/`, `.packages/`, `.dotnet/`, `bin/`, `obj/` | Generated/downloaded material; do not recursively read as project source. Inspect selected metadata and model/package inventory separately |
| `artifacts/`, `publish/` | Historical builds, verification records and portable distributions; hashes/manifests are assessed separately |
| `PHASE-5.5.*-REPORT.*`, `antiDBT_release_prep/`, `smoke-test-out/` | Pre-existing untracked historical documents/helpers and generated output; preserved |
| Parent `analysis/`, `extracted/`, `reports/`, screenshots and clean-export directories | Auxiliary audit/reverse-engineering/build material outside the active repository; not additional production modules |

## Maturity assessment

**Verified by source:** clear interface boundaries; explicit session/revision identity; bounded queues and history; several provider-specific resource and transport safeguards; extensive deterministic regression definitions; packaged model/legal provenance.

**Verified limitations:** contextual revisions do not share all normal-job protections; native cleanup has timeout/worker-ownership risks; UI-applied telemetry is not wired into segment records; the quality benchmark aggregates fixture scores rather than measuring real translation accuracy. See the [issue register](CODE-QUALITY-AUDIT.md).

**Unverified:** recognition/translation accuracy across languages, hardware/driver diversity, multi-hour stability, real-provider combinations, and reproducible byte-identical builds. Green CI demonstrates the exercised contracts, not general production readiness.

Continue with [Architecture](ARCHITECTURE.md), [ASR and Translation](ASR-AND-TRANSLATION.md), and the [master audit](FULL-PROJECT-AUDIT.md).
