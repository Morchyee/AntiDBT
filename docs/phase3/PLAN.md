# Phase 3 implementation plan

Spec: REQUEST.md copied verbatim from the user's attachment. Existing baseline commit preserves Phase 2; no original installation or existing release writes.

## Tasks

1. Root: audit existing contracts, add provider-neutral structured errors, profile persistence/migration, HTTP safety/retry/cooldown and capability catalog.
2. Root: Groq Whisper segmented PCM/WAV/VAD adapter, DeepL/Azure Translator/Google Basic/Groq LLM/custom OpenAI MT. Protocol tests with dummy credentials and injected HTTP responses/localhost; preserve Volc code.
3. SDK implementer: official Azure Speech and Google STT V2 SDK adapters, isolated CloudSpeech project; real SDK calls plus injectable SDK gateway fixtures. Read task-sdk.md. One implementation child only; no workers edit shared contracts/UI/catalog.
4. Root: dynamic independent ASR/MT settings, per-provider secure profiles, validation vs live status, model fetch and lifecycle/cross-combination tests.
5. Review each task; broad final review. Run regression suites, package into new phase3 directory, no existing release overwrite, local commits, update docs/progress. Real cloud unverified without user-entered keys.

## Preflight

| Tasks | Shared interface / risk | Decision |
|---|---|---|
| 1/2 | Typed errors + HTTP retry vs Scheduler deadlines | Keep existing interfaces; cancellation always stops retries/cooldown. No automatic cross-vendor fallback. |
| 1/3 | Core contracts vs SDK adapters | Freeze IAsrSession/AsrResult signatures; expose ProviderException in Core before dispatch. |
| 1/4 | Profile dictionaries vs DPAPI | Public configs JsonIgnore secrets; encrypted envelope v2 contains every profile; v1 migration supported. |
| 2/4 | Catalog field names / settings | Fields share exact config keys, factory consumes selected independently. |
| 3/5 | SDK requires runtime dependency | User expressly requires SDKs; restore official NuGet only, no system install. |
| Each | Protocol tests vs real service acceptance | Mock SDK gateway tests clearly distinct from actual cloud/gRPC-wire acceptance. |

User requests independent development commits. Repository was absent; initialized Git inside prototype and committed existing baseline. Ignore generated releases/data/caches. No push or public publication. User requests persistent progress; retain ledger/docs rather than deleting them.
