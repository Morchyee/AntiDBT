# Full Project Audit

Audit date: 2026-10-19. Repository: `D:/Desktop/AntiDBT-Gemini/antiDBT/prototype`. Baseline: `main`, `765caa53264fbb8ce8fc07faf92c19d2790d769b`. Release: assembly/file v0.5.5.4, published tag `v0.5.5-beta.4` at `0ecc3fc`. This is a read-only source audit with nine new Markdown reports; no implementation or release work is authorized or performed.

## Executive Summary

antiDBT has a coherent Windows audio-to-bilingual-caption pipeline, meaningful revision safeguards, bounded common buffers and a unified live-history UI. Its current source supports six ASR selections and eight MT selections, with local Sherpa recognition and optional cloud translation/correction. The project is a functional beta with substantial component regression coverage, but contextual translation ownership/order and native teardown require attention. Several reported quality/performance claims exceed their measurement methodology.

No P0 issue was established. Three data/translation counterexamples and a redaction counterexample were reproduced safely in memory; native teardown failures remain conditional risks. Existing release hashes/manifests and archived verification records are consistent with the published source revision. No fresh full-suite or real-device/provider execution was performed here.

## Project Overview

The Git root is the nested `prototype` directory, with 207 tracked files and 65 pre-existing untracked files at audit start. Five production projects divide Core, Windows capture/settings, cloud speech SDKs, concrete providers and WPF desktop composition. The .NET 10 Windows x64 self-contained distribution includes an English Zipformer model and native libraries; MT still requires configured services unless source equals target.

Detailed inventory, dependencies, source surfaces and feature qualifications: [PROJECT-OVERVIEW.md](PROJECT-OVERVIEW.md).

## Architecture Summary

`App.OnStartup` creates `MainWindow`; the latter loads protected settings and composes `CaptionPipeline`. WASAPI audio is downmixed/resampled to mono PCM16/16 kHz, queued, sent to an `IAsrSession`, and reconciled into revisioned segments. Local recognition uses `StreamingSentenceManager`; cloud adapters retain their own segmentation. `SubtitleStore` accepts source immediately; `TranslationScheduler` debounces/selects context and calls MT. Accepted exact/provisional/grouped translation updates notify WPF, where `SubtitleProjection` supplies wrapping paragraphs inside one ListBox scroller.

