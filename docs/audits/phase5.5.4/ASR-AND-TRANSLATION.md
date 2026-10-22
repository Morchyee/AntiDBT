# ASR and Translation

Baseline and scope: [Project Overview](PROJECT-OVERVIEW.md). This report describes current source, including limitations; configuration defaults are not performance guarantees. See [Architecture](ARCHITECTURE.md) for lifecycle diagrams and [Code Quality Audit](CODE-QUALITY-AUDIT.md) for finding IDs.

## Audio acquisition and ownership

`MainWindow.StartConfiguredPipelineAsync` composes a `WasapiAudioSource`, provider-created ASR and MT adapters, settings and the shared `SubtitleStore` ([MainWindow.cs:732](../../../src/AntiDbt.Desktop/MainWindow.cs#L732)). Capture starts only after `IAsrSession.ConnectAsync` succeeds, rather than buffering while a local model loads ([CaptionPipeline.cs:98](../../../src/AntiDbt.Core/CaptionPipeline.cs#L98)).

| Stage | Actual behavior | Evidence |
|---|---|---|
| Endpoint | Selected render device or startup default; an unavailable explicit ID falls back to default. No device-change subscription or seamless hotplug migration | [AudioSources.cs:170](../../../src/AntiDbt.Windows/AudioSources.cs#L170) |
| Conversion | PCM 16/24/32-bit and float32; channel average to mono; stateful area resampling to signed PCM16 at 16 kHz; non-finite float input becomes zero | [AudioSources.cs:11](../../../src/AntiDbt.Windows/AudioSources.cs#L11) |
| Capture buffering | 640,000-byte ring (20 seconds at the normalized format), then a 50-item `DropOldest` channel; usual chunk is 6,400 bytes/200 ms | [AudioSources.cs:70](../../../src/AntiDbt.Windows/AudioSources.cs#L70), [AudioSources.cs:112](../../../src/AntiDbt.Windows/AudioSources.cs#L112) |
| Pipeline buffering | Separate queue: 50 items, maximum age 2 seconds. Drop-oldest and age trimming count discontinuities in this queue | [BoundedAudioQueue.cs:21](../../../src/AntiDbt.Core/BoundedAudioQueue.cs#L21), [CaptionPipeline.cs:130](../../../src/AntiDbt.Core/CaptionPipeline.cs#L130) |
| Silence | Capture loop computes RMS but suppresses extended **exact-zero** PCM after the configured default 7 seconds. This is separate from local/Groq energy VAD | [CaptionPipeline.cs:55](../../../src/AntiDbt.Core/CaptionPipeline.cs#L55), [Contracts.cs:100](../../../src/AntiDbt.Core/Contracts.cs#L100) |
| Streaming sender | Sends with a 5-second deadline, paces backlog, and injects 200 ms zero PCM after 2 seconds without input to support endpointing | [CaptionPipeline.cs:137](../../../src/AntiDbt.Core/CaptionPipeline.cs#L137) |
| Stop/disposal | Source uses a lifecycle gate, stops capture and awaits recording completion; pipeline drains ASR/MT within bounded budgets. Failed stop retains ownership for another disposal attempt | [AudioSources.cs:205](../../../src/AntiDbt.Windows/AudioSources.cs#L205), [CaptionPipeline.cs:435](../../../src/AntiDbt.Core/CaptionPipeline.cs#L435) |

`AudioChunk` carries sample-clock start/duration plus a monotonic capture timestamp. Pipeline anchors associate sent audio with recognition endpoints; they estimate ASR/queue timing rather than word-accurate acoustic latency ([Contracts.cs:5](../../../src/AntiDbt.Core/Contracts.cs#L5), [CaptionPipeline.cs:158](../../../src/AntiDbt.Core/CaptionPipeline.cs#L158), [CaptionPipeline.cs:339](../../../src/AntiDbt.Core/CaptionPipeline.cs#L339)). Loss in the source channel is not included in pipeline queue statistics (AQ-05). Long reconnects favor freshness over lossless transcription.

## Implemented ASR providers

The factory registers six choices, including the local alias `SherpaOnnxStreaming`. There is no automatic ASR-provider failover; reconnects recreate the selected provider's session ([ProviderCatalog.cs:29](../../../src/AntiDbt.Providers/ProviderCatalog.cs#L29)).

| Provider/class | Input and recognition behavior | Lifecycle, revision and failure details |
|---|---|---|
| `VolcengineAsrProvider` | Binary WebSocket protocol; 16 kHz PCM; sends model, utterance, punctuation and ITN options | `ConnectAsync` initializes headers/request; `SendPacket` serializes writes; `Decode` validates bounded packets/gzip; `Reconcile` maps changing provisional suffixes into IDs/revisions/deletions, keeps a finalized watermark and marks split/merge batches. `CompleteAsync` sends negative final sequence; disposal aborts socket before awaiting sender. [Source:73](../../../src/AntiDbt.Providers/VolcengineAsrProvider.cs#L73), [180](../../../src/AntiDbt.Providers/VolcengineAsrProvider.cs#L180), [272](../../../src/AntiDbt.Providers/VolcengineAsrProvider.cs#L272) |
| `GroqWhisperProvider` | HTTP WAV chunks, Final only; not incremental cloud decoding | 10 ms energy VAD, default minimum 1 s, maximum 4 s, silence 600 ms, pre-roll/overlap 200 ms, threshold 0.008. Job queue of four evicts oldest; lexical overlap removal can confuse genuine repetitions. `DroppedJobs` is local to adapter. [Source:25](../../../src/AntiDbt.Providers/GroqWhisperProvider.cs#L25), [34](../../../src/AntiDbt.Providers/GroqWhisperProvider.cs#L34), [67](../../../src/AntiDbt.Providers/GroqWhisperProvider.cs#L67), [129](../../../src/AntiDbt.Providers/GroqWhisperProvider.cs#L129) |
| `SherpaOnnxStreamingAsrProvider` | Local ONNX transducer or Paraformer; 16 kHz/80 mel features; CPU default, two threads, greedy search | `ResolveModelDirectory`/`BuildConfig` find tokens and model files, prefer int8, enable native endpointing. `ConnectAsync` synchronously constructs recognizer/stream. Bounded audio 128/result 256 channels use backpressure; worker decodes, emits managed sentence-policy results, resets native endpoint state and flushes 0.6 s lookahead at EOF. Model/stream are per-session, not a shared model cache. [Source:51](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L51), [117](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L117), [188](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L188), [279](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L279) |
| `HybridStreamingAsrProvider` | Local Sherpa partial/final plus optional Groq correction of completed sentence PCM | 45-second PCM ring, final-job channel 16, result channel 256. `PumpLocal` publishes local handoff atomically before queueing correction; `TakeSentencePcm` uses absolute sample intervals. Single correction worker, default 5-second deadline; accepted cloud correction becomes higher Final revision of the same mapped ID; filtered/failed correction retains local result. Experimental; cloud access requires configuration. [Source:33](../../../src/AntiDbt.Providers/HybridStreamingAsrProvider.cs#L33), [132](../../../src/AntiDbt.Providers/HybridStreamingAsrProvider.cs#L132), [164](../../../src/AntiDbt.Providers/HybridStreamingAsrProvider.cs#L164), [223](../../../src/AntiDbt.Providers/HybridStreamingAsrProvider.cs#L223) |
| `AzureSpeechProvider` | Azure continuous Speech SDK; PCM push stream; Recognizing/Recognized callbacks | `AzureGateway` maps SDK fields to common `SpeechEvent`; `SpeechSession` owns identity, revisions, errors, completion and cleanup. A new utterance ID follows Final. Config uses explicit subscription credentials. [Source:24](../../../src/AntiDbt.CloudSpeech/AzureSpeechProvider.cs#L24), [SpeechSession.cs:24](../../../src/AntiDbt.CloudSpeech/SpeechSession.cs#L24) |
| `GoogleSpeechProvider` | Google Speech V2 streaming, explicit service-account JSON, `long` model and regional language validation | Sends config first, then PCM requests no larger than 15,000 bytes; maps interim/final results through the same `SpeechSession`. Stream renews through pipeline reconnect after its 4.75-minute limit. Injected transport tests are not live Google evidence. [Source:11](../../../src/AntiDbt.CloudSpeech/GoogleSpeechProvider.cs#L11), [41](../../../src/AntiDbt.CloudSpeech/GoogleSpeechProvider.cs#L41), [83](../../../src/AntiDbt.CloudSpeech/GoogleSpeechProvider.cs#L83) |

`SpeechSession` serializes writes, fails rather than silently losing callback results when its 256-item channel fills, unsubscribes handlers and caches a single cleanup task ([SpeechSession.cs:19](../../../src/AntiDbt.CloudSpeech/SpeechSession.cs#L19), [53](../../../src/AntiDbt.CloudSpeech/SpeechSession.cs#L53), [104](../../../src/AntiDbt.CloudSpeech/SpeechSession.cs#L104)). Local native decode can outlast the disposal wait; see AQ-04. `TimedSherpaRecognizer` reads native token timestamps through a version-specific ABI and frees result JSON; upgrades need native-contract tests ([TimedSherpaRecognizer.cs:24](../../../src/AntiDbt.Providers/TimedSherpaRecognizer.cs#L24)).

## Local sentence segmentation

`StreamingSentenceManager` is a managed policy over local recognition hypotheses, not a universal service applied to all cloud outputs. It retains committed prefixes when native reset cannot preserve context; conflicting hypotheses are withheld until a safe endpoint/EOF rather than re-emitted as duplicate history ([StreamingSentenceManager.cs:39](../../../src/AntiDbt.Core/StreamingSentenceManager.cs#L39)).

| Parameter | Default | Validation/clamp |
|---|---|---|
| Silence boundary | 500 ms | 200-3,000 ms |
| Preferred duration | 5 s | 1-30 s; capped by maximum |
| Maximum duration | 9 s | 2-30 s |
| Preferred character count | 120 | 30-1,000 |
| Stable hypothesis window | 300 ms | 100-1,000 ms |
| Energy VAD threshold | 0.008 | 0.0001-0.1 |

Defaults/options: [StreamingSentenceManager.cs:5](../../../src/AntiDbt.Core/StreamingSentenceManager.cs#L5). Native Sherpa endpoint rules separately use 2.4 s trailing silence, 0.5 s trailing silence after speech, and 9 s utterance length ([SherpaOnnxStreamingAsrProvider.cs:174](../../../src/AntiDbt.Providers/SherpaOnnxStreamingAsrProvider.cs#L174)).

The boundary order is EOF, native endpoint, configured silence, stable punctuation, preferred-duration/length with stable whitespace, then maximum duration. Stability is derived from common prefixes of recent hypotheses. Split logic avoids cutting inside a trailing word/subword and uses native word starts when available ([StreamingSentenceManager.cs:55](../../../src/AntiDbt.Core/StreamingSentenceManager.cs#L55), [69](../../../src/AntiDbt.Core/StreamingSentenceManager.cs#L69), [79](../../../src/AntiDbt.Core/StreamingSentenceManager.cs#L79)).

A committed prefix is Final under its current ID; a retained suffix is Partial under the next ID. The first result declares the whole handoff's `BatchSize`; the pipeline collects and applies the batch before scheduling translations ([StreamingSentenceManager.cs:88](../../../src/AntiDbt.Core/StreamingSentenceManager.cs#L88), [CaptionPipeline.cs:295](../../../src/AntiDbt.Core/CaptionPipeline.cs#L295)). An interrupted incomplete batch is not partially published. Synthetic word-continuity tests exercise continuous 30/120-second streams, but do not prove real recognition accuracy or absence of audio loss ([SentencePolicyTests.cs:8](../../../tests/AntiDbt.Core.Tests/SentencePolicyTests.cs#L8)).

## Semantic translation window and dispatch

There is no class named `SemanticTranslationWindow`; the feature is implemented by `TranslationScheduler` and `TranslationRequest.Context/Lookahead`.

1. ASR revisions update source immediately. Old normal jobs for a segment are canceled/replaced; final work takes slot priority over partial work ([TranslationScheduler.cs:203](../../../src/AntiDbt.Core/TranslationScheduler.cs#L203), [536](../../../src/AntiDbt.Core/TranslationScheduler.cs#L536)).
2. Partial requests wait default 350 ms (range 0-2,000). Incomplete Finals wait the semantic window, default 400 ms, clamped to 200-1,000; setting debounce to zero also disables this wait. The implementation uses a fixed delay, not an event-driven early wake on lookahead ([TranslationScheduler.cs:259](../../../src/AntiDbt.Core/TranslationScheduler.cs#L259), [Contracts.cs:66](../../../src/AntiDbt.Core/Contracts.cs#L66)).
3. `IsIncompleteClause` treats terminal punctuation as complete and uses English trailing-word lists for conjunctions, prepositions, auxiliaries, determiners, subordinators and pronouns. It is a heuristic, not a multilingual syntactic parser ([TranslationScheduler.cs:97](../../../src/AntiDbt.Core/TranslationScheduler.cs#L97)).
4. It selects up to the configured 0-12 preceding Final source strings (default four) plus a later segment as lookahead. Selection uses `ReceivedTimestamp`, which changes on revisions, rather than stable audio order: AQ-03 ([TranslationScheduler.cs:330](../../../src/AntiDbt.Core/TranslationScheduler.cs#L330)).
5. A 128-entry cache keys text, languages, context and lookahead. Two logical slots bound ordinary scheduling; normal requests also cap outstanding provider tasks at 16, including tasks that ignore cancellation. Normal timeout is 1-120 seconds ([TranslationScheduler.cs:346](../../../src/AntiDbt.Core/TranslationScheduler.cs#L346), [372](../../../src/AntiDbt.Core/TranslationScheduler.cs#L372), [536](../../../src/AntiDbt.Core/TranslationScheduler.cs#L536)).
6. Normal completion applies only to matching session/segment/revision. Compatible older provisional results can supply display fallback; stale exact results cannot complete a newer revision ([SubtitleStore.cs:264](../../../src/AntiDbt.Core/SubtitleStore.cs#L264), [323](../../../src/AntiDbt.Core/SubtitleStore.cs#L323)).

## Translation adapters and context contracts

| Provider | Transport and request construction | Context behavior |
|---|---|---|
| DeepL | `HttpTranslationProvider.DeepL`, JSON text request | Last four context entries plus lookahead, capped at 2,000 characters, in native `context`; target text stays separate. [Source:50](../../../src/AntiDbt.Providers/HttpTranslationProvider.cs#L50) |
| Groq LLM / OpenAI compatible | `HttpTranslationProvider.Llm`, nonstreaming chat completion | System instruction requests translation of current input only, preservation of terminology and no translation of context; bounded context/lookahead. Source-derived context is embedded in the system message, creating a prompt-injection quality risk. Arbitrary endpoint/model capability is not assumed. [Source:75](../../../src/AntiDbt.Providers/HttpTranslationProvider.cs#L75) |
| Azure Translator / Google Translation Basic | `HttpTranslationProvider` REST adapters | Translate current text; no preceding-context/lookahead mapping. Google key is placed in its request query; request URLs must not be logged. [Source:60](../../../src/AntiDbt.Providers/HttpTranslationProvider.cs#L60) |
| Volcengine MT | `VolcengineMachineTranslationProvider` signed/provider-specific JSON | Current `text_list` only, no context parameter. Owned transport, sanitized business errors and bounded response. [Source:29](../../../src/AntiDbt.Providers/VolcengineMachineTranslationProvider.cs#L29) |
| Baidu Translate | `BaiduTranslationProvider` official endpoint, MD5 protocol signature | Current text; joins returned translation segments. Business limit produces cooldown. [Source:13](../../../src/AntiDbt.Providers/BaiduTranslationProvider.cs#L13), [47](../../../src/AntiDbt.Providers/BaiduTranslationProvider.cs#L47) |
| Aliyun Translate | `AliyunTranslationProvider`, SDK request model/ACS3 signer with owned HTTP transport | Current text only, maximum 5,000 characters; static two-request physical limit. The transport does not delegate redirects/retries to the generated SDK client. [Source:14](../../../src/AntiDbt.Providers/AliyunTranslationProvider.cs#L14), [34](../../../src/AntiDbt.Providers/AliyunTranslationProvider.cs#L34) |

Same-language translation is local passthrough. Backup translation requires opt-in, cost consent and a distinct configured profile. Fallback handles network, timeout, service and rate-limit failures, not authentication/quota/configuration failures ([ProviderCatalog.cs:49](../../../src/AntiDbt.Providers/ProviderCatalog.cs#L49), [TranslationRoutes.cs:25](../../../src/AntiDbt.Providers/TranslationRoutes.cs#L25)). Fallback does not select a different ASR provider.

## Contextual revision and atomic display replacement

When a new Final follows an incomplete Final, `TryTriggerContextualRevision` attempts one revision unless lookahead already served it. For DeepL/LLM it retranslates the previous text with the new text as context-only lookahead. For other providers it translates the concatenation and assigns both segment IDs to one display group ([TranslationScheduler.cs:123](../../../src/AntiDbt.Core/TranslationScheduler.cs#L123)).

`AssignTranslationDisplayGroup` holds the store lock, writes the combined translation on the first member, blanks following member translations and records group identity. `Snapshot` exposes a single combined fallback through `DisplayTranslationOverride`. Individual exact results retire the group once every member has an exact translation ([SubtitleStore.cs:219](../../../src/AntiDbt.Core/SubtitleStore.cs#L219), [288](../../../src/AntiDbt.Core/SubtitleStore.cs#L288), [301](../../../src/AntiDbt.Core/SubtitleStore.cs#L301)). Atomic visibility is implemented, but correctness of the commit is weaker than the normal path: only session/IDs are supplied, with no expected source revisions (AQ-01).

The revision task has an independent deadline/token and is absent from normal `jobs` cancellation, rate-limit/terminal guards, outstanding-request accounting and ordinary telemetry. A session guard prevents old-session display updates but cannot cancel remote work or prevent same-revision last-writer races. One-shot tracking is added before success and is never pruned (AQ-02, AQ-08). These are implementation gaps, not proof that every ordinary translation is stale or duplicated.

## Failure and recovery boundaries

| Failure | Current response | Limit |
|---|---|---|
| ASR transient disconnect | Up to 10 reconnect failures; exponential 0.5-5 s delay; reset count only after results plus healthy interval | Audio backlog older than 2 s is discarded; no seamless transcript replay |
| ASR auth/quota/config/model/language | Fatal, surfaced to UI | No automatic credential/provider substitution |
| Long ASR Retry-After | Up to 2-minute advertised delay accepted; longer requires manual recovery | Cooperative connect/native model load can still ignore cancellation |
| Ordinary MT timeout | Deadline plus `WaitAsync`; error attached to current row; late result guarded | Physical task may continue, capped by outstanding accounting |
| MT 429/5xx | `ProviderHttp` waits Retry-After and retries 429 once; one 5xx retry after 300 ms | Contextual revision bypasses scheduler-level guards, though adapter policies still apply |
| Manual retry | Current-session Final, exact identity, error and cooldown checks; 2-second click cooldown | Old-session history is intentionally not retried |

Evidence: [CaptionPipeline.cs:210](../../../src/AntiDbt.Core/CaptionPipeline.cs#L210), [365](../../../src/AntiDbt.Core/CaptionPipeline.cs#L365), [ProviderHttp.cs:29](../../../src/AntiDbt.Providers/ProviderHttp.cs#L29), [TranslationScheduler.cs:290](../../../src/AntiDbt.Core/TranslationScheduler.cs#L290). Provider connectivity, model quality and sustained real-time throughput remain separate acceptance dimensions; see [Test and Performance](TEST-AND-PERFORMANCE.md).
