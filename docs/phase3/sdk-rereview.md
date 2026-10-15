# Scoped SDK re-review

Reviewed 2026-10-14: current CloudSpeech source, Regessions.cs/Program.cs fixtures, updated task-sdk-report.md and existing sdk-review-red/green build/execution logs. Scope is the four findings in sdk-review.md plus the silence/stream timer change. No tests rerun, implementation edits, or cloud requests.

## Verdict

- **Spec: prior findings resolved within the agreed offline implementation scope.**
- **Quality: approve these scoped fixes.** No remaining blocker identified in the reviewed changes. This is not native-runtime, real gRPC, authenticated cloud or release-package acceptance.

## Finding disposition

1. **Google consecutive interim overwrite — resolved.** `prototype/src/AntiDbt.CloudSpeech/GoogleSpeechProvider.cs`, ReadAsync now aggregates non-final portions for each response into one pending utterance update; final portions advance previousEnd before that update. Existing recorded tests cover two interims followed by merged final, final followed by interims, empty alternatives, stable identity and the new pending audio start.

2. **Aborted operations leave sessions usable — resolved for the reported paths.** `prototype/src/AntiDbt.CloudSpeech/SpeechSession.cs` supplies linked operation deadline tokens, sets terminal state on acquired-operation failure, cancels lifetime, invokes transport Abort and performs cached cleanup. Sends recheck terminal/EOF state inside the writer gate. Failed connection immediately cleans up acquired transport. GoogleTransport Abort cancels the actual gRPC lifetime, and its write wrapper aborts on cancellation/failure. The pending-write fixture ignores its operation token until disposal; recorded evidence verifies only one write entered and disposal occurred once while the queued second send was rejected. EOF is now serialized with writes and rejects later sends without discarding a final emitted after input closure.

3. **Azure partial-construction unwind — resolved.** `prototype/src/AntiDbt.CloudSpeech/AzureSpeechProvider.cs` acquires format/push/audio/recognizer via `NativeResources` inside an exception cleanup boundary. `prototype/src/AntiDbt.CloudSpeech/NativeResources.cs` disposes in reverse order and continues when one disposer throws. The staged fixture covers failure at each acquisition stage and the throwing-disposer case. Actual native construction/disposal behavior remains a separate smoke-test boundary.

4. **Receive/lifecycle fixtures bypass production logic — materially resolved for the cited omissions.** Google injected SDK response streams now run the production ReadAsync loop with real StreamingRecognizeResponse/Result objects and RpcException instances. Azure production callbacks and tests share AzureEvents SDK-field conversion, including NoMatch/time/error mapping. Added fixtures cover failed connect cleanup, blocked write cancellation and a final arriving after EOF. This is still adapter/SDK-object fixture coverage, not a network gRPC server or actual native event delivery. RpcException tests assert sanitization and category-bearing messages; exact expected categories are evident in source but would be stronger explicit assertions in future test maintenance.

## Silence / stream lifetime

The former 30-second no-result timeout is removed. A silent Google receive can remain pending until the overall stream limit, stop, operation failure or disposal. The overall timer starts before SDK StartAsync/config transmission, so startup no longer extends the 4m45 cap. Expiry marks the session failed, directly cancels transport I/O and starts cleanup. The recorded shortened real-clock test verifies a blocked receive observes cancellation; it does not simulate 4m45 of real cloud operation. This decision avoids misclassifying ordinary silence as a network fault while retaining a bounded stream lifetime.

## Evidence checked

- sdk-review-red.txt records **11 behavioral regression failures** before fixes; unlike the initial sdk-red.txt environment failure, this is behavioral RED evidence.
- sdk-review-green.txt records **14 regression cases passed and 36/36 total assertions passed**.
- sdk-review-green-build.txt records successful compilation, **0 errors and 1 NU1900 warning**. The report's phrase “zero compiler warnings/errors; one NU1900 ... warning” should be read as no compiler diagnostics, not a warning-free build.
- Updated report explicitly retains native synchronous-call blocking, no real gRPC/cloud, region/model availability, and packaging limitations. WaitAsync does not prove native Write/Close/Dispose cannot block; the report correctly avoids that guarantee.
