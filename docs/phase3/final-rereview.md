# Scoped final-fixes re-review

Reviewed 2026-10-15 against the three findings in final-review.md plus Azure Chinese language mapping and directly introduced breakage. Inspected current TranslationScheduler, GroqWhisperProvider, ProviderHttp, HttpTranslationProvider, affected Core/HTTP test sources, LocalProtocol source, and final-fixes build/RED/GREEN artifacts. No tests rerun, network calls, production edits, index/HEAD mutations or subagents. This report is the only file written.

## Verdict

**Approve the scoped production fixes.** All three reported findings are resolved in the inspected implementation; Azure Chinese aliases are mapped on both source and target paths. No remaining code-level blocker was identified in this scope. **Full-suite and rebuilt-package acceptance remain separate coordinator gates.** The successful affected localhost retry was inspected before completing this review, as described below.

## Dispositions

1. **Groq reconnect cooldown — resolved.** ProviderHttp now belongs to GroqWhisperProvider and is passed into each replacement Session. CaptionPipeline retains the provider while recreating sessions, so a timeout cannot erase its cooldown. The retained max-deadline lock also prevents shorter concurrent limits replacing a longer one. The new recorded fixture exercises a 3-second Retry-After with 1-second request deadlines across replacement sessions, verifies only one request before expiry, verifies disposal during cooldown stays bounded, and verifies submission resumes after expiry. This is direct replacement-session/handler evidence, not an authenticated Groq run or a new whole-pipeline reconnect fixture.

2. **Final priority — resolved.** An attempt-scoped linked token is canceled when a Partial becomes Final or a newer Final arrives. It interrupts both debounce and waiting/request work while preserving the job lifetime; the loop then takes Latest. The explicit two-slot queue selects waiting Final jobs before previews. Cancellation racing with a granted slot releases that reservation, and acquired slots release in finally. Existing outstanding-provider-request limits and late-result observation remain. Recorded new tests prove Final starts before an uncooperative preview is released, wakes debounce, wins a released slot over a queued preview, and handles a same-revision Final upgrade. Existing generation isolation and stale-response cases remain green. An already-running request for an unrelated segment is allowed to finish; the requirement is not interpreted as arbitrary cancellation of all useful in-flight work.

3. **Malformed HTTP error envelopes — resolved.** Classify checks root/error object shape and string metadata before property extraction; 401/403 takes precedence. Invalid JSON still falls back to status classification. Malformed optional JSON cannot throw InvalidOperationException before the 429 cooldown branch. Recorded fixtures cover 401 and 403 with array/null/string roots and numeric/object status fields, asserting Authentication and one attempt; malformed 429 verifies delayed retry; malformed successful responses still become InvalidResponse. Authored messages remain sanitized.

4. **Azure Chinese aliases — approved within stated mapping scope.** AzureLanguage operates on original source/target values before generic language reduction: zh/zh-CN/zh-SG/zh-Hans become zh-Hans; zh-TW/zh-HK/zh-Hant become zh-Hant. Underscores and case are normalized. The request builder applies it to both from and to, retains the expected subscription headers and Text body, and preserves generic handling for other languages. Seven recorded alias fixtures assert both query fields and body; the existing Azure protocol case additionally asserts default zh maps to zh-Hans and en-US source to en. This reviewer did not browse official documentation; the coordinator's documentation verification remains separate evidence. No claim of exhaustive BCP-47 mapping is made.

## Evidence actually inspected

- final-fixes-core-red.json: 35 total, 3 behavioral failures — pending-preview preemption, debounce wakeup, and queued Final priority.
- final-fixes-http-red.json: 86 total, 19 behavioral failures — malformed auth/error handling, malformed 429, seven Azure aliases, and reconnect cooldown. Three malformed-success fixtures already passed before the fix and are not counted as new failures.
- final-fixes-core-green.json: **36/36**, including the subsequent same-revision Final regression. This supersedes the coordinator's earlier 35/35 count.
- final-fixes-http-green.json: **86/86**.
- Affected build logs: successful builds with zero errors, retaining NU1900 vulnerability-metadata network warnings; these are not vulnerability-audit passes.
- The first affected localhost run recorded 13 failed checks and zero protocol counts; it is preserved as final-fixes-combinations-sandbox-blocked.json/log. The coordinator reports the retry ran with approved localhost execution. Before completing this review, final-fixes-combinations-green.json was directly inspected: **13/13 checks passing, 10 combinations, 6 WAV requests, 18 PCM writes, 1 WebSocket and 1 final audio frame**. This supersedes the failed execution as integration evidence without erasing it. SDK gateways remain injected; this is not native Azure or Google gRPC-wire acceptance.
- task-final-fixes-report.md was not yet present when inspected; no claims in that pending report were assumed.

## Residual load-bearing issues

No unresolved scoped production defect identified. The successful affected localhost retry resolves the earlier socket verification gap. The coordinator must still complete its independent full-suite and new portable-package gates and ensure the executable contains the reviewed source.

## Declined to judge

- Real cloud credentials, service quality, billing, regional availability, SDK native-cloud/gRPC-wire behavior and hour-scale stability: no new evidence; prior explicit limitations remain.
- Broader language/region alias coverage beyond the seven requested Azure Chinese mappings: outside this correction scope and not claimed as complete.
- Repeating the complete branch review or reopening previously dismissed SDK/UI findings: no new evidence implicates them in these four changes.
- Root cause of the all-failed localhost run: source/log inspection alone does not establish whether execution policy, connectivity, or application behavior caused it; the coordinator's independent verification gate must decide.

## Assessment

**Ready to merge? Yes for the scoped fixes; delivery acceptance pending full-suite/package gates.** The fixes address the concrete triggers and have behavioral RED followed by focused GREEN and affected localhost integration evidence. This approval does not declare a rebuilt portable release verified.
