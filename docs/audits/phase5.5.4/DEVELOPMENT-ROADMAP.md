# Evidence-Based Development Roadmap

This is a planning artifact, not authorization to implement a new phase or publish a release. Priority follows reliability, translation quality, real-time latency, UI consistency, resource efficiency, then features. Complexity is approximate relative engineering scope, not a calendar estimate. Findings are defined in [Code Quality Audit](CODE-QUALITY-AUDIT.md); verification boundaries are in [Test and Performance](TEST-AND-PERFORMANCE.md).

## Ordered work packages

### R1 — Make contextual commits revision-safe

| Attribute | Plan |
|---|---|
| Motivation / benefit | AQ-01/03: preserve the correct source-to-translation relationship when earlier ASR Finals are corrected; eliminate wrong predecessor/lookahead mapping |
| Priority / complexity | P1; medium |
| Risk | Medium: group replacement and hybrid split/merge semantics must remain atomic |
| Dependencies | Define stable transcript ordering and expected identity for every group member; use existing store lock/session model |
| Acceptance criteria | Any changed/deleted/missing member rejects an old group commit; context follows audio order after earlier corrections; exact/provisional/sticky display remains distinct; no duplicated/lost source rows |
| Verification | Delayed fake MT plus deterministic interleavings for correction, deletion, split/merge, equal audio starts and out-of-order responses; retain ordinary revision/continuity/WPF tests |
| Source anchor | [SubtitleStore.cs:219](../../../src/AntiDbt.Core/SubtitleStore.cs#L219), [TranslationScheduler.cs:127](../../../src/AntiDbt.Core/TranslationScheduler.cs#L127) |

### R2 — Give all MT attempts one lifecycle and policy

| Attribute | Plan |
|---|---|
| Motivation / benefit | AQ-02: cancel owned contextual work on stop/reconfigure, respect terminal/cooldown/outstanding limits and prevent same-revision last-writer regressions |
| Priority / complexity | P1; medium-high |
| Risk | Medium-high: fairness, Final priority and cooperative/uncooperative provider behavior interact |
| Dependencies | R1 identity contract; explicit logical-slot versus physical-request ownership |
| Acceptance criteria | No new provider calls after stop completes; late noncooperative responses cannot apply; contextual calls obey same terminal/rate/cost policies; physical concurrency remains bounded; disposal accounts for every owned task |
| Verification | Controlled blocked/canceled/429/auth providers, stop/reconfigure races and repeated retries; assert call counts and identities, not only visible output |
| Source anchor | [TranslationScheduler.cs:135](../../../src/AntiDbt.Core/TranslationScheduler.cs#L135), [574](../../../src/AntiDbt.Core/TranslationScheduler.cs#L574) |

### R3 — Make native and hybrid teardown ownership-safe

| Attribute | Plan |
|---|---|
| Motivation / benefit | AQ-04/11: prevent resources being freed while decode/pumps can still use them; make repeated Azure initialization deterministic |
| Priority / complexity | P1; high for stalled native decode, small-medium for missing configuration ownership |
| Risk | High: replacing unsafe timed disposal must not introduce an indefinite UI shutdown wait |
| Dependencies | Explicit task/resource owner; potentially a worker isolation strategy if native calls cannot be interrupted |
| Acceptance criteria | Every native object is freed only after its worker releases ownership; timeout is surfaced without freeing live objects; hybrid workers joined; constructor failures clean acquired objects exactly once |
| Verification | Failure-injection seams, blocked-worker tests, repeated connect/stop/dispose, handle/private-byte trends and real slow-host native runs |
| Source anchor | [SherpaOnnxStreamingAsrProvider.cs:340](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L340), [AzureSpeechProvider.cs:27](../../../src/AntiDbt.CloudSpeech/AzureSpeechProvider.cs#L27) |

### R4 — Harden privacy and legacy configuration paths

| Attribute | Plan |
|---|---|
| Motivation / benefit | AQ-09/10: keep legacy secret options out of plaintext settings and avoid treating regex-redacted transcripts as safe metadata |
| Priority / complexity | P2; medium |
| Risk | Medium: migration must preserve working profile authority and credentials without silently discarding configuration |
| Dependencies | Enumerate supported secret aliases, define transcript logging consent/retention and structured error schema |
| Acceptance criteria | Synthetic legacy keys never serialize publicly; normal DPAPI roundtrip/authority guards preserved; trace defaults contain metadata only or documented explicit content opt-in; UI descriptions match cloud authentication behavior |
| Verification | Synthetic base64/Unicode/header/JSON secret vectors, migration/recovery/endpoint changes, opt-in trace content inspection; no real secrets required |
| Source anchor | [CaptionDiagnostics.cs:25](../../../src/AntiDbt.Core/CaptionDiagnostics.cs#L25), [HybridStreamingAsrProvider.cs:80](../../../src/AntiDbt.Providers/HybridStreamingAsrProvider.cs#L80) |

### R5 — Establish credible quality evaluation before tuning semantics

| Attribute | Plan |
|---|---|
| Motivation / benefit | AQ-07: replace fixed-score evidence with measured outputs; distinguish ASR, segmentation and MT errors before changing heuristics/prompts |
| Priority / complexity | P1 evidence correctness; medium-high experimental work |
| Risk | Medium: small curated corpora, grader bias and provider/model drift can still mislead |
| Dependencies | R1/R2 correctness baseline; explicit authorization for paid provider tests; versioned corpus/rubric and held-out cases |
| Acceptance criteria | Scenario fixtures assert actual expected behavior and never substitute expected text for blank output; real outputs retained with model/config provenance; independent blind ratings/error attribution; score changes described as quality scores unless a defined accuracy metric exists |
| Verification | Repeated same-input comparisons, reviewer agreement, negative controls, missing-output failures and separate ASR WER only with reference transcripts |
| Source anchor | [TranslationAccuracyBenchmarkTests.cs:91](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L91), [112](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L112) |

### R6 — Measure the real latency and loss chain

| Attribute | Plan |
|---|---|
| Motivation / benefit | AQ-05/06: show where latency/loss originates before optimizing sentence wait, decoding, network or WPF work |
| Priority / complexity | P2; medium-high |
| Risk | Medium: instrumentation can perturb timing; acoustic/render milestones need precise definitions |
| Dependencies | Stable revision/attempt identity from R1/R2; monotonic clocks and explicit discontinuities |
| Acceptance criteria | First partial, first accepted translation and final stabilization reported separately; capture/dequeue/ASR/wait/provider/store/UI/render stages correlated; cache/cancel/context paths and missing samples visible; every dropping queue reports sample/job loss |
| Verification | Synthetic deterministic stage delays, then authorized WASAPI/model/provider runs with warmup/sample counts/hardware/network; P50/P95 never substitute scheduling time for acoustic-to-render latency |
| Source anchor | [PipelineTelemetry.cs:28](../../../src/AntiDbt.Core/PipelineTelemetry.cs#L28), [TranslationScheduler.cs:404](../../../src/AntiDbt.Core/TranslationScheduler.cs#L404), [AudioSources.cs:112](../../../src/AntiDbt.Windows/AudioSources.cs#L112) |

### R7 — Preserve unified UI and define export session semantics

| Attribute | Plan |
|---|---|
| Motivation / benefit | AQ-13 plus proven unified-scroller architecture: consistent history/export without reintroducing competing viewports |
| Priority / complexity | P2; medium |
| Risk | Medium: rebasing/export grouping must not change live subtitle identity or manual follow behavior |
| Dependencies | Explicit product choice: current-session export or documented multi-session timeline; R1 display group semantics |
| Acceptance criteria | Monotonic SRT/VTT cues under restart; deliberate grouped/exact/provisional export policy; source remains immediately visible; manual history position preserved; viewport grows with actual available window size |
| Verification | Two-session string-based export tests, correction/group cases, existing resize/scroll smoke plus physical DPI/mixed-monitor/small desktop/manual touchpad checks |
| Source anchor | [SubtitleExporter.cs:12](../../../src/AntiDbt.Core/SubtitleExporter.cs#L12), [MainWindow.cs:315](../../../src/AntiDbt.Desktop/MainWindow.cs#L315) |

### R8 — Bound long-session state and optimize measured hot paths

| Attribute | Plan |
|---|---|
| Motivation / benefit | AQ-08/14/16: prune contextual tracking, make model identity explicit and control long-history projection/allocation |
| Priority / complexity | P2 resource work; P3 structural cleanup; medium |
| Risk | Medium: premature model caching can retain large native allocations; projection optimization can break history semantics |
| Dependencies | R3 lifetime safety and R6 measurements; retain existing history/cache bounds |
| Acceptance criteria | Tracking sets bounded by a documented retained-session policy; resolved model identity/languages validated; no unexplained private-byte/handle/thread growth over a declared multi-hour run; only measured UI/CPU hotspots optimized |
| Verification | High-volume synthetic ID/eviction tests plus multi-hour real native/device soak and full-process memory/handle sampling; maintain frame/follow correctness |
| Source anchor | [TranslationScheduler.cs:120](../../../src/AntiDbt.Core/TranslationScheduler.cs#L120), [MainWindow.cs:933](../../../src/AntiDbt.Desktop/MainWindow.cs#L933), [SherpaOnnxStreamingAsrProvider.cs:72](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L72) |

### R9 — Make CI/release evidence exact and skip-aware

| Attribute | Plan |
|---|---|
| Motivation / benefit | AQ-12/15/17: future releases should identify exactly tested inputs and distinguish unavailable integration fixtures from passes |
| Priority / complexity | P2; medium |
| Risk | Low-medium: stricter gates need clear provisioning and must preserve existing evidence/artifacts |
| Dependencies | Named-suite/result schema, authoritative script selection, model fixture provenance, resolved runtime/dependency manifest |
| Acceptance criteria | Full intended tree/inputs tied to tested revision; explicit pass/fail/skip; mandatory real-model job provisioned separately; fresh immutable run artifacts; CI uploads WPF failure evidence; correct package cache; no old-script version drift |
| Verification | Missing-fixture negative test, changed/untracked input rejection, path-with-spaces staging, archive-byte/hash checks and authorized independent rebuild comparison |
| Source anchor | [publish-local-asr.ps1:15](../../../scripts/publish-local-asr.ps1#L15), [Providers Program.cs:154](../../../tests/AntiDbt.Providers.Tests/Program.cs#L154), [windows-ci.yml:24](../../../.github/workflows/windows-ci.yml#L24) |

### R10 — Consider features only after reliability/measurement gates

| Attribute | Plan |
|---|---|
| Motivation / benefit | Device change recovery and broader local-model/language support may reduce user setup friction; adding provider count alone does not address current correctness gaps |
| Priority / complexity | P3, optional; medium-high depending on feature |
| Risk | High if introduced alongside lifecycle changes or advertised without model/provider validation |
| Dependencies | R1-R3, credible R5/R6 evidence, resource budgets from R8 and release gates from R9 |
| Acceptance criteria | Feature-specific behavior and fallback policy defined before implementation; no regressions in sentence ownership, subtitle safety, privacy, stop/restart or measured latency; language support tied to actual model identity |
| Verification | Dedicated device hotplug/default-switch or model-language fixtures plus real Windows acceptance; existing complete suites retained |
| Source anchor | Current startup-only device selection: [AudioSources.cs:180](../../../src/AntiDbt.Windows/AudioSources.cs#L180); model discovery: [SherpaOnnxStreamingAsrProvider.cs:51](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L51) |

## Delivery discipline

Keep changes incremental and scoped to one invariant at a time. Preserve the unified live-history container, exact/provisional/display-fallback distinction and atomic source handoff. Each future change should first reproduce its targeted condition, retain unrelated local work, run relevant tests, and attach fresh revision-specific evidence. Existing tags and binaries remain historical artifacts; this audit neither changes them nor authorizes a release.
