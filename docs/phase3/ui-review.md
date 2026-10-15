# Dynamic provider UI review

Reviewed 2026-10-14: task-ui.md, task-ui-report.md, ui-review.patch, current SettingsWindow/MainWindow/DesktopSmoke, ProviderDescriptors, SettingsStore and SubtitleStore integration. Existing phase3-ui/desktop-smoke.json inspected: 20 checks report pass. No tests rerun, no network requests, no implementation edits.

## Verdicts

- **Spec: changes required for custom-host credential reuse.** Independent selectors, descriptor fields, masking/deletion, configuration-only status, capability labels, save error reporting and caption-generation clearing otherwise match the assigned task.
- **Quality: changes required for the same credential destination issue; otherwise no blocking UI correctness issue identified.** A bounded model-list request lifecycle and broader save/switch tests are follow-up improvements.

## Finding

### P2 — Changing a custom endpoint silently reuses the previous host's masked key

Location: `prototype/src/AntiDbt.Desktop/SettingsWindow.cs:91-95,107-111`.

With an existing OpenAiCompatible profile for host A and its saved key, editing only Base URL to host B keeps the PasswordBox value, and WriteMt/save stores both the new endpoint and old key unchanged. ProviderHttp correctly disables redirects, but custom providers intentionally allow user-entered hosts; the next start therefore sends A's key to B. SettingsStore's encrypted endpoint binding does not prevent this UI path: SaveAsync binds the retained key to the newly supplied endpoint. Provider-ID isolation is working, but one custom provider ID can represent multiple vendors. This falls under REQUEST.md section 5's requirement to prevent unintended credential forwarding to a different host.

Compare the new endpoint's normalized authority with that associated with the loaded credential before snapshot/save. On an authority change, clear the retained credential and require its re-entry for the new destination, or obtain explicit confirmation naming the credential destination. Preserve same-authority path edits. Add a synthetic editor/save check: load A+key, change only host to B, and assert the old key cannot be sent or silently rebound; also cover switch-away/back so intermediate snapshots cannot bypass the protection. This finding concerns reuse of an existing credential, not a ban on custom URLs.

## Nonblocking follow-ups

- `prototype/src/AntiDbt.Desktop/SettingsWindow.cs:177-189`: FetchGroqModelsAsync receives CancellationToken.None. Closing/canceling the settings window leaves the authorized request running and its continuation updating detached controls. The HTTP helper bounds the request, so this is not an indefinite hang. Prefer a dialog-lifetime cancellation source, cancel on close, and suppress late updates. No credential appears in displayed exception text.
- `prototype/src/AntiDbt.Desktop/DesktopSmoke.cs:78-100`: added checks verify editor memory state, masking and capabilities, but do not exercise Save/Result, reopening after persisted settings, cancel preserving original settings, a settings write failure, or an active old pipeline receiving a late response after switching. Keep the existing 20/20 claim scoped to what actually ran; add focused tests when these paths change. Storage tests elsewhere may cover DPAPI independently, but the WPF checks do not establish persistence round-trip by themselves.

## Confirmed behavior and limits

- ASR/MT selectors have independent dictionaries and snapshots. Switching provider IDs restores the correct profile/default and does not copy another provider's key. Record/options/secrets replacements keep cancel-time edits from mutating original profile dictionaries.
- All descriptor secrets use PasswordBox, including Google service-account JSON. ClearSecrets clears ApiKey and prior secret dictionary contents for the selected service. Hidden retained secret values are dropped; empty descriptor secret entries may remain, with no credential value.
- Selected and non-selected profiles are returned in Result. Incomplete credentials can be saved; MainWindow uses descriptor completeness checks before starting either provider, including Google JSON credentials. Timeout/debounce/context controls are intentionally common settings, as task-ui.md specifies.
- Configuration check is local and explicitly says authentication/real requests are unverified. Groq model fetch requires a button click, calls the fixed official model-list URL, does not honor an arbitrary endpoint for credential forwarding, and its success message is limited to model-list retrieval. No paid speech/translation calls are triggered automatically.
- Endpoint UI rejects non-HTTPS/WSS, loopback, userinfo, query and fragment. It does not itself validate each provider's exact scheme/host; runtime adapters enforce that. A successful local completeness check is not proof that a specific URL/model/region is supported, consistent with the declared check contract.
- MainWindow's busy guard wraps settings; StopCaptureCoreAsync awaits old pipeline disposal before opening the dialog. Accepted settings call store.BeginSession() with clear=true before rendering, invalidating old generation results and clearing source/translation/history. Existing projection, font, scrolling and dragging code is unchanged.
- SettingsStore encrypts all profile secret values and checks encrypted endpoint authority on load. MainWindow reports save failure and does not replace it with the success label. This storage protection does not resolve the explicit UI rebind finding above.
- Existing 20-check WPF artifact is actual recorded local UI execution, not cloud authentication, live model-fetch, full service-switch integration, or long-run evidence. Review did not repeat it.
