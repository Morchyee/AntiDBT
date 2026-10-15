# Combined final-review fixes

2026-10-15. Scope limited to TranslationScheduler, GroqWhisperProvider, ProviderHttp, HttpTranslationProvider, Core.Tests and Multi.Tests. No UI, SDK, original Volc algorithm, release scripts or commits changed by this wave.

## Changes

- Groq keeps one ProviderHttp per IAsrProvider instance, so Retry-After survives session disposal/replacement and request timeout. The store is instance-local, not global or shared across provider/credential instances. Existing queues and error behavior remain.
- Scheduler uses a cancellable per-attempt waiter: a newer Final cancels obsolete work and wakes pending debounce, even if the plugin ignores cancellation. Latest is published before cancellation because waiters may resume synchronously. Two logical slots dispatch queued Final work before previews. Nonfinal updates still use the fixed coalescing window. Existing 128-job/cache bounds, outstanding uncooperative request guard of 16, session cancellation, stale revisions and provisional application remain.
- HTTP error parsing checks JSON root/error/property kinds. Malformed optional metadata cannot bypass 401/403 Authentication or 429 cooldown. Errors remain sanitized; malformed success payloads remain InvalidResponse.
- Azure-only source/target normalization maps zh/zh-CN/zh-SG/zh-Hans to zh-Hans and zh-TW/zh-HK/zh-Hant to zh-Hant; en-US remains en. Microsoft Learn language-support table lists Simplified zh-Hans and Traditional zh-Hant: https://learn.microsoft.com/en-us/azure/ai-services/translator/language-support (opened and checked this run). No empirical service rejection claim.

## Behavioral RED and GREEN

Evidence is in artifacts/phase3/final-fixes-*.json and corresponding logs.

- Before production edits: Core 35 assertions, 3 new failures; all original 32 pass. Failures demonstrate uncooperative preview blocking Final, pending debounce blocking Final, and queued preview taking a Final slot.
- Before production edits: HTTP 86 assertions, 19 new failures; all original 64 pass. Failures demonstrate 10 malformed 401/403 shapes, malformed 429, 7 Azure aliases, and replacement Groq session submitting before cooldown. Three malformed 200 cases already pass as InvalidResponse.
- Final GREEN: Core 36/36, HTTP 86/86. Added same-revision Final upgrade regression after the initial RED. Controlled task barriers cover an unreleased uncooperative preview and contention with exactly one released slot; a start barrier with a 600ms bound verifies bypass of a pending 2000ms debounce.
- Groq actual-clock fixture uses 1s configured request deadlines and 3s Retry-After; feeds voice through replacement sessions, verifies no early request, bounded user stop during cooldown, and successful submission after expiration. This is actual timing, not virtual time.
- Initial HTTP dotnet-run build invocation failed without a behavioral result; a separate serialized build succeeded and the compiled DLL produced the saved genuine RED. That build failure is not counted as RED.
- One intermediate GREEN iteration exposed synchronous cancellation reentrancy; fixed before final evidence. Final failures are zero.

## Executed validation and limits

Serialized dotnet builds used workspace DOTNET_CLI_HOME, MSBuildEnableWorkloadResolver=false, --no-restore -m:1 -p:UseSharedCompilation=false. Core build: 0 errors, 1 NU1900 warning. Multi build: 0 errors, 4 NU1900 warnings. Warnings concern unavailable NuGet vulnerability metadata; no successful vulnerability audit is claimed.

Final localhost integration: 13/13 checks over 10 combinations, 6 WAV uploads, 18 gateway writes, 1 WebSocket and 1 Final frame. The default sandbox run reached zero WAV/write/socket operations and failed all 13 checks due restricted networking; preserved as final-fixes-combinations-sandbox-blocked.{log,json}. Approved localhost-only escalated compiled-DLL run produced final-fixes-combinations-green.{log,json}. SDK gateway fixtures are not native Azure/gRPC wire acceptance.

No real cloud calls, tool installations or repeated unchanged Windows/SDK suites. Real authentication, quality, billing, cloud latency, long-duration hardware/cloud behavior remain unverified. Root owns independent full verification and fresh r2 packaging after scoped review. Build slot released after this report.
