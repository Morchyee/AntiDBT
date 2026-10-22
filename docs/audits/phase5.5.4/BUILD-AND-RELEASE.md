# Build and Release Audit

This is source/provenance documentation. No restore, build, packaging, release script, Git mutation or remote release operation was executed during this audit. Commands below are reference commands for a separately authorized development run.

## Prerequisites and solution

| Input | Actual configuration | Evidence |
|---|---|---|
| Host/architecture | Windows desktop; packaged RID `win-x64`; WASAPI/WPF/DPAPI and native ASR/Speech libraries | [Desktop.csproj:3](../../../src/AntiDbt.Desktop/AntiDbt.Desktop.csproj#L3), [publish-local-asr.ps1:25](../../../scripts/publish-local-asr.ps1#L25) |
| SDK | `10.0.402`, stable only, `rollForward: latestFeature` | [global.json:3](../../../global.json#L3) |
| Frameworks | Core, Providers, CloudSpeech: `net10.0`; Windows/Desktop: `net10.0-windows` | [Core.csproj:2](../../../src/AntiDbt.Core/AntiDbt.Core.csproj#L2), [Windows.csproj:1](../../../src/AntiDbt.Windows/AntiDbt.Windows.csproj#L1) |
| Compiler | Nullable, implicit usings, latest language, deterministic build | [Directory.Build.props:3](../../../Directory.Build.props#L3) |
| NuGet | nuget.org, repository-local `.packages` | [NuGet.Config:5](../../../NuGet.Config#L5) |
| Native model | English streaming Zipformer 20M; separate untracked/ignored model assets, not downloaded by app | [SherpaOnnxStreamingAsrProvider.cs:51](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L51), [.gitignore:31](../../../.gitignore#L31) |
| Manifest | As-invoker; PerMonitorV2; active UTF-8 code page for native Unicode model paths | [app.manifest:4](../../../src/AntiDbt.Desktop/app.manifest#L4) |

`AntiDbt.sln` includes five production projects and eight test/diagnostic projects (13 total); `AntiDbt.Sticky.Live` is a ninth test/tool project outside the solution. Project dependencies are documented in [Project Overview](PROJECT-OVERVIEW.md) and [Architecture](ARCHITECTURE.md).

Reference PowerShell, from `D:/Desktop/AntiDBT-Gemini/antiDBT/prototype`:

```powershell
dotnet restore AntiDbt.sln --disable-parallel
dotnet build AntiDbt.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
pwsh -File scripts/verify-all.ps1 -ArtifactsDir artifacts/<fresh-run-name> -SkipRestore
```

The driver independently builds Debug outputs even after a Release build. `-SkipRestore` skips restore, not build. Full side effects and optional hardware/cloud paths are described in [Test and Performance](TEST-AND-PERFORMANCE.md). Use a fresh development/evidence context; `-ArtifactsDir` does not relocate every child-suite output.

## Dependencies, native files and licenses

Direct versions are pinned in project files: NAudio.Wasapi 2.3.0, sherpa-onnx 1.13.8, Azure Speech 1.52.0, Google Speech V2 1.8.0, Alibaba Alimt SDK 2.4.2. Core has no external package dependency. SDK/signing packages introduce additional transitives; no tracked package lock captures the complete closure ([Windows.csproj:1](../../../src/AntiDbt.Windows/AntiDbt.Windows.csproj#L1), [Providers.csproj:1](../../../src/AntiDbt.Providers/AntiDbt.Providers.csproj#L1), [CloudSpeech.csproj:1](../../../src/AntiDbt.CloudSpeech/AntiDbt.CloudSpeech.csproj#L1)). This is inventory, not a current vulnerability advisory assessment.

The local-ASR publisher reads resolved `project.assets.json`, copies package-provided legal material and writes dependency metadata; runtime legal material is selected from cached runtime package versions. The latter should be bound to the exact resolved runtime in future reproducibility work (AQ-12; [publish-local-asr.ps1:41](../../../scripts/publish-local-asr.ps1#L41), [53](../../../scripts/publish-local-asr.ps1#L53)).

Model provenance is recorded in [ZIPFORMER-20M-SOURCES.json](../../../licenses/upstream/ZIPFORMER-20M-SOURCES.json). Native/model legal source hashes are recorded in [LOCAL-ASR-LEGAL-HASHES.json](../../../licenses/upstream/LOCAL-ASR-LEGAL-HASHES.json). Notices and upstream license files accompany the package. Their presence establishes packaged attribution/provenance material, not a legal compliance opinion or permission for every future model.

## Authoritative current portable path

`scripts/publish-local-asr.ps1` is the tracked source for the final local-ASR package:

1. Requires a fresh output below `artifacts/`; preserves an existing output instead of deleting it ([9](../../../scripts/publish-local-asr.ps1#L9)).
2. Reads version from build properties and source commit from Git; checks selected-path cleanliness and a passing verification summary ([13](../../../scripts/publish-local-asr.ps1#L13)). The check is incomplete for untracked/root inputs (AQ-12).
3. Publishes Release, win-x64, self-contained, single-file managed application, native self-extraction disabled, no PDB, serial MSBuild ([25](../../../scripts/publish-local-asr.ps1#L25)). Native DLLs remain adjacent files.
4. Copies multilingual READMEs, LICENSE, notices, legal tree and deployment guide; copies only the four manifest-listed model files after verifying hashes ([27](../../../scripts/publish-local-asr.ps1#L27), [35](../../../scripts/publish-local-asr.ps1#L35)). Test WAVs are excluded.
5. Checks required native DLLs and rejects WAV/log/PDB/settings/data contents; writes version/commit/SDK/framework/runtime/model/verification manifest ([60](../../../scripts/publish-local-asr.ps1#L60)). Filename checks are not a general secret-content scanner.
6. Runs hidden packaged `--smoke-test` with quoted output path and a 90-second bound; computes hashes and verifies every archived file against staged bytes ([68](../../../scripts/publish-local-asr.ps1#L68), [75](../../../scripts/publish-local-asr.ps1#L75), [87](../../../scripts/publish-local-asr.ps1#L87)). This packaged smoke is synthetic WPF, separate from real WASAPI/model smoke.

Reference only, with an already matching twelve-suite summary:

```powershell
pwsh -File scripts/publish-local-asr.ps1 -Output artifacts/<fresh-release-stage> -VerificationDirectory artifacts/<matching-verification-run>
```

Normal app settings are created under the executable's `data` directory, so portable extraction must be writable for persistence. DPAPI credentials remain bound to the Windows user; copying an encrypted profile to a different account/machine is not portable authentication ([MainWindow.cs:22](../../../src/AntiDbt.Desktop/MainWindow.cs#L22), [SettingsStore.cs:179](../../../src/AntiDbt.Windows/SettingsStore.cs#L179)). Models are bundled for English local ASR; machine translation remains separately configured or same-language passthrough.

## Version and artifact provenance

| Identity | Observed value |
|---|---|
| Audit branch / HEAD / local origin/main reference | `main` / `765caa53264fbb8ce8fc07faf92c19d2790d769b` |
| Release naming | User-facing v0.5.5.4 maps to Git tag/version `v0.5.5-beta.4` / `0.5.5-beta.4`; assembly/file version `0.5.5.4` |
| Annotated tag target commit | `0ecc3fc8526982c651460cd65957c96031b92130` |
| Final local ZIP | `artifacts/release-0.5.5.4-beta/dist-final/antiDBT-v0.5.5-beta.4-win-x64-full.zip` |
| ZIP size / SHA256 | 112,575,330 bytes / `7301D84C71EA4ECD7DEB91E4C01EE34EE703D4B4FE8C58B38B13B1A34AF00D65` |
| ZIP executable SHA256 | `55F243B3FBFA28A9DC98555225FED838CF9E660A28252E38F5093CF32E0827CC` |
| Staged executable metadata | FileVersion `0.5.5.4`, ProductVersion `0.5.5-beta.4+0ecc3fc8526982c651460cd65957c96031b92130` |
| ZIP release manifest | Same release commit/version; SDK 10.0.402; net10.0-windows; win-x64 self-contained; 12 verification suites/passes |

These values were freshly read/hashed locally in this audit. The ZIP has 113 file entries rooted under `antiDBT/`, required Sherpa/ONNX Runtime/Azure Speech native libraries, four model files, documentation and legal/dependency material. No WAV/log/PDB/settings/data/bin/obj entries were found by the inspected path filters. ZIP streams were read without extraction or execution.

All four ZIP model streams matched their source manifest SHA256 values:

| File | Bytes | SHA256 |
|---|---|---|
| encoder-epoch-99-avg-1.int8.onnx | 42,845,182 | `3810755CE7C3AB26B42A8BCF39D191308FA27FB0F53358823BA46141D03B7EB3` |
| decoder-epoch-99-avg-1.int8.onnx | 539,499 | `21E2A2ACD961B3AC72F55BE2F10F1A285E1B0B0BA010D7C0B6EAB141411B163C` |
| joiner-epoch-99-avg-1.int8.onnx | 259,572 | `E085D73B593CF9B0707F370DBD656D58327D3FE36D80D849202EF81DF02CB01E` |
| tokens.txt | 5,048 | `49E3C2646595FD907228B3C6787069658F67B17377C60AEB8619C4551B2316FB` |

Existing [GitHub download verification](../../../artifacts/release-0.5.5.4-beta/github-download-verification.json) and [release asset record](../../../artifacts/release-0.5.5.4-beta/github-release.json) identify the same ZIP hash/size and release URL, [v0.5.5-beta.4](https://github.com/Morchyee/AntiDBT/releases/tag/v0.5.5-beta.4). This audit did not refresh remote release metadata or redownload assets. Local hash, embedded revision, tag and archived upload/download evidence establish consistent provenance; no independent bit-for-bit rebuild was attempted.

Current main contains the release source plus README commit `dbdc141` and resize-smoke commit `765caa5`. No Core source changes exist between the release and audit HEAD. The older untracked phase report's ZIP hash `F55DE2A9899E472791F279CEDA39DB1492377BF569F667BDFEB1095DFDC36621` identifies a different earlier artifact, not the final uploaded binary. This is an evidence-version distinction, not missing source synchronization.

## CI and release strategy

[windows-ci.yml](../../../.github/workflows/windows-ci.yml#L1) triggers on main/feature pushes and main pull requests; windows-latest, 30-minute job timeout, SDK setup from global.json, restore, Release build, then the twelve-suite Debug driver. It uploads summary, telemetry sample and root logs. Desktop subdirectory PNG/JSON artifacts are not included in the current upload pattern. NuGet cache targets the default user cache while NuGet.Config uses `.packages`, so that cache does not cover the configured repository package folder (AQ-15; [workflow:24](../../../.github/workflows/windows-ci.yml#L24), [NuGet.Config:7](../../../NuGet.Config#L7)).

There is no automated release-upload workflow. Existing creation/update scripts hardcode older releases, obtain authentication through the credential helper and can create/update releases or delete/re-upload matching assets. They were read statically, never executed ([create-release.ps1:2](../../../scripts/create-release.ps1#L2), [101](../../../scripts/create-release.ps1#L101), [create-release-v0.5.3.ps1:131](../../../scripts/create-release-v0.5.3.ps1#L131), [update-release-notes.ps1:95](../../../scripts/update-release-notes.ps1#L95)). No credentials were retrieved.

## Legacy entry points and remaining gaps

| Script | Intended historical role | Current limitation |
|---|---|---|
| `publish.ps1` | Basic self-contained publish and legal/docs copy | Does not itself bundle/verify current English model or full release gate |
| `publish-phase3.ps1`, `publish-phase4.ps1`, `publish-phase5.ps1` | Phase-specific native/legal metadata/ZIP | Different artifact structures/gates; phase5 smoke path quoting/wait issue |
| `build-candidate-package.ps1` | Phase5.3 candidate packaging | Defaults/overrides old assembly/file version; can remove existing scoped output |
| Untracked `publish-phase5.5.4.ps1` | Earlier phase5.5.4 package experiment | Not tracked final release workflow; copies model directory recursively and has different provenance/gates |
| `test.ps1`, `test-phase4.ps1` | Earlier test orchestration | Hardcoded net9 paths incompatible with current framework outputs |
| `run-live-telemetry.ps1` | Hardcoded older packaged process observation | Points at phase5.3; not evidence of v0.5.5.4 full pipeline performance |

Source: [publish.ps1:10](../../../scripts/publish.ps1#L10), [publish-phase3.ps1:1](../../../scripts/publish-phase3.ps1#L1), [publish-phase4.ps1:1](../../../scripts/publish-phase4.ps1#L1), [publish-phase5.ps1:57](../../../scripts/publish-phase5.ps1#L57), [build-candidate-package.ps1:74](../../../scripts/build-candidate-package.ps1#L74), [publish-phase5.5.4.ps1:85](../../../scripts/publish-phase5.5.4.ps1#L85), [test.ps1:20](../../../scripts/test.ps1#L20), [run-live-telemetry.ps1:1](../../../scripts/run-live-telemetry.ps1#L1).

Highest-value release work is stronger exact-input/skip-aware verification, immutable evidence, resolved dependency/runtime pinning, separate synthetic versus real-device gates, and explicit documentation of the authoritative publisher (AQ-12/15/17). Published tags/binaries were preserved; no new release is proposed by this audit.
