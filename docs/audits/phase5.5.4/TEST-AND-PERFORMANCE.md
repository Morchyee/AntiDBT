# Test and Performance Audit

Scope: source definitions plus existing local result artifacts. **No full verification suite, build, device capture, cloud call or UI smoke was executed during this audit.** Four narrow in-memory probes were executed against an existing Core assembly. Historical green results are identified by revision and are not new executions.

## Test infrastructure and side-effect assessment

Tests are executable console projects with hand-written assertions and JSON reports, not a conventional discovered xUnit/NUnit test set. `scripts/verify-all.ps1` builds the solution, runs suites serially with process deadlines, saves logs/summary, fails on a suite error, and optionally adds soak/hardware checks ([verify-all.ps1:68](../../../scripts/verify-all.ps1#L68), [139](../../../scripts/verify-all.ps1#L139), [229](../../../scripts/verify-all.ps1#L229)). Desktop smoke is a separate executable mode with a 60-second process limit and captured error detail ([164](../../../scripts/verify-all.ps1#L164)).

Although `-ArtifactsDir` isolates driver logs, child suites also write fixed project-relative outputs. Running the driver would write `bin`, `obj`, `.dotnet-home`, restore/build artifacts and existing report locations; provider suites start loopback listeners and may conditionally load local models. This exceeds this audit's documentation-only write exception. Existing evidence was inspected instead. No unsafe suite was silently represented as executed.

| Suite/project | Type and actual verification | Side effects / important limits |
|---|---|---|
| Core | Unit and mock-based pipeline/store/scheduler tests: stale session/revision rejection, Partial/Final history, split/merge, retries/cooldowns/cancellation, queue, export and telemetry | Fixed `artifacts/core-tests.json`; benchmark writes another fixed file. [Program.cs:5](../../../tests/AntiDbt.Core.Tests/Program.cs#L5), [923](../../../tests/AntiDbt.Core.Tests/Program.cs#L923) |
| Core sentence policy/continuity | Synthetic hypotheses for 30/120-second continuous speech, trailing words, handoff, Final immutability and capture-after-readiness | Tests algorithmic word sequences, not real audio recognition/WER. [SentencePolicyTests.cs:8](../../../tests/AntiDbt.Core.Tests/SentencePolicyTests.cs#L8), [SentenceContinuityTests.cs:7](../../../tests/AntiDbt.Core.Tests/SentenceContinuityTests.cs#L7) |
| Core sticky translation | Mock slow/failing provider, timeout/retry, exact versus display fallback, atomic related split/merge | Does not cover delayed contextual display-group commit after source correction. [StickyTranslationTests.cs:8](../../../tests/AntiDbt.Core.Tests/StickyTranslationTests.cs#L8) |
| Windows | Platform unit/integration: PCM rate/callback invariance, clipping/widths, ring/channel overflow, synthetic capture lifecycle, DPAPI roundtrip/corruption/authority profiles | Creates/writes/removes temporary settings directories; default uses synthetic credentials. Optional hardware path is separate. [Program.cs:9](../../../tests/AntiDbt.Windows.Tests/Program.cs#L9), [13](../../../tests/AntiDbt.Windows.Tests/Program.cs#L13), [80](../../../tests/AntiDbt.Windows.Tests/Program.cs#L80) |
| CloudSpeech | SDK contract mapping via injected gateways, identities, PCM preservation, cancellation/errors/EOF, Google config/chunk/stream deadline behavior | No live Azure/Google recognition. [Program.cs:17](../../../tests/AntiDbt.CloudSpeech.Tests/Program.cs#L17), [Regressions.cs:7](../../../tests/AntiDbt.CloudSpeech.Tests/Regressions.cs#L7) |
| CloudSpeech.NativeSmoke | Native SDK construction/load path | No real recognition quality/network acceptance; separate command flag. [Program.cs:9](../../../tests/AntiDbt.CloudSpeech.Tests/Program.cs#L9) |
| Providers | Fake HTTP plus loopback protocol/transport, WebSocket reconciliation/errors, Groq VAD/jobs, hybrid correlation, model-path failures; conditional real Sherpa WAV | Creates temporary fixtures/listeners/reports. Real model test returns successfully without execution when WAV/tokens are absent (AQ-17). [Program.cs:75](../../../tests/AntiDbt.Providers.Tests/Program.cs#L75), [151](../../../tests/AntiDbt.Providers.Tests/Program.cs#L151) |
| Providers.Multi | Factory/provider mock combinations and request/response contracts | Synthetic text and fake credentials; no cloud quality evidence. [Program.cs:1](../../../tests/AntiDbt.Providers.Multi.Tests/Program.cs#L1) |
| Providers.Multi.Localhost | 15 ASR/MT combinations through local HTTP/WebSocket protocol paths; Azure/Google use injected gateways | Tests transport/adaptor interoperability, not 15 live cloud combinations. [LocalProtocol.cs:9](../../../tests/AntiDbt.Providers.Multi.Tests/LocalProtocol.cs#L9) |
| Language | Registry normalization, profiles/MRU, unsupported selections, provider language mappings, DeepL fake catalog cache, backup routing, synthetic pipeline alignment | Mock handlers and fixed JSON output. [Program.cs:11](../../../tests/AntiDbt.Language.Tests/Program.cs#L11), [LanguageCases.cs:12](../../../tests/AntiDbt.Language.Tests/LanguageCases.cs#L12), [LanguagePipelineCases.cs:10](../../../tests/AntiDbt.Language.Tests/LanguagePipelineCases.cs#L10) |
| Domestic / Domestic.Offline | Owned HTTP paths, signing/body/error contracts, routing and language restrictions; offline variant avoids listener scenarios | Official endpoint production behavior/credentials not exercised. [Program.cs:9](../../../tests/AntiDbt.Domestic.Tests/Program.cs#L9) |
| Desktop.Smoke | WPF windows/templates, source-first visibility, sticky/sentence handoff, scrolling, actual resize growth, theme/font/opacity and settings interactions | Writes PNG/JSON/trace, synthetic configuration and pipeline; simulated DPI scales are not physical monitor tests. [DesktopSmoke.cs:15](../../../src/AntiDbt.Desktop/DesktopSmoke.cs#L15), [SentenceDesktopSmoke.cs:94](../../../src/AntiDbt.Desktop/SentenceDesktopSmoke.cs#L94), [Phase4DesktopSmoke.cs:76](../../../src/AntiDbt.Desktop/Phase4DesktopSmoke.cs#L76) |
| Diagnostics.Performance | 40,000 synthetic Core events, bounded 2,000-row store, periodic projection, per-operation stopwatch and retained managed bytes | No capture, native ASR, real MT, WPF or full process footprint. [Program.cs:27](../../../tests/AntiDbt.Diagnostics/Program.cs#L27) |
| Diagnostics.Soak (optional) | Approximately 60-second synthetic audio/ASR/MT pipeline with reconnects/jitter/failures | Not a multi-hour real-device/native/provider stability test. [Soak.cs:10](../../../tests/AntiDbt.Diagnostics/Soak.cs#L10) |
| Diagnostics.HardwareTone (optional) | WASAPI capture of generated quiet tone; verifies counts/nonzero PCM | Actual audio device use, no ASR/MT and no persisted PCM. [Program.cs:10](../../../tests/AntiDbt.Diagnostics/Program.cs#L10) |
| Sticky.Live (outside solution/default 12) | WAV or actual loopback + local/cloud recognition/translation/rendering evidence paths | Loads saved credentials, can issue paid requests/capture device audio, writes captions/reports. Not run here. [Program.cs:16](../../../tests/AntiDbt.Sticky.Live/Program.cs#L16), [SentenceLive.cs:14](../../../tests/AntiDbt.Sticky.Live/SentenceLive.cs#L14) |
| Portable local-ASR smoke | Packaged EXE, WAV played three times into default output, real WASAPI, bundled Sherpa, WPF; optional real MT via saved configuration | Device/network/user-data access if selected. Asserts Finals/render frames/translations, not WER or semantic fidelity. [LocalAsrReleaseSmoke.cs:22](../../../src/AntiDbt.Desktop/LocalAsrReleaseSmoke.cs#L22), [93](../../../src/AntiDbt.Desktop/LocalAsrReleaseSmoke.cs#L93) |

## Existing 12-suite evidence

The default twelve are Core, Windows, CloudSpeech, CloudSpeech.NativeSmoke, Providers, Providers.Multi, Providers.Multi.Localhost, Language, Domestic, Domestic.Offline, Desktop.Smoke and Diagnostics.Performance. Optional soak, tone and live provider sessions are **not** included in 12/12.

| Record | Recorded revision/time | Verified interpretation |
|---|---|---|
| [phase5.5.4-verified summary](../../../artifacts/phase5.5.4-verified/verification-summary.json) | `1556dfd`, 2026-10-18 16:27 +08 | 12 passed, zero failed, suite exit codes zero; pre-release working-tree generation, not exact final artifact provenance |
| [UTF-8 release verification](../../../artifacts/release-0.5.5.4-beta/utf8-release-verification/verification-summary.json) | `0ecc3fc`, 2026-10-18 22:05 +08 | 12 named suites, all exit zero, 86.33 s recorded total; matches published source commit |
| [local CI-fix verification](../../../artifacts/ci-resize-20261019/full-host/verification-summary.json) | `dbdc141`, 2026-10-19 15:29 +08 | 12 passed; metadata records parent HEAD while CI fix was being tested in the working tree |
| [CI result record](../../../artifacts/ci-resize-20261019/github-ci-result.json) | Run #6, exact `765caa5`, 2026-10-19 | Stored GitHub run/log evidence reports success and all 12; not refreshed or rerun by this audit |

The release/CI records support component regression success at their identified inputs. They do not prove that every optional model/device test executed, all cloud providers work, the 30-sample scores are measured, or native shutdown is safe under hangs. Source-linked skip reporting needs improvement (AQ-17).

## Phase 5.5.4 metric credibility

The untracked [phase report](../../../PHASE-5.5.4-REPORT.md) is a claim inventory, not primary proof. Its source and raw methodology were checked:

| Historical claim | Origin and audit conclusion |
|---|---|
| 30 translation samples | Thirty hand-authored fixtures in `Create30BenchmarkSamples`; simulated ASR segments and hardcoded pattern translator. Useful scenario examples, not a real-provider evaluation. [Benchmark:35](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L35), [570](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L570) |
| Scores 2.68 -> 4.86, +80.99% | Arithmetic over predefined `HumanScoreOld/New`. No independent runtime scoring, grader, blinded rater record, confidence interval or measured model comparison. This is a fixed quality-score comparison and **cannot establish accuracy improvement** (AQ-07). [Benchmark:112](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L112) |
| Error counts 30 -> 0 | Aggregates fixture error labels; does not classify generated outputs. Blank generated text can be replaced by expected fixture text. [Benchmark:91](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L91), [108](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L108) |
| P50 0.04 ms / P95 1.65 ms | Historical report values; current overwritten [benchmark JSON](../../../artifacts/phase5.5.4-benchmark.json), timestamp 2026-10-19 15:27 +08, has 0.03 / 1.46 ms and 74 fake calls. Stopwatch spans injected segments through scheduler flush with waits disabled and immediate fake translation. Neither result measures acoustic, ASR, network or render latency. [Benchmark:53](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L53), [60](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L60), [99](../../../tests/AntiDbt.Core.Tests/TranslationAccuracyBenchmarkTests.cs#L99) |
| UIApplied / 448 ms sample | [Telemetry sample](../../../artifacts/ci-resize-20261019/full-host/diagnostic-telemetry-sample.json) contains one synthetic record with supplied stage numbers. Tests explicitly set UI time; production does not (AQ-06). [Core Program:453](../../../tests/AntiDbt.Core.Tests/Program.cs#L453), [490](../../../tests/AntiDbt.Core.Tests/Program.cs#L490) |
| Model/Groq/hybrid CPU and 55/30/60 MB RAM table | No matching controlled three-provider process-memory/CPU experiment or WER comparison was established. Synthetic managed-memory delta is not private working set; local-ASR smoke records process CPU time but no memory. Treat this comparative table as unverified, not a benchmark. [Diagnostics:27](../../../tests/AntiDbt.Diagnostics/Program.cs#L27), [Local smoke:110](../../../src/AntiDbt.Desktop/LocalAsrReleaseSmoke.cs#L110) |
| Provider quality stars and latency ranges | Adapter context support is source-verifiable; relative semantic quality, costs and latency require independent dated provider measurements. No comparable raw experiment was established for the table |
| Portable ZIP hash/size | Phase report describes an earlier 110,416,675-byte ZIP, hash `F55DE2...`; final published-record ZIP is 112,575,330 bytes, hash `7301D8...`. Both can exist; they must not be treated as the same artifact. See [Build and Release](BUILD-AND-RELEASE.md) |

## Genuine runtime evidence and its bounds

Existing release smoke records provide a narrower real path: WAV playback -> default render device -> WASAPI -> bundled English Sherpa -> store -> WPF, with optional DeepL. The test measures average machine-normalized CPU as process CPU seconds / wall seconds / logical processor count, including startup/playback/drain. It records visible frames and nonempty Finals, not transcription reference accuracy ([LocalAsrReleaseSmoke.cs:69](../../../src/AntiDbt.Desktop/LocalAsrReleaseSmoke.cs#L69), [110](../../../src/AntiDbt.Desktop/LocalAsrReleaseSmoke.cs#L110)).

| Existing record | Outcome | Wall time / normalized CPU | What it supports |
|---|---|---|---|
| [fresh-no-key](../../../artifacts/release-0.5.5.4-beta/fresh-no-key/local-asr-smoke.json) | Passed | 30.08 s / 8.33% | Local ASR + WASAPI + WPF, eight rows, no MT API |
| [cross-drive-no-key](../../../artifacts/release-0.5.5.4-beta/cross-drive-no-key/local-asr-smoke.json) | Passed | 26.41 s / 9.17% | Another extraction location on the same tested host |
| [fresh-real-mt](../../../artifacts/release-0.5.5.4-beta/fresh-real-mt/local-asr-smoke.json) | Passed | 26.50 s / 9.26% | Seven Finals with DeepL text and zero recorded translation errors |
| [unicode-native-red](../../../artifacts/release-0.5.5.4-beta/unicode-native-red/local-asr-smoke.json) | Failed | 0.07 s | Historical path/encoding failure, zero rows |
| [unicode-utf8-green](../../../artifacts/release-0.5.5.4-beta/unicode-utf8-green/local-asr-smoke.json) | Passed | 28.80 s / 9.34% | Packaged UTF-8-path mitigation worked for this host/run |

These are archived single-host smoke observations, not reruns, broad hardware certification, multi-hour soak or E2E latency distributions. CPU percentages depend on processor count/model and workload. No reliable full-process memory envelope, first-partial acoustic latency, first-translation latency, final-stabilization distribution or sustained real-provider throughput benchmark was established.

Existing [performance.json](../../../artifacts/performance.json) is explicitly synthetic: 40,000 events, 2,000 retained rows, ~1.65 MB retained managed delta; it excludes native models/WPF. Existing [soak.json](../../../artifacts/soak.json) records ~62 seconds and three connections, with synthetic providers. These do not substantiate multi-hour resource stability.

## Safe probes executed in this audit

PowerShell loaded the existing `src/AntiDbt.Core/bin/Debug/net10.0/AntiDbt.Core.dll` and constructed synthetic records in memory. Identity: assembly 0.5.5.4; informational version `0.5.5-beta.4+dbdc1412133414c9a1787916c4596bbbe3a144d8`; SHA256 `F7A178222D71171D70584BC561DD7E070B1D695E1E39CCFD9FD61053146552FE`. Core source is unchanged between release and audit HEAD. This is **existing-binary corroboration**, not a fresh source build or complete test suite.

| Probe | Executed result | Bound |
|---|---|---|
| Stale display group | New revision source preserved, stale combined display accepted | Public store API counterexample; no async network race executed |
| Context chronology | Earlier audio segment corrected after later Final; actual receipt-time predecessor predicate selects later audio as preceding | Predicate/store counterexample; no translator invoked |
| Diagnostic redaction | Synthetic labeled token with `+` left its suffix visible | Sanitizer behavior only; no real secret accessed |
| Retained-session SRT | Two rows across sessions export 8-second cue then 0-second cue | String generated in memory, no export file |

No persistent writes or network requests were made by these probes. Initial command syntax/constructor mistakes were corrected before successful probe results; they are not test passes. Package verification separately read ZIP streams and hashes without extraction/execution.

## Missing scenarios and better measurement

| Gap | Required verification | Priority |
|---|---|---|
| Contextual race/cancellation | Controlled delayed provider: source correction, deletion, split/merge, stop/reconfigure, same-revision response reordering; check rejected commits and zero post-stop calls | P1, AQ-01/02/03 |
| Native lifetime | Blocked decode/start/stop, repeated dispose, slow CPU, hybrid pump/correction teardown; detect use after ownership release | P1, AQ-04 |
| Real quality | Versioned audio/text corpus, independent source/reference translations, actual provider/model/version, captured outputs, blinded rubric and error attribution | P1, AQ-07 |
| Timing chain | Capture timestamp -> first partial -> first MT -> accepted revision -> render milestone, with missing samples and cancellation/cache/context paths separate | P2, AQ-06 |
| End-to-end loss | Overflow every queue independently; verify counters and discontinuity mapping rather than only surviving items | P2, AQ-05 |
| Long-run resources | Multi-hour native/device/provider sessions, full process private bytes/working set, handles/threads/GC and retained scheduler IDs | P2, AQ-08/11 |
| Settings/diagnostics | Legacy secret-option migration, Unicode/base64/header redaction, endpoint authority transitions; synthetic credentials only | P2, AQ-09/10 |
| UI/device environments | Physical DPI transitions, small/resized desktops, manual history under revisions, hotplug/default-device changes, restart/export sessions | P2, AQ-13 |
| Optional capability reporting | Explicit skipped/ran result, required-model fixture availability, named-suite exit/result consistency | P2, AQ-17 |

For quality, compare outputs under a declared rubric without renaming score gains as accuracy. For timing, report acoustic and internal scheduling stages separately, include sample count/warmup/percentile method and hardware/network/provider metadata. For memory, distinguish managed retained delta, process private bytes and working set. None of those future experiments was performed here.
