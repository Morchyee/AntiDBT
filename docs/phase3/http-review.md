# HTTP/config review

Reviewed 2026-10-14: task-http.md/report, http-review.patch, current ProviderHttp/HttpTranslationProvider/GroqWhisperProvider/ProviderDescriptors/ProviderCatalog, Core typed-error integration, SettingsStore and Multi/Windows test source. Current source includes the Azure Text serialization and default-prompt fixes. Existing HTTP artifact records 56/56 passing handler cases; Windows artifact records zero failures. No tests rerun, implementation changed, credentials read, or cloud requests made. UI host editing and SDK implementations remain separate reviews.

## Verdicts

- **Spec: changes required.** Actual adapters and bounded local VAD exist, with honest handler-test labeling; profile credential binding, quota classification and overlap correctness have defects below.
- **Quality: changes required.** One credential separation flaw (P1) and three P2 behavior defects should be fixed with narrow regression cases.

## Findings

### P1 — Selected profile binding validates one credential while restoring another

Location: `prototype/src/AntiDbt.Windows/SettingsStore.cs:56-81`.

Load restores selected credentials from the top-level encrypted AsrApiKey/TranslationApiKey, but checks the selected endpoint against the encrypted profile selected by the *plaintext* Provider ID. These need not describe the same credential. Example: save selected DeepL with its key and a nonselected OpenAiCompatible profile pointing to another host. Modify only plaintext selected Translation.Provider to OpenAiCompatible and its Endpoint to that existing custom profile's authority. Both profile bindings and the selected binding pass, but selected Translation.ApiKey is still the original DeepL key. It then overwrites the restored custom profile and can be sent to the custom host. The existing tampered-host test changes only Endpoint, so it does not cover this substitution.

For v2, restore the selected ApiKey/Secrets from the same encrypted Credential object whose endpoint binding was checked; keep top-level selected credentials only for explicit v1 migration. Alternatively cryptographically bind and verify the selected provider identity as well. Add a two-profile plaintext provider+endpoint substitution regression, asserting that the original selected provider's secret is never paired with the other authority. This is distinct from the separately reviewed UI host edit.

### P2 — 429 quota failures never reach the quota classifier

Location: `prototype/src/AntiDbt.Providers/ProviderHttp.cs:42-49,75`.

Every 429 enters cooldown/retry and eventually becomes RateLimited, before Classify can recognize insufficient_quota or RESOURCE_EXHAUSTED. The intended terminal quota behavior therefore cannot work for those 429 bodies, and quota exhaustion is retried and presented as temporary throttling. Classify structured terminal quota codes before deciding whether a 429 is retryable; retain cooldown for genuine rate limits. Add 429 + insufficient_quota and quota-exhausted structured-body cases verifying a single request and Quota, alongside the existing ordinary 429 recovery test.

### P2 — Overlap removal can cut the beginning off a legitimate new word

Location: `prototype/src/AntiDbt.Providers/GroqWhisperProvider.cs:114-122`.

Only the boundary *before* the matched previous suffix is checked. RemoveOverlap("a cat", "catalog entries") matches "cat" and returns "alog entries". Thus a normal change in the next word at a forced cut permanently corrupts a final subtitle. Require a boundary at the end of the matched current prefix too for word-based scripts, while preserving the intentional CJK policy. Add negative partial-word and positive whole-word cases.

Related continuity requirement at `GroqWhisperProvider.cs:18,81`: DropOldest can discard the immediately preceding audio job, but every remaining Job.Overlap still compares against whichever text was last processed. Track processed job identity/time adjacency and suppress overlap removal after a queue gap; test queue overflow with matching text in nonadjacent segments. A bounded queue must not imply that nonadjacent recognition results overlap.

### P2 — Concurrent 429 responses can shorten an already established cooldown

Location: `prototype/src/AntiDbt.Providers/ProviderHttp.cs:46`.

The assignment overwrites coolingUntil unconditionally. With two in-flight translations, response A can establish a 60-second Retry-After and response B arriving later can replace it with one second. Subsequent requests and retries then resume before A's required delay. The scheduler allows multiple concurrent translation requests, so this is a reachable production path. Under the existing lock, retain the maximum of the current deadline and the new deadline. Add a two-request ordering fixture that establishes a long delay then a shorter delay and confirms no request is sent before the longer bound (with cancellation to keep the test short).

## Confirmed implementation and evidence boundaries

- DeepL sends its auth scheme, text array, target language and optional context; Azure now preserves Text capitalization and sends subscription headers/query; Google Basic uses its own API-key contract and decodes returned entities. LLM requests use chat/completions, separate system/user messages, bounded context and the fixed fallback prompt even when the descriptor supplies an empty prompt.
- Official HTTP providers pin hosts; custom endpoints are separately allowed. Requests reject insecure remote schemes/userinfo/query/fragment and production HttpClient disables redirects. Loopback is accepted by the shared helper for fixture use; the normal UI rejects it. Injected test clients require a loopback endpoint. No automatic cross-provider fallback exists.
- ProviderHttp has a total linked cancellation deadline, a 4 MiB response cap and at most two attempts. Whisper disables server-error retries to reduce uncertain duplicate billing. Ordinary caller cancellation is propagated. Shared cooldown is per provider instance, not global across new instances.
- Whisper uses raw PCM16 16 kHz mono WAV, the transcriptions path, local energy-based VAD, a four-job queue, bounded pre-roll and maximum segment size, and final-only results. Silence-only input is tested. Current tests do not validate forced-cut overflow continuity or sustained slow-service behavior. Result queue drops old finals at capacity; this keeps memory bounded but does not preserve a complete transcript under overload.
- Core distinguishes terminal provider configuration/auth/quota/model/language errors from retryable failures, honors reported RateLimited retry intervals and preserves session generation checks. Existing scheduler revision matching/cancellation prevents old translations replacing new revisions. End-of-input uses a finite pipeline drain budget; a large queued Whisper backlog is not guaranteed to drain on stop.
- V1 migration and DPAPI envelope/profile encryption are implemented, and test source exercises an actual v1-shaped envelope. All profile secret maps/API keys are excluded from plaintext Settings serialization. The v2 substitution finding above is not disproved by the current one-profile endpoint tampering test.
- The 56 handler tests cover requests/parsing, selected HTTP status mapping, cancellation, simple Retry-After, a VAD case and factory combinations. The 4 x 6 test constructs providers; it does not run 24 end-to-end pairings. Handler tests do not establish redirect behavior on a real socket or authenticated cloud acceptance. Existing Windows 18-case evidence is local execution, with hardwareRequested=false.
- Error bodies are not displayed; authored ProviderException text avoids secret-bearing URLs. Additional malformed error-envelope cases would improve defensive coverage because Classify currently assumes object/string shapes in some fields.
