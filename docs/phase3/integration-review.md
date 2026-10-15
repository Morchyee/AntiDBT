# Local integration task review

Reviewed 2026-10-15: task-integration.md, task-integration-report.md, REQUEST.md named combinations, LocalProtocol.cs, Program.cs --localhost hook, combinations.json and combinations-green.log. No tests rerun, cloud requests or implementation edits in this review.

## Verdict

**Spec: approved within the assigned localhost scope. Quality: approved; no blocking finding.** The new suite exercises actual production pipeline combinations, not only provider construction. The default handler suite remains selected without --localhost; its inspected log reports 64/64 passing.

## Requirements and evidence

- ASP.NET binds 127.0.0.1:0 with logging providers cleared. Explicit gateway HttpClient disables proxy and redirects. Provider endpoints for actual integration calls point at the loopback server; only synthetic credentials and audio are supplied. Azure/Google transports are injected, so their official configuration metadata does not cause cloud calls in this suite.
- Ten combinations instantiate real CaptionPipeline, SubtitleStore and TranslationScheduler with production ASR/session and MT provider classes. Six pair Groq Whisper with each MT provider, two pair Azure with Azure Translator/DeepL, one pairs Google with Groq LLM, and one pairs original Volc ASR with DeepL. This includes all seven named examples in REQUEST.md; it does not establish all 24 possible combinations.
- Each combination sends finite voice and silence, awaits bounded pipeline completion, verifies source appeared before translation, exact final source/translation, segment/session identity, final revision equality, no translation error, Stopped without Error, and disposal of synthetic audio and injected gateway. Streaming cases also require an observed non-final segment. Intermediate translation revision may lag the source revision, consistent with retaining prior translation until final translation completes; final revision must match exactly.
- Groq requests traverse real HTTP and multipart parsing; the fixture checks bearer auth, model, RIFF header, sample rate and channel count. All MT providers traverse actual HTTP and their provider-specific request/response parsers, checking source text and selected authentication/query/path fields. This is meaningful wire evidence for the asserted fields, not exhaustive schema validation of every field.
- Original Volc ASR traverses binary WebSocket framing. The server checks configuration/audio frame types and 16 kHz configuration, emits nonterminal partial/final results after audio, and emits terminal output only after a negative final audio sequence. The recorded socket and final-frame counts both equal one.
- Additional actual HTTP cases verify 401 classification and caller cancellation. The cancellation assertion proves the client observes cancellation; it does not separately assert that the server observed RequestAborted.

## Recorded result and boundaries

combinations.json and combinations-green.log agree: **13/13 checks pass, including 10 actual pipeline combinations**, with six multipart WAV requests, eighteen gateway PCM writes, one WebSocket and one negative final audio frame. These artifacts were inspected, not independently rerun by this reviewer.

Azure and Google use production provider/session logic around injected ISpeechTransport gateway fixtures. Those gateways convert predetermined HTTP fixture responses into SpeechEvent callbacks. They bypass Azure native transport/callback conversion and Google gRPC serialization/receive transport: **this is SDK-session seam integration, not Azure native SDK or Google gRPC wire validation**. Separate SDK review evidence remains in sdk-rereview.md.

The suite uses predetermined transcripts and synthetic audio; it does not validate recognition quality, paid API behavior, real credentials, WASAPI, UI, configuration persistence, long sessions, packaging or release exit behavior. Disposing gateway/audio and observing pipeline Stopped is bounded component evidence, not a process-residue audit. Existing handler fault cases remain distinct from these actual socket tests.

Nonblocking fixture limitations: cooldown timing uses real-clock observation intervals; integration protocol assertions cover selected fields rather than full schemas; statuses are collected in a plain list and would benefit from synchronized collection if future fixtures add concurrent status writers. None invalidates the inspected passing cases or the stated evidence boundaries.
