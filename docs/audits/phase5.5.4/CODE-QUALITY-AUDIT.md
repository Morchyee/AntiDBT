# Code Quality Audit

Baseline: `765caa5`, 2026-10-19. Scope is the complete tracked source/project/script/workflow inventory, relevant untracked phase reports and local release/test evidence. Generated directories were excluded from repetitive source reading. No dependency scanner, live credential check, cloud test, native stress test or production modification was performed.

## Classification and strengths

P0 means critical correctness/security/stability; P1 high-impact reliability or user-facing defect; P2 moderate risk/performance/maintainability; P3 low-risk debt. A **confirmed** issue has a demonstrated implementation or evidence defect; this does not imply observed field incidence. A **risk** has a specific source-supported trigger but no reproduced production failure. No P0 issue was established within this audit's limits.

Useful boundaries already exist: package-free Core contracts, platform-specific capture/settings, SDK-specific speech transport and concrete provider factories. Store locking and revision/session checks protect ordinary updates; bounded queues/history/cache limit common growth paths. Final-priority scheduling, opt-in backup costs, endpoint validation, owned no-redirect HTTP, typed provider errors and DPAPI atomic saves are substantive safeguards ([Contracts.cs:22](../../../src/AntiDbt.Core/Contracts.cs#L22), [SubtitleStore.cs:120](../../../src/AntiDbt.Core/SubtitleStore.cs#L120), [ProviderHttp.cs:9](../../../src/AntiDbt.Providers/ProviderHttp.cs#L9), [SettingsStore.cs:94](../../../src/AntiDbt.Windows/SettingsStore.cs#L94)). They do not cover every contextual-revision path.

## Prioritized register

| ID | Priority | Status | Finding |
|---|---|---|---|
| AQ-01 | P1 | Confirmed API counterexample | Combined translation can commit against newer source revisions |
| AQ-02 | P1 | Confirmed policy/lifecycle gap; runtime impact conditional | Contextual revision bypasses normal cancellation and request guards |
| AQ-03 | P1 | Confirmed ordering counterexample | Receipt-time context ordering reverses after ASR correction |
| AQ-04 | P1 | Conditional native-lifecycle risk | Native resources can be freed without worker completion |
| AQ-07 | P1 | Confirmed evidence defect | Fixed-score fake benchmark does not establish translation accuracy |
| AQ-05 | P2 | Confirmed observability gap | Capture/job loss is omitted from pipeline drop totals |
| AQ-06 | P2 | Confirmed measurement gap | Pipeline telemetry stops before UI and never populates UI apply time |
| AQ-08 | P2 | Confirmed unbounded retention path | Contextual one-shot tracking outlives bounded history |
| AQ-09 | P2 | Confirmed sanitizer counterexample; disclosure conditional | Diagnostic redaction is incomplete and text is retained |
| AQ-10 | P2 | Conditional configuration exposure | Legacy hybrid key option is serializable plaintext |
| AQ-11 | P2 | Resource-ownership risk | Azure SpeechConfig is outside the disposal owner |
| AQ-12 | P2 | Confirmed release-gate gap | Clean-tree/verification checks do not establish exact reproducibility |
| AQ-13 | P2 | Confirmed export counterexample | Retained sessions export backward cue timestamps |
| AQ-14 | P3 | Maintainability debt | Desktop composition/actions/rendering share a large class |
| AQ-15 | P2 | Confirmed legacy tooling drift | Historical scripts pin obsolete frameworks/version metadata |
| AQ-16 | P3 | Conditional model-selection risk | Model discovery can silently select a different directory |
| AQ-17 | P2 | Confirmed test-reporting gap | Missing model fixture is reported as a passing test |

### AQ-01 — Missing revision checks in display-group commit

- **Evidence:** [SubtitleStore.cs:219](../../../src/AntiDbt.Core/SubtitleStore.cs#L219) accepts session, segment IDs and text; it validates no expected revision/source or complete membership and returns true even when no member is found. Caller snapshots text before awaiting MT ([TranslationScheduler.cs:146](../../../src/AntiDbt.Core/TranslationScheduler.cs#L146)).
- **Trigger/impact:** Either member is corrected/deleted while a combined request is in flight. Old combined translation can overwrite the newer first row and hide subsequent translations within that group; the matching session alone is insufficient.
- **Recommendation:** Commit atomically only when every expected session/ID/revision/source identity still matches; reject incomplete/changed groups and distinguish rejection in diagnostics.
- **Confidence/reproduction:** High. Safe in-memory probe updated the first Final to revision 2, then applied a group built for revision 1; stale display text was accepted. Actual asynchronous provider interleaving was not executed.

### AQ-02 — Contextual requests have separate lifecycle and policy

- **Evidence:** [TranslationScheduler.cs:135](../../../src/AntiDbt.Core/TranslationScheduler.cs#L135) creates an independent job/deadline and directly awaits the provider. It is tracked in `tasks`, but not `jobs`; `CancelSegment`/`CancelPending` only cancel `jobs` ([566](../../../src/AntiDbt.Core/TranslationScheduler.cs#L566)). Compare ordinary terminal/cooldown guards and outstanding-task tracking ([290](../../../src/AntiDbt.Core/TranslationScheduler.cs#L290), [372](../../../src/AntiDbt.Core/TranslationScheduler.cs#L372)).
- **Trigger/impact:** Stop, reconfigure, corrected/deleted segment, provider ignoring cancellation, or rate-limit/authentication failure during contextual revision. Work can survive cancellation, continue paid requests, hold logical slots and race normal requests for the same revision. A session guard prevents old-session display, not the call itself. Context revision is also omitted from ordinary request telemetry.
- **Recommendation:** Use a common attempt owner/lifetime token and policy for ordinary and contextual work; guard same-revision attempt ordering, account physical requests, and await owned completion before disposal.
- **Confidence/reproduction:** High for source gap; runtime occurrence not induced. This is not a claim that provider cancellation always fails.

### AQ-03 — Arrival timestamp is not transcript order

- **Evidence:** Store resets `ReceivedTimestamp` on accepted revisions ([SubtitleStore.cs:191](../../../src/AntiDbt.Core/SubtitleStore.cs#L191)), but contextual predecessor/context/lookahead predicates use it ([TranslationScheduler.cs:127](../../../src/AntiDbt.Core/TranslationScheduler.cs#L127), [330](../../../src/AntiDbt.Core/TranslationScheduler.cs#L330), [336](../../../src/AntiDbt.Core/TranslationScheduler.cs#L336)). Store rows themselves sort by audio time ([SubtitleStore.cs:202](../../../src/AntiDbt.Core/SubtitleStore.cs#L202)).
- **Trigger/impact:** Hybrid corrects earlier segment A after later segment B is already Final. B can qualify as A's preceding context, and lookahead can point backward. Context-sensitive translation may revise the wrong relationship.
- **Recommendation:** Use stable session-local transcript order/audio intervals plus explicit tie-breaking, preserving receipt time solely for latency.
- **Confidence/reproduction:** High. Two-segment in-memory source sequence plus correction reproduced the actual predecessor predicate selecting a segment later in audio. No live MT quality regression was measured.

### AQ-04 — Timed wait does not prove native worker termination

- **Evidence:** Worker captures native stream/recognizer references and calls synchronous decode ([SherpaOnnxStreamingAsrProvider.cs:283](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L283), [320](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L320)). Disposal catches failure of its three-second wait, then frees those objects ([357](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L357)). Hybrid disposal also does not join its pump/correction workers before disposing shared state ([HybridStreamingAsrProvider.cs:333](../../../src/AntiDbt.Providers/HybridStreamingAsrProvider.cs#L333)).
- **Trigger/impact:** A native call outlasts the wait, or a hybrid worker remains active. Resources may be disposed concurrently with use; native crash/use-after-free is a conditional risk, not an observed audit crash.
- **Recommendation:** Separate cancellation deadline from ownership transfer. Retain/quarantine resources until the worker exits, or isolate genuinely uninterruptible native work; join hybrid workers before disposing dependencies.
- **Confidence/reproduction:** High confidence in unsafe ordering; conditional impact unverified. No native hang/crash was induced.

### AQ-05 — Drop statistics cover only one queue

- **Evidence:** `PcmAudioSource` uses a 50-item `DropOldest` channel without a drop counter ([AudioSources.cs:112](../../../src/AntiDbt.Windows/AudioSources.cs#L112)). Pipeline statistics derive from `BoundedAudioQueue` ([BoundedAudioQueue.cs:60](../../../src/AntiDbt.Core/BoundedAudioQueue.cs#L60)); Groq independently evicts jobs ([GroqWhisperProvider.cs:67](../../../src/AntiDbt.Providers/GroqWhisperProvider.cs#L67)).
- **Trigger/impact:** Slow capture consumer, ASR/backpressure/reconnect, or slow chunk MT/ASR upload. Zero pipeline drops does not imply no missing audio/transcripts.
- **Recommendation:** Expose cumulative per-stage drop/sample counters and carry discontinuities into segment diagnostics.
- **Confidence/reproduction:** High, static proof of coverage gap. Overflow was not newly induced; historical queue tests cover isolated components.

### AQ-06 — UI/end-to-end telemetry is incomplete

- **Evidence:** `UiApplyMs` exists ([PipelineTelemetry.cs:28](../../../src/AntiDbt.Core/PipelineTelemetry.cs#L28)) but production never assigns it; assignments found only in synthetic tests. `TotalPipelineMs` is recorded before `store.ApplyTranslation` ([TranslationScheduler.cs:404](../../../src/AntiDbt.Core/TranslationScheduler.cs#L404), [418](../../../src/AntiDbt.Core/TranslationScheduler.cs#L418)); semantic/slot waiting is not separately timed. MainWindow records a separate receive-to-view latency ([MainWindow.cs:997](../../../src/AntiDbt.Desktop/MainWindow.cs#L997)).
- **Trigger/impact:** Reporting collector summaries as acoustic-to-render performance hides Dispatcher/layout cost and contextual requests; comments describing UIApplied overstate implementation.
- **Recommendation:** Correlate session/segment/revision/attempt from capture to store acceptance, UI binding and a defined rendered-frame milestone; report missing values honestly.
- **Confidence/reproduction:** High, source search/call-chain inspection. No new end-to-end latency run.

### AQ-07 — Benchmark scores are fixture constants

- **Evidence:** Samples contain `HumanScoreOld/New` and error labels ([TranslationAccuracyBenchmarkTests.cs:14](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L14)). `RunAsync` uses `BenchmarkContextTranslator`, a pattern-based fake ([46](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L46), [570](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L570)); it retains fixed scores and even substitutes expected translation on blank output ([91](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L91)). Aggregation averages those constants ([112](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L112)).
- **Trigger/impact:** Reading the 2.68 -> 4.86 (+80.99%) and preset error changes as measured model quality/accuracy, or its stopwatch as real-time E2E latency, leads to unjustified product decisions. The Core entry adds a `passed` field without incrementing `failures` when that predicate is false ([Program.cs:11](../../../tests/AntiDbt.Core.Tests/Program.cs#L11), [925](../../../tests/AntiDbt.Core.Tests/Program.cs#L925)).
- **Recommendation:** Retain this as a deterministic scenario fixture, assert actual outputs, and establish a separate blinded, reproducible real-provider evaluation with independently scored outputs and raw timing provenance.
- **Confidence/reproduction:** High, source and existing JSON verified. No new model quality evaluation; P1 concerns evidence integrity, not a measured 80.99% runtime defect.

### AQ-08 — Unbounded contextual tracking

- **Evidence:** `_retranslated` and `_translatedWithLookahead` are per-scheduler HashSets ([TranslationScheduler.cs:120](../../../src/AntiDbt.Core/TranslationScheduler.cs#L120)), populated at [131](../../../src/AntiDbt.Core/TranslationScheduler.cs#L131) and [340](../../../src/AntiDbt.Core/TranslationScheduler.cs#L340), with no eviction/reset. They are not pruned with history or `CancelPending`.
- **Trigger/impact:** Many contextual segments in a long-running pipeline cause memory growth despite bounded store/cache; marking before success also suppresses a later successful contextual attempt for that ID.
- **Recommendation:** Scope attempts to retained revision identities and prune upon completion/eviction/session change.
- **Confidence/reproduction:** High static confidence; growth rate/long-session memory impact not measured.

### AQ-09 — Diagnostics are not a complete privacy boundary

- **Evidence:** Regex only matches labeled ASCII token subsets ([CaptionDiagnostics.cs:25](../../../src/AntiDbt.Core/CaptionDiagnostics.cs#L25)); records retain source, translation, context, lookahead and exception detail ([43](../../../src/AntiDbt.Core/CaptionDiagnostics.cs#L43)). Opt-in disk logging performs synchronous append/rotation under a shared lock ([61](../../../src/AntiDbt.Core/CaptionDiagnostics.cs#L61)); contextual failure passes `ex.Message` ([TranslationScheduler.cs:179](../../../src/AntiDbt.Core/TranslationScheduler.cs#L179)).
- **Trigger/impact:** A secret containing `+`/other excluded characters or an unlabeled credential appears in captured text/error detail. Sanitizer can leave a suffix; transcripts also remain sensitive even without keys. Enabled disk tracing may add processing-thread I/O latency.
- **Recommendation:** Prefer metadata-only default diagnostics, explicit transcript opt-in/retention policy, structured error categories and secret allowlisting/redaction tests. Queue optional disk output off critical threads.
- **Confidence/reproduction:** High for sanitizer counterexample using a synthetic token, no real credentials accessed. No actual leak, disk trace or I/O slowdown reproduced.

### AQ-10 — Legacy hybrid option can persist outside DPAPI

- **Evidence:** Hybrid reads `Options["cloudApiKey"]` as a fallback credential ([HybridStreamingAsrProvider.cs:80](../../../src/AntiDbt.Providers/HybridStreamingAsrProvider.cs#L80)). Only `ApiKey`/`Secrets` are JSON-ignored; Options remain public serialization fields ([Contracts.cs:54](../../../src/AntiDbt.Core/Contracts.cs#L54)). `SettingsStore.SaveAsync` serializes the public settings alongside encrypted secret blobs ([SettingsStore.cs:119](../../../src/AntiDbt.Windows/SettingsStore.cs#L119)).
- **Trigger/impact:** A legacy/imported/programmatic profile puts a cloud key in this option; a subsequent save can store it in plaintext. Normal credential UI/ApiKey storage is protected.
- **Recommendation:** Explicitly migrate known secret options into protected fields, reject/remove them from public serialization and test all supported legacy paths.
- **Confidence/reproduction:** High for reachable serialization path, conditional exposure. User settings were deliberately not read and no saved secret was demonstrated.

### AQ-11 — Azure configuration ownership is incomplete

- **Evidence:** `SpeechConfig.FromSubscription` creates a local configuration object ([AzureSpeechProvider.cs:27](../../../src/AntiDbt.CloudSpeech/AzureSpeechProvider.cs#L27)); unlike audio format, push stream, AudioConfig and recognizer, it is not acquired by `NativeResources` ([30](../../../src/AntiDbt.CloudSpeech/AzureSpeechProvider.cs#L30)). Cleanup disposes only that owner ([47](../../../src/AntiDbt.CloudSpeech/AzureSpeechProvider.cs#L47)).
- **Trigger/impact:** Repeated Azure connections rely on SDK/finalizer behavior for configuration resources; deterministic cleanup is not established, particularly on constructor failures.
- **Recommendation:** Register configuration with the owner before other allocations and test failure paths/repeated initialization.
- **Confidence/reproduction:** Medium for practical leak magnitude, high for missing explicit ownership. No live Azure/native-memory leak reproduced.

### AQ-12 — Release provenance checks have blind spots

- **Evidence:** Publisher checks `git diff HEAD` only over selected paths and tests aggregate summary counts/commit ([publish-local-asr.ps1:15](../../../scripts/publish-local-asr.ps1#L15)). Untracked files, root `global.json`/NuGet configuration/solution and workflow changes are outside that clean-tree check. No tracked package lock exists; SDK allows roll-forward ([global.json:4](../../../global.json#L4)).
- **Trigger/impact:** A package can be built from inputs not uniquely described by summary commit, even with deterministic compilation enabled. A manifest/hash proves artifact identity, not independent reproducibility or coverage of every input.
- **Recommendation:** Verify full intended input-tree cleanliness, exact named suites/exit codes, resolved dependency/runtime hashes and source revision; keep immutable run-specific evidence and consider locked restore.
- **Confidence/reproduction:** High static confidence. Local published ZIP/source provenance matches inspected records; no mismatch of that artifact is alleged.

### AQ-13 — Export crosses independent session clocks

- **Evidence:** Store preserves earlier-session rows on normal stop/new session ([SubtitleStore.cs:50](../../../src/AntiDbt.Core/SubtitleStore.cs#L50)); SRT/VTT export writes each row's `AudioStart/End` without session grouping/rebasing ([SubtitleExporter.cs:12](../../../src/AntiDbt.Core/SubtitleExporter.cs#L12), [45](../../../src/AntiDbt.Core/SubtitleExporter.cs#L45)). UI exports all retained rows ([MainWindow.cs:1278](../../../src/AntiDbt.Desktop/MainWindow.cs#L1278)).
- **Trigger/impact:** Stop/restart then export history. A second session starting at zero follows an older cue at eight seconds, yielding backward/overlapping timestamps. Grouped/sticky UI text can also differ from exact fields exported.
- **Recommendation:** Define session selection or rebased multi-session export; align grouped cues deliberately and test Partial/Final/export semantics.
- **Confidence/reproduction:** High. Safe in-memory two-session `ToSrt` probe reproduced an 8-second cue followed by a 0-second cue; no file created.

### AQ-14 — Desktop coupling and compressed control flow

- **Evidence:** `MainWindow` spans 1,515 lines and owns composition, persistence coordination, native hooks, rendering and export ([MainWindow.cs:118](../../../src/AntiDbt.Desktop/MainWindow.cs#L118), [933](../../../src/AntiDbt.Desktop/MainWindow.cs#L933), [1452](../../../src/AntiDbt.Desktop/MainWindow.cs#L1452)); `SettingsWindow` also combines layout/profile/probe behavior ([SettingsWindow.cs:242](../../../src/AntiDbt.Desktop/SettingsWindow.cs#L242)).
- **Trigger/impact:** Future changes across language, lifecycle or UI state have broad regression surfaces; dense one-line control flow reduces reviewability.
- **Recommendation:** After lifecycle/regression tests are stronger, extract narrowly owned action/state or view-building responsibilities when needed. Avoid a speculative rewrite.
- **Confidence/reproduction:** High maintainability observation; no functional defect attributed solely to class length.

### AQ-15 — Legacy scripts are not current release entry points

- **Evidence:** `test.ps1` and `test-phase4.ps1` contain hardcoded `net9.0` paths ([test.ps1:20](../../../scripts/test.ps1#L20), [test-phase4.ps1:21](../../../scripts/test-phase4.ps1#L21)); candidate packaging overrides assembly/file version to 0.5.3.0 ([build-candidate-package.ps1:74](../../../scripts/build-candidate-package.ps1#L74)); phase5 smoke arguments are unquoted and have no bounded process wait ([publish-phase5.ps1:57](../../../scripts/publish-phase5.ps1#L57)).
- **Trigger/impact:** A maintainer chooses a plausible older entry point on current .NET 10 source or a path with spaces; validation/package metadata can be wrong. Old release scripts can delete/replace remote assets when explicitly run.
- **Recommendation:** Document current authoritative commands; label/archive legacy entry points in a separately authorized change, and add script-level path/version contracts.
- **Confidence/reproduction:** High static mismatch; scripts were not executed. No remote mutation occurred.

### AQ-16 — Model auto-discovery is broader than explicit intent

- **Evidence:** `ResolveModelDirectory` tries base/current paths then scans model subdirectories for tokens ([SherpaOnnxStreamingAsrProvider.cs:72](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L72)); wildcard selection chooses matching files with int8 preference ([98](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L98)). Language metadata does not inspect the loaded model ([ProviderLanguages.cs:41](../../../src/AntiDbt.Providers/ProviderLanguages.cs#L41)).
- **Trigger/impact:** Several models are installed or an explicit relative path is invalid. A different model can load successfully without matching the selected language/identity.
- **Recommendation:** Treat explicit missing paths as errors; use a validated model manifest and show resolved identity/languages.
- **Confidence/reproduction:** Medium practical risk, high source confidence; no model loading performed in this audit.

### AQ-17 — Optional native integration is not skip-aware

- **Evidence:** The Sherpa real-audio test returns immediately when the model WAV/tokens are absent ([Providers Program.cs:151](../../../tests/AntiDbt.Providers.Tests/Program.cs#L151)). The surrounding `Check` treats a completed delegate as success, with no skipped outcome ([Program.cs:12](../../../tests/AntiDbt.Providers.Tests/Program.cs#L12)).
- **Trigger/impact:** A clean CI runner has no ignored model/test WAV directory. A green Providers suite can include a named real-model test that never ran; 12/12 then overstates native integration coverage if read literally.
- **Recommendation:** Record ran/skipped/failed separately, make fixture availability explicit, and require the native integration gate only in an appropriately provisioned job.
- **Confidence/reproduction:** High static confidence. No new missing-model suite execution; actual optional execution must be checked per historical log.

## Security and reliability limits

CurrentUser DPAPI, zeroing temporary secret bytes, authority-bound profile restoration and atomic settings replacement are well implemented ([SettingsStore.cs:69](../../../src/AntiDbt.Windows/SettingsStore.cs#L69), [119](../../../src/AntiDbt.Windows/SettingsStore.cs#L119), [179](../../../src/AntiDbt.Windows/SettingsStore.cs#L179)). This is encryption at rest, not protection from code running as the same user. Cloud providers intentionally receive audio/text and authentication; the English UI string claiming secrets never leave the machine is overly broad ([UiText.cs:155](../../../src/AntiDbt.Desktop/UiText.cs#L155)). Managed strings are not reliably erasable.

Owned HTTP clients disable redirects and validate endpoint authority. LLM custom endpoints are intentionally user-configurable; loopback HTTP is an explicit development path. No actual credential files were opened. No vulnerability advisory/dependency license compliance conclusion is possible from source inventory alone. The roadmap prioritizes identity/lifecycle and credible measurement before more providers or UI redesign ([Development Roadmap](DEVELOPMENT-ROADMAP.md)).
