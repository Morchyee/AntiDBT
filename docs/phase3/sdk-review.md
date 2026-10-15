# Official SDK review

Reviewed 2026-10-14: task-sdk.md, task-sdk-report.md, SDK-SPECS.md, sdk-review.patch, current CloudSpeech source/tests, existing build/GREEN logs; Core pipeline was read only to understand reconnect/drain integration. No implementation changes, tests rerun, or cloud calls.

## Verdicts

- **Spec: changes required.** Official packages/APIs, explicit Google credentials, PCM config/chunking and basic session events are implemented. Google multi-result Partial mapping is incorrect; requested SDK receive/lifecycle fixture coverage is incomplete. Behavioral RED evidence was not obtained, correctly disclosed in the implementation report.
- **Quality: changes required.** Fix receive mapping and operation cancellation/terminal-state handling before treating this as ready for integration. Native lifecycle verification remains incomplete.

## Actionable findings

### P1 — Consecutive Google interim portions overwrite one another

Location: `prototype/src/AntiDbt.CloudSpeech/GoogleSpeechProvider.cs:60-64`, interacting with `prototype/src/AntiDbt.CloudSpeech/SpeechSession.cs:43-49`.

Google returns consecutive portions in one response, including multiple non-final results. This loop emits each as a separate revision while SpeechSession advances identity only on final. For the documented response containing `to be` and ` or not to be`, the same subtitle row is first set to the prefix and then replaced by the suffix; the complete interim is never visible and translation can receive the suffix alone. Aggregate the non-final portions of each response into the current pending utterance (after handling any settled final), or implement explicit stable portion identities with reconciliation. Add a receive fixture for two interim results followed by their merged final, plus a response with final then multiple interim portions.

Official 1.8.0 reference, Results semantics and example: https://docs.cloud.google.com/dotnet/docs/reference/Google.Cloud.Speech.V2/latest/Google.Cloud.Speech.V2.StreamingRecognizeResponse

### P2 — Deadline/cancellation abandons an operation but leaves the session usable

Location: `prototype/src/AntiDbt.CloudSpeech/SpeechSession.cs:34-38,53-67`; `prototype/src/AntiDbt.CloudSpeech/GoogleSpeechProvider.cs:41,77-78`.

The 12/15/4-second WaitAsync limits bound the waiter without canceling the token supplied to the underlying operation. Google gRPC is controlled by its separate transport lifetime token. A canceled SendAudioAsync releases the semaphore while its SDK WriteAsync may still run, allowing another write against a single-writer duplex stream. Timeout calls Fail, but Fail/Stop do not cancel lifetime and SendAudioAsync does not check ended, so even a terminal session accepts further sends. Connect failure also leaves allocated transport attached until caller disposal. Use operation-linked deadline tokens, terminate/cancel the transport on aborted operations, reject subsequent writes after terminal failure/EOF, and clean up failed connections. Verify with a pending-write fixture that cancellation prevents a second write and releases resources. The current pipeline usually disposes on error, which reduces exposure but does not fulfill the session contract on its own.

### P2 — Azure partial construction leaks previously acquired native resources

Location: `prototype/src/AntiDbt.CloudSpeech/AzureSpeechProvider.cs:24-30`.

If CreatePushStream, FromStreamInput or SpeechRecognizer construction fails after earlier allocations, factory() never returns an ISpeechTransport; SpeechSession therefore cannot dispose its format/push/audio resources. This is especially relevant to native-load/device/resource failures and repeated reconnect attempts. Allocate into locals with exception cleanup, transferring ownership only after successful construction; ensure each previously acquired disposable is released when a later acquisition throws. Also ensure disposal attempts all owned resources if one Dispose throws.

### P2 — Current fixtures bypass the receive/lifecycle logic that needs verification

Location: `prototype/tests/AntiDbt.CloudSpeech.Tests/Program.cs:8-38,44-49,57-68`.

Both provider suites emit already normalized SpeechEvent values from the same Fixture; none exercises Azure SDK event-to-event conversion, Google ReadAsync, real response batches/empty alternatives, RpcException mapping, blocked read/write cancellation, or native acquisition failures. The test labeled EOF drain emits all results before CompleteAsync and therefore proves no final arrives/drains after EOF. The test labeled sanitized error only supplies an enum, so no potentially sensitive SDK exception ever reaches the implementation under test. Add an SDK response/event seam (or in-process gRPC fixture) and behavioral cases for these paths; preserve the report's distinction between offline fixtures and cloud verification. A real gRPC server is optional under task-sdk.md, but adapter receive logic coverage is not replaced by the shared session fixture.

## Confirmed and bounded conclusions

- Package versions are pinned to Microsoft.CognitiveServices.Speech 1.52.0 and Google.Cloud.Speech.V2 1.8.0. Existing sdk-build.txt records a successful actual build, supporting API signature compatibility; this review did not rerun it.
- Existing sdk-green.txt records 22/22 assertions; the suite and its gaps above were inspected. This is gateway/request evidence, not authenticated service or native runtime proof.
- Google validates service_account type and exact official token URI before SDK credential parsing, uses explicit GoogleCredential, and does not select ADC. Error messages created by the session omit raw credentials and SDK details. Endpoint construction prevents arbitrary hosts; availability of every regex-accepted location/model combination remains unverified.
- Basic one-part partial/final identity and revision mapping is correct. Core starts a new generation on reconnect, so reset utterance counters do not collide across generations. Pipeline-owned EOF waiting mitigates Azure CompleteAsync returning immediately after push.Close.
- The 4m45 timer exists, but begins after startup/config transmission rather than at actual gRPC stream creation; its current test uses a shortened real timer only. Cancellation of the actual Google call relies on later transport disposal, not directly on expiry.
- Azure WaitAsync bounds asynchronous start/stop waiting, not synchronous native Write/Close/Dispose execution. The implementation report acknowledges missing native smoke evidence; no guarantee of a strict native-release time bound should be claimed from these fixtures.
- Source consumes raw PCM and Google sends config first with explicit Linear16/16 kHz/mono and <=15,000-byte audio requests. Release-native-copy/license checks remain the root integration gate.