Source anchors: [MainWindow.cs:732](../../../src/AntiDbt.Desktop/MainWindow.cs#L732), [CaptionPipeline.cs:79](../../../src/AntiDbt.Core/CaptionPipeline.cs#L79), [TranslationScheduler.cs:203](../../../src/AntiDbt.Core/TranslationScheduler.cs#L203), [MainWindow.cs:933](../../../src/AntiDbt.Desktop/MainWindow.cs#L933). Dependency/data-flow/sequence/state diagrams: [ARCHITECTURE.md](ARCHITECTURE.md). Provider/segmentation/context details: [ASR-AND-TRANSLATION.md](ASR-AND-TRANSLATION.md).

## Current Feature Status

| Area | Source-verified status | Qualification |
|---|---|---|
| Capture / local ASR | WASAPI, normalized PCM, Sherpa streaming, bounded queues and endpoint policy | Hotplug is not seamless; model language/identity must match actual files |
| Cloud / hybrid ASR | Volcengine, Groq, Azure, Google; local plus Groq higher-Final correction | Hybrid experimental; no automatic ASR-provider fallback; live acceptance varies by provider |
| Contextual MT | Semantic wait, previous context, lookahead, grouped replacement, optional backup | English completeness heuristic; provider-dependent context; AQ-01/02/03 gaps |
| Revision safety | Session/revision guarded ordinary translation, Final protections, tombstones, atomic handoff and sticky fallback | Display-group commit has weaker validation than ordinary commit |
| Desktop UI | Shared live-history scroller, wrapping, manual/follow navigation, resize, themes/languages, export | Empty-history dual placeholders remain hidden once rows exist; physical DPI/long-run cost unverified |
| Persistence | Authority-bound profiles, CurrentUser DPAPI, atomic saves | Legacy hybrid secret option can bypass protected fields |
| Portable release | Model/native/legal/dependency packaging; revision manifest and SHA256 | Consistent provenance verified locally, no independent reproducible rebuild |

UI evidence and previous regression analysis: [UI-ARCHITECTURE.md](UI-ARCHITECTURE.md). Build/distribution evidence: [BUILD-AND-RELEASE.md](BUILD-AND-RELEASE.md).

## Major Technical Strengths

- Core contracts separate capture/ASR/MT from UI and service transport.
- Ordinary store updates enforce session/revision identity and preserve Final history while retaining compatible display fallback.
- Local and hybrid handoff batches publish committed text and remaining tail atomically.
- Common queues/history/cache are bounded; normal MT accounts for requests that ignore cancellation.
- Unified subtitle history avoids competing per-text vertical constraints; credential persistence and owned HTTP policies provide useful safety boundaries.

## Architecture and Reliability Risks

The principal weakness is inconsistent invariants between ordinary and contextual translation paths. Receipt timestamps are reused as semantic ordering, combined commits lack expected revisions, and contextual tasks have separate cancellation/policy ownership. Native cleanup also treats a timed wait as sufficient to release resources potentially still used by a worker. Diagnostic redaction, loss accounting, long-session tracking and export sessions need stronger contracts. Full evidence/trigger/impact/recommendation/confidence/reproduction fields for all 17 findings are in [CODE-QUALITY-AUDIT.md](CODE-QUALITY-AUDIT.md).

## Test and Benchmark Credibility

Existing release verification at `0ecc3fc` records 12 named suites with exit zero. Stored CI run #6 at exact audit HEAD reports all 12 passing. These are inspected historical records, not new executions. The tests provide useful unit/mock/transport/WPF regression evidence, with optional real model/device/provider paths separately qualified. Missing model fixtures can be reported as passes (AQ-17).

The 30-sample benchmark uses hardcoded translator behavior and predefined scores/error labels; 2.68 -> 4.86 (+80.99%) is fixture arithmetic, not measured accuracy. Its sub-millisecond P50/P95 measure synthetic scheduling with waits disabled, not end-to-end caption latency. Production UI apply telemetry is unwired. Archived real WASAPI/local-model/WPF smoke and a DeepL smoke provide narrow single-host evidence; multi-hour stability, controlled provider comparisons and full memory/latency envelopes remain unverified. Methodology and raw record links: [TEST-AND-PERFORMANCE.md](TEST-AND-PERFORMANCE.md).

## Top P0/P1 Findings

| ID | Priority / type | Most important consequence | Verification |
|---|---|---|---|
| AQ-01 | P1 confirmed API defect | Delayed grouped MT can attach stale translation to revised source | In-memory public store counterexample |
| AQ-02 | P1 confirmed policy/lifecycle gap | Contextual work can outlive cancel/stop and bypass ordinary request guards | Source call-chain proof; adverse runtime interleaving untested |
| AQ-03 | P1 confirmed ordering defect | Revised earlier audio can treat later audio as preceding context | In-memory store plus actual selection predicate |
| AQ-04 | P1 conditional stability risk | Native decode resources can be freed before worker exit | Static unsafe ordering; no hang/crash induced |
| AQ-07 | P1 confirmed evidence defect | Fixed-score benchmark misrepresents measured translation improvement | Fixture/aggregator/fake-provider source and JSON inspected |

No confirmed P0. AQ-09 sanitizer and AQ-13 multi-session export counterexamples were also reproduced; neither required real credentials, device access or file output.

## Technical Debt

Large desktop composition/control classes, dense provider code, broad model discovery and legacy framework/version-pinned scripts increase change risk. More consequential debt is evidentiary: overwritten fixed test artifacts, synthetic measurements labeled too broadly, missing skip outcomes, incomplete release-input checks and no locked dependency closure. Avoid a broad rewrite; strengthen invariants and validation first.

## Recommended Next Steps

1. Validate all contextual group members/revisions and use stable transcript ordering (AQ-01/03).
2. Unify cancellation, request policy and attempt ownership for all MT work (AQ-02).
3. Make native/hybrid teardown and Azure resource ownership deterministic (AQ-04/11).
4. Replace fixed-score claims with actual-output assertions and independent quality evaluation (AQ-07/17).
5. Correlate capture-to-render timing and per-stage loss, then optimize measured bottlenecks (AQ-05/06).

Privacy, session export, long-run resource bounds, UI/device validation and exact release evidence follow in [DEVELOPMENT-ROADMAP.md](DEVELOPMENT-ROADMAP.md). Each work package specifies benefit, risk, complexity, dependencies, acceptance criteria and verification. No Phase 5.5.5 implementation is included.

## Audit Execution and Preservation

| Operations actually executed | Result / boundary |
|---|---|
| `git status --short --untracked-files=all`, `git branch`, `git log`, `git show`, `git tag`, `git rev-parse`, `git diff` | Read-only inventory/history/version comparison; main/HEAD/tag preserved; no fetch/push/commit/reset/clean/rebase |
| `rg --files`, targeted `rg -n`, `Get-Content`, XML/JSON parsing and directory metadata | Traced production modules, project references, tests, scripts, workflow and historical evidence; credentials/settings contents not opened |
| `Get-FileHash`, ZIP `OpenRead`/entry streams, executable VersionInfo | Recomputed final ZIP/EXE/model identity without extraction, rebuilding or execution |
| PowerShell `Assembly.LoadFrom`, synthetic store/request predicates, `Sanitize`, `ToSrt` | Four safe in-memory probes corroborated AQ-01/03/09/13 using an identified existing Core binary; no network/persistent writes |
| `apply_patch` | Created/edited only this audit's nine new Markdown files |
| Documentation/reference/preservation checks | Nine reports, 398 links/352 source-line references, eight Mermaid diagrams and 17 finding IDs checked; no missing targets, out-of-range lines, unbalanced fences, unknown finding IDs or non-English report text |

Full verification/build/packaging/live tests were skipped because they write existing project locations, create local listeners, access devices/models or saved credentials, or issue external requests. No dependencies were installed, no external services started and no application settings changed. This is a deliberate scope boundary, not an unexplained green test claim.

Final preservation comparison: all 272 pre-existing tracked/untracked file hashes match; Git index, HEAD, branch and tags match their snapshots. `git diff --name-only HEAD` is empty. Untracked file count is 74: the original 65 plus these nine reports. No existing local file was deleted or changed. Ignored artifact streams read for provenance were not modified.

New-file inventory: `PROJECT-OVERVIEW.md`, `ARCHITECTURE.md`, `ASR-AND-TRANSLATION.md`, `UI-ARCHITECTURE.md`, `CODE-QUALITY-AUDIT.md`, `TEST-AND-PERFORMANCE.md`, `BUILD-AND-RELEASE.md`, `DEVELOPMENT-ROADMAP.md`, `FULL-PROJECT-AUDIT.md`, all under `docs/audits/phase5.5.4/`. Cross-report terminology, source correspondence and state/sequence diagrams were manually reviewed; automated checks validated references, line bounds, language and fences. Mermaid was statically checked, not rendered with a diagram engine. No external links were fetched solely for documentation validation.

## Final Assessment

The implementation is understandable and extensible enough for focused reliability work; its ordinary caption/revision flow and unified UI are substantial foundations. Treat contextual correctness, native lifetime safety and evidence credibility as prerequisites for stronger release-quality claims. Existing green suites and portable smoke support a functional beta, while broader translation accuracy, complete end-to-end latency and long-running resource stability remain unverified.
