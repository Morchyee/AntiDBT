# Whole-branch Phase 3 final review

Reviewed 2026-10-15 against `REQUEST.md` and `PLAN.md`, using the requesting-code-review/code-reviewer rubric. Baseline: cdf682f; scope: `artifacts/phase3/final-review.patch` plus current source, including committed and uncommitted work. Read-only review; no builds, test reruns, network, source/index/HEAD mutations or subagents. This report is the only file written.

## Strengths

- Actual independent ASR/MT adapters and dynamic provider profiles exist. Groq is honestly final-only local VAD/file transcription, Azure uses the official native Speech SDK, and Google uses official V2 duplex streaming. Original Volc algorithm files are absent from the change set.
- Credentials remain DPAPI CurrentUser encrypted, including inactive profiles. The corrected v2 restoration checks the binding of the actual selected credential; UI authority changes clear credentials, and production HTTP disables redirects. Previous scoped credential, overlap, concurrent cooldown, SDK receive and cleanup fixes remain present.
- Session generations and revision-aware subtitle application protect against stale callbacks; bounded audio/job/result queues prevent unbounded accumulation. SDK operation cancellation invalidates the transport rather than allowing later writes to an abandoned operation.
- Evidence is candid: localhost pipeline combinations exercise real HTTP/WebSocket paths, while SDK seams are explicitly not native-cloud/gRPC-wire acceptance. The inspected native-ui.json records normal portable launch, empty keys, Start opening settings, cancel, UI close, and zero remaining processes. The inspected soak.json is explicitly a 62-second synthetic run, not a cloud soak.

## Issues

### Critical (Must Fix)

None identified.

### Important (Should Fix)

1. **P2 — Groq reconnect discards an outstanding server cooldown.** `src/AntiDbt.Providers/GroqWhisperProvider.cs:13,30,77`; `src/AntiDbt.Providers/ProviderHttp.cs:25-26,47,59`; `src/AntiDbt.Core/CaptionPipeline.cs:81,160`.

   Trigger: a live Groq request receives HTTP 429 with Retry-After longer than its configured request timeout (for example 60 seconds versus the default 30). ProviderHttp records the deadline but its wait is interrupted by the request deadline and becomes Timeout. The Groq pump terminates, the pipeline reconnects after 500 ms, and the new Session constructs a new ProviderHttp with no cooldown. Continued speech can therefore submit a new chargeable request before the original Retry-After expires. This is an automatic production path, not the user deliberately starting a new provider instance. Existing tests cover cancellation within one instance and concurrent cooldown, not this reconnect boundary. Preserve cooldown across sessions of the same Groq provider, or propagate the remaining rate-limit deadline through timeout/reconnect; test with a shortened request timeout and longer cooldown while feeding continuing audio.

2. **P2 — Final translations have no priority over an in-flight preview.** `src/AntiDbt.Core/TranslationScheduler.cs:35-38,65-67,95`.

   Trigger: a Partial starts a slow HTTP/LLM translation; a corrected Final arrives while that request is pending. Schedule merely updates Latest; ProcessAsync remains awaiting the old preview until it completes or the configured timeout expires (up to 120 seconds). Final also cannot interrupt an already-running debounce, and unrelated queued previews share the same undifferentiated slot queue. Generation/revision checks prevent stale overwrite, but do not provide the Final priority required by REQUEST section VII and claimed by API_PROVIDERS.md. This scheduler behavior predates Phase 3; the new slow-provider support and explicit Phase 3 requirement make it an unresolved requirement gap, not a newly introduced regression. Give a newer Final an explicit cancellation/wakeup path for the obsolete preview and arrange queued Final work ahead of previews while preserving bounded concurrency and stale-result checks. Add a controlled pending-preview fixture proving Final starts before releasing or timing out the old preview; retain the uncooperative-provider and generation-isolation tests.

3. **P2 — Malformed JSON error envelopes bypass the HTTP status classification.** `src/AntiDbt.Providers/ProviderHttp.cs:69-75`.

   Trigger: an HTTP 401/403/429 response contains valid JSON with a non-object root (`[]`, `null`, or a string), or an error object with numeric/object `status`. TryGetProperty/GetString throws InvalidOperationException, but Classify catches only JsonException. Consequently a 401 no longer becomes Authentication, and a 429 never establishes cooldown. MT falls back to a generic failure; Groq converts the exception to Service and the pipeline retries instead of treating authentication as terminal. Prior review mentioned hardening as outside its narrow correction gate; this whole-branch review establishes the concrete terminal-auth/cooldown consequence. Shape-check root/error/status before reading and preserve status-code classification when optional error metadata is malformed. Add malformed-401 single-attempt/category and malformed-429 cooldown tests. Do not include server bodies in messages.

### Minor (Nice to Have)

No additional stylistic findings. The dense one-line new code makes lifecycle auditing harder, but broad formatting is not necessary for this correction gate.

## Recommendations

Resolve the three behavioral findings with focused regression cases, then rerun affected Core/HTTP suites and any changed integration paths. If code changes after the current portable build, publish to another new directory and repeat package smoke so the delivered executable corresponds to the reviewed final source. Refresh delivery documentation with the coordinator's latest verified package results.

## Evidence and limits

The coordinator reports Core 32, Windows 19, original Volc 26, HTTP 64, SDK 36, WPF 26, combinations 13 checks/10 combinations, native SDK construction/disposal without recognition, and synthetic soak passing. Current test source and earlier scoped review artifacts substantiate the nature of those checks; this reviewer did not independently rerun them. Existing integration review correctly bounds SDK gateway substitution. Package/no-key native UI and synthetic soak JSON were directly inspected. Passing artifact counts do not cover the three triggers above.

## Declined to judge

- Real cloud authentication, recognition/translation quality, regional model availability, billing, and real latency: no keys or cloud requests; explicitly unverified, not used as a merge blocker.
- Hour-scale cloud/hardware stability and synchronous native SDK worst-case blocking: the 62-second synthetic soak and native construction smoke cannot establish these properties; retained as explicit acceptance unknowns.
- Google cross-connection word-level bridging: documented limitation; stream rotation/reconnect exists, and the request does not require seamless bridging specifically.
- Google Translation Advanced OAuth, Azure Translator VNET/custom resource endpoints, arbitrary OpenAI Responses/SSE/auth schemes: Basic/global/Chat Completions are the stated implemented modes; no hidden claim of these broader modes.
- Dedicated P1 proxy editor: documented absent; no existing safe per-provider proxy mechanism was identified to reuse. This is an explicit P1 limitation, not full P1 completion.
- Full language/region picker expansion and semantic VAD/model-quality improvements: current zh/en target UI and energy VAD limitations are disclosed; no claim of full service-language UI coverage or semantic accuracy.
- User cancellation intentionally abandoning a bounded stop drain, and overload intentionally dropping old queued audio/results: explicit realtime freshness/bounded-memory policies, not lossless transcription guarantees.
- Same-Windows-user hostile processes and compromised trusted HTTPS hosts: DPAPI/authority binding do not promise protection against those threat models.

## Assessment

**Ready to merge? With fixes.**

The branch contains substantial functional implementation and useful layered offline evidence, with no critical security finding identified in this pass. The three deterministic cooldown, Final-priority and malformed-error behaviors should be corrected before declaring the Phase 3 realtime/failure-handling requirements complete; documented real-cloud unknowns remain separate from these code-level blockers.
