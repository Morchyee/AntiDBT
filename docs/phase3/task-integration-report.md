# Phase3 localhost integration report

Implemented `tests/AntiDbt.Providers.Multi.Tests/LocalProtocol.cs` and optional `Program.cs --localhost` entry. Existing handler tests remain the default.

Executed validation:

- Serial no-restore build passed (0 errors). Two NU1900 warnings reflect unavailable NuGet vulnerability metadata; no install or restore was performed.
- Default handler tests: 64/64 passed; `artifacts/phase3/Providers.Multi-green.log` and `http-tests.json`.
- Actual localhost integration: 13/13 passed; `artifacts/phase3/combinations.json` and `combinations-green.log`.
- Ten combinations passed: Groq Whisper with all six MT providers (including original Volc MT); Azure Speech with Azure Translator and DeepL; Google Cloud STT with Groq LLM; original Volc ASR with DeepL. These include all seven named REQUEST.md combinations.
- Observed six multipart WAV requests (RIFF, PCM16kmono, model, auth), eighteen SDK gateway PCM writes, one binary Volc WebSocket connection and one negative final audio frame. Actual provider MT requests were parsed and checked for original text and provider authentication/query fields.
- Every combination ran finite synthetic voice and silence through real CaptionPipeline, SubtitleStore and TranslationScheduler. Checks covered source visible before translation, exact final text, translation revision/session/segment identity, Partial for streaming providers, finalization without pipeline Error, audio disposal and injected gateway disposal.
- Additional actual server checks covered HTTP401 authentication classification and caller cancellation.

Execution boundary: default sandbox run could start the listener but sent zero requests and failed all checks because client loopback access was blocked. A scoped approved invocation of the existing test DLL outside sandbox networking restrictions passed. The first reachable run found an overly strict fixture assertion: identical-text final source updates intentionally preserve an earlier translation until the scheduler applies the final revision. The fixture now permits that transient older revision while requiring an exact final revision; the final run passed.

Limits: Azure and Google use their real provider/session classes with injected ISpeechTransport HTTP gateway fixtures. This is SDK-session integration evidence, not Azure native SDK or Google gRPC wire validation. Volc uses original binary WebSocket framing; Groq and all MT providers use actual HTTP. Synthetic audio and predetermined server transcripts do not establish speech recognition quality, paid API correctness, hardware/WASAPI behavior, UI behavior, real cloud credentials, language coverage or soak performance. No production changes or commits were made by this task.
