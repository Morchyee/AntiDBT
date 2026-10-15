# Phase 3 dynamic settings UI report

## Scope and behavior

Changed only SettingsWindow.cs, the provider-validation/settings-acceptance statements in MainWindow.cs, DesktopSmoke.cs, and this report.

Settings now has independent descriptor-driven ASR and translation selectors. Each switch snapshots the current editor into the corresponding provider profile and restores the new provider's profile or defaults. Result preserves both complete profile dictionaries, including incomplete configurations. All secret fields use PasswordBox, including service-account JSON; clear credentials removes the selected profile's API key and entire secret dictionary. Other providers remain independent.

The UI retains translation debounce, timeout, context count; audio device, target language, font, original-text visibility, and pin settings. Capability/language text comes from the catalog; Groq Whisper final-only segmentation and minimum billed duration are displayed. Seed LiveInterpret stays explanatory gated text. Configuration checking is local and explicitly distinguishes completeness from authentication/live requests. Nonempty endpoint edits require public HTTPS/WSS without userinfo/query/fragment. Groq LLM alone exposes an explicit model-list request, with editable model and a qualified model-list-only success label. No startup/background provider requests were added.

MainWindow now uses descriptor completeness checks for both selected providers instead of generic API-key assumptions, so Google service-account ASR is supported. Settings already stops/disposes the prior pipeline and has a busy guard; accepting settings starts a cleared subtitle generation before rendering. Existing subtitle rendering/scroll/font/drag/size behavior was untouched.

## Verification

Added synthetic WPF smoke checks for selector independence, key isolation, ASR profile restoration, credential deletion surviving switches, Google JSON password control/restoration/deletion, SDK/Groq capability text, and translation model restoration. These use synthetic strings, no credentials, no provider network, and the existing test-mode persistence suppression.

Build and actual WPF smoke completed; see executed evidence below. Authentication, paid speech/translation requests, and live Groq model list intentionally unexecuted.

### Executed evidence

2026-10-14 21:05 local: Desktop Debug build succeeded with `dotnet build prototype/src/AntiDbt.Desktop/AntiDbt.Desktop.csproj --no-restore -m:1 -p:UseSharedCompilation=false`, using workspace-local DOTNET_CLI_HOME after the default CLI-home sentinel was denied. 0 compiler errors; 2 NU1900 warnings because NuGet vulnerability metadata could not be reached (not an audit success).

Actual WPF executable ran `--smoke-test F:\Program\antiDBT\prototype\artifacts\phase3-ui`, exited 0; all 20 checks passed (11 existing + 9 added). Evidence: artifacts/phase3-ui/desktop-smoke.json and five screenshot PNGs. No real credential or provider request was exercised.

### UI review corrections and rerun

2026-10-15 10:06 +08:00: inspected the interrupted fix before continuing. SettingsWindow now binds retained credentials to the normalized endpoint authority. Endpoint edits snapshot immediately: changing authority clears the rendered secret fields, API key, and retained secret dictionary; same-authority path edits preserve credentials. Switching providers and Save/Result use the same snapshot protection. Reopening the saved Result retains the cleared key. A new key entered after the destination change can be saved for that destination.

Groq model retrieval receives the dialog lifetime token; closing the dialog cancels that token. Canceled requests suppress late result/error/control updates. Added a synthetic smoke assertion for cancellation on close. The token wiring and late-update guards were inspected; an in-flight live HTTP cancellation was not exercised.

Executed `dotnet build prototype/src/AntiDbt.Desktop/AntiDbt.Desktop.csproj --no-restore -m:1 -p:UseSharedCompilation=false` with workspace `DOTNET_CLI_HOME=prototype/.dotnet-home` and `MSBuildEnableWorkloadResolver=false`: success, 0 errors, 2 NU1900 warnings for unavailable NuGet vulnerability metadata.

Executed the built WPF `antiDBT.exe --smoke-test F:\Program\antiDBT\prototype\artifacts\phase3-ui-review` and waited for process completion: exit 0, **26/26 checks passed**. Evidence: `artifacts/phase3-ui-review/desktop-smoke.json` (timestamp 2026-10-15T10:06:16.1223551+08:00) and five screenshot PNGs. The six additional checks cover same-authority path retention, authority-change deletion across provider switches, other-provider isolation, saved Result, reopening Result, and dialog-close cancellation. Reopening Result is an in-memory settings round-trip; this UI smoke does not prove encrypted persistence or live authentication. No real credentials, provider network requests, or settings persistence were used.
