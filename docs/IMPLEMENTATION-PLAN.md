# antiDBT Phase 2 Implementation Plan

> Agentic workers: implement the scoped task brief, test its behavior first, and preserve evidence in artifacts. User requests autonomous implementation; no design approval pause.

Goal: independent runnable Windows bilingual subtitles with configurable third-party ASR/translation, verified state/capture/UI behavior and honest service/visual limitations.

Architecture: net9.0 Core contracts/state/scheduling; net9.0-windows Windows WASAPI and DPAPI; WPF Desktop; independent Providers; dependency-free executable test harnesses and local HTTP/WebSocket integration servers. NAudio 2.3.0 is already cached. No target binaries, extracted proprietary code, assets, credentials, or private endpoints enter the product.

Spec: user Phase 2 request and reports/TECHNICAL-ARCHITECTURE.md, FEATURES-AND-PRIORITIES.md, subtitle-settings.md. Phase 2 supersedes older references to integration with other projects: this application is independent.

## Constraints and decisions

- All authored product code/build scripts/tests/docs under prototype; Phase 2 progress under reports/phase2.
- D:\Doubao remains read-only. Observe only a returned subtitle window; never launch/restart/inject/reconfigure original software.
- API keys entered in settings and DPAPI CurrentUser encrypted separately from non-secret JSON, never log content or key values.
- net9.0 uses installed SDK/WindowsDesktop references. Self-contained win-x64 release when available; document runtime requirements if publish blocked.
- User selected Volcengine Doubao streaming ASR2.0 + Doubao machine translation API; generic adapters remain replaceable. Seed LiveInterpret2.0 remains gated until mainland dual text/incremental event capability is verified. Vendor defaults/protocols live only in Providers.
- Synthetic/local integration tests never count as real cloud quality/latency evidence.
- Source-first display while translation pending and bounded 2000-row history are explicit engineering choices; documented differences from first-phase behavior.

## Tasks / checkable deliverables

### 1. Core state, translation scheduler and pipeline (root)
- [x] Write/run failing tests for same-segment revision replacement, stale session/revision rejection, bounded history, source/translation grouping, cancellation, and latest translation wins.
- [x] Implement SubtitleStore, SubtitleProjection, TranslationScheduler, CaptionPipeline with injected audio/ASR/translation.
- [x] Test local/synthetic streaming incl disconnect/retry, finish, stop/restart, cancellation and bounded queues.

### 2. Windows audio and secure settings (one implementer)
- [x] Read task-audio.md and Core contracts; write/run failing tests for ring overflow/full-block/tail, silence suppress/resume, PCM conversion chunk invariance and DPAPI roundtrip/nonplaintext.
- [x] Implement WASAPI loopback with NAudio; stereo→mono resampling to 16000 S16LE, 6400-byte chunks, 640000 ring, bounded channel50, generation/cancel/disposal.
- [x] Add WaveFileAudioSource and input device listing; settings JSON atomic+DPAPI secure keys. No cloud or microphone calls in tests.

### 3. API providers (one implementer, after task2)
- [x] Write/run failing tests against local WebSocket/HTTP servers: fragmented results, interim/final, auth header, finish, malformed responses, cancellation and errors without secrets.
- [x] Implement user-selected Volcengine ASR2 and machine translation adapters, replaceable factory boundary, endpoint/resource/key configuration and safe errors. LiveInterpret remains gated.
- [x] Save provider protocol documentation with official source links and testing coverage.

### 4. WPF UI and delivery (root)
- [x] Build independent 600-DIP dark rounded subtitle overlay with 16/20/32 type, source toggle, target zh/en, compact+expanded virtual history, bottom follow, selectable content, toolbar drag, autosize, topmost toggle, error/retry/stop/close.
- [x] Build settings panel for service/model/endpoint/device and DPAPI keys; explicit capture start button; demo/local test mode clearly labeled.
- [x] Test window via built-in UI smoke mode and Computer Use if visible; read-only attempt original subtitle observation and record limitation.
- [x] Publish runnable Windows executable, run complete tests/performance and capture-only hardware check without persisting or transmitting audio. Save completion matrix, architecture, continuation log and known issues.

## Review focus

- Stop/restart/target change during slow translation: old result never repopulates new session.
- Service disappears with no audio: keepalive/reconnect independent of capture callbacks, bounded stop.
- Slow ASR/network: discard oldest audio without growing memory; surface dropped count.
- Partial stereo frames and streaming resampling: preserve remainder/phase across callbacks.
- Settings/cross-monitor: corrupted settings recover visibly, keys unreadable to others; offscreen windows clamp into work area.

## Completion boundary
Local implementation and packaged verification complete. Authenticated cloud acceptance, original visual parity and long-session acceptance remain open in CONTINUATION.md. Provider initial tests were partly added after code; do not claim full initial TDD.

