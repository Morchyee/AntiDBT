# Windows audio and protected settings report — reviewed implementation

## Owned files and public signatures

- `prototype/src/AntiDbt.Windows/AntiDbt.Windows.csproj`: net9.0-windows; Core project reference; NAudio.Wasapi 2.3.0 with NAudio.Core dependency.
- `AudioSources.cs`: `WasapiAudioSource(string? deviceId = null)`, `static IReadOnlyList<AudioDevice> ListDevices()`, `AudioDevice(string Id, string Name, bool IsDefault)`, `WaveFileAudioSource(string path, bool realtime = true)`. Both sources implement Core IAudioSource.
- Independently testable `StreamingPcmConverter(WaveFormat)`, `byte[] Convert(ReadOnlySpan<byte>)`, `PcmRing(int capacity = 640000)`. `PcmRing.Write` returns dropped sample count.
- `SettingsStore.cs`: `SettingsStore(string directory)`, `Task<AppSettings> LoadAsync()`, `Task SaveAsync(AppSettings settings)`, `string? Warning`.
- `AssemblyInfo.cs`: test-only internal access for delayed capture and filesystem failure injection.
- `prototype/tests/AntiDbt.Windows.Tests/AntiDbt.Windows.Tests.csproj`, `Program.cs`, original `red.log`/`green.log`, `review-red.log`, `review-green.log`, `hardware-smoke.log`.
- Machine-readable latest results: `prototype/artifacts/windows-tests.json` (hardwareRequested=true, failures=0).

## Behavior

Render-endpoint WASAPI loopback captures system audio, optionally by device ID. WAV replay is independent. Conversion preserves incomplete frames and resampler phase, averages channels, handles PCM 16/24/32 and IEEE float 32 including extensible formats, maps nonfinite floats to zero and clips to mono 16000 Hz S16LE. Full chunks contain 6400 bytes; termination preserves a shorter PCM tail. Stopwatch metadata and sample-derived AudioStart/AudioDuration are emitted. The 640000-byte drop-oldest ring now returns dropped samples and the source advances AudioStart accordingly; channel capacity remains 50 with DropOldest.

Each real source serializes Start/Stop/Dispose through a lifecycle semaphore. Disposal is terminal; repeated Stop/Dispose are safe. WASAPI Stop requests capture termination and waits for RecordingStopped before channel completion or resource disposal. Late data is accepted until that event, and event errors propagate to the chunk reader. Stop accepts caller cancellation and also has a ten-second termination timeout. Dispose uses the same ten-second bound; a timeout keeps resources alive for a later disposal retry instead of disposing a running capture. Cancellation registration is removed before disposing the capture session. WAV startup transfers reader ownership only after converter validation and pump creation; all failures close its reader.

Settings schema version 1 uses a single atomically replaced JSON envelope: public JSON settings plus a separate ProtectedSecrets ciphertext field containing both providers' ApiKey and Secrets dictionaries. DPAPI uses CurrentUser scope, UI forbidden. One envelope avoids public/secret generation mismatch. Core JsonIgnore prevents plaintext secrets in Settings. UTF8/native plaintext buffers are cleared. Options survive roundtrip. Missing settings produce defaults; invalid JSON/version/ciphertext produces defaults with Warning; I/O failures propagate. Temporary-file cleanup now runs within a nested finally that always releases the settings semaphore, including deletion failure.

## Commands and results

Environment:

```powershell
$env:MSBuildEnableWorkloadResolver='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_HOME='F:/Program/antiDBT/prototype/.dotnet-home'
```

Initial local-cache build:

```powershell
dotnet build prototype/tests/AntiDbt.Windows.Tests -p:NuGetAudit=false -p:BuildInParallel=false -v minimal
```

Review regression RED, before the fixes:

```powershell
dotnet run --project prototype/tests/AntiDbt.Windows.Tests --no-restore -p:UseSharedCompilation=false -p:BuildInParallel=false
```

`review-red.log` captures actual failure output: dispose-then-start accepted, unsupported WAV reader remained locked, oversized-feed AudioStart was incorrect. Cleanup also failed from leaked input handles, so this RED process exited 1 before saving its JSON. Original alignment RED is separately recorded in red.log. Review fixes changed those cases to GREEN; review-green.log records the initial 14-group successful run.

Final build and explicit hardware metadata smoke:

```powershell
dotnet run --project prototype/tests/AntiDbt.Windows.Tests --no-restore -p:UseSharedCompilation=false -p:BuildInParallel=false -- --capture-smoke
```

Final exit 0: **15/15 synthetic and resource regression groups plus 1/1 hardware lifecycle smoke passed**. Exact final output is captured in hardware-smoke.log. The build reported NU1900 because cached restore metadata attempted unavailable NuGet vulnerability service access; compilation and execution succeeded. No escalation, dependency installation, target application execution, or audio transmission was used.

Coverage:

1. 48 kHz and 44.1 kHz stereo float rate conversion and exact irregular-byte callback invariance.
2. Stereo downmix and positive/negative clipping; nonzero signed half-scale PCM 16/24/32 vectors.
3. Ring overflow/tail/alignment, bounded channel drops, oversized 22-second feed with both ring and channel drops yielding correct retained AudioStart=12 seconds.
4. Synthetic WAV full/tail timing, cancellation, disposal, exclusive reopen; unsupported 8-bit WAV startup failure closes the file.
5. Repeated Stop/Dispose, start-after-dispose rejection, concurrent Start/Dispose and Stop/Dispose.
6. Delayed capture fixture proves Stop stays pending, cancellation bounds its wait, Dispose waits for termination, final callback survives, error reaches the reader, and only one stop request is issued.
7. Actual Windows DPAPI roundtrip for both ApiKey fields and both Secrets dictionaries, no test-secret plaintext in persisted JSON, options retained, invalid JSON/version/ciphertext recovery.
8. Injected save-cleanup IOException proves the semaphore still releases and subsequent Load completes.

Folder-mode dotnet format whitespace succeeded for both owned directories. Earlier project-mode formatting hit a sandbox named-pipe UnauthorizedAccessException; no escalation was needed.

## Hardware versus synthetic evidence

All audio content assertions use generated bytes or temporary synthetic WAV files. The explicit three-second WASAPI smoke did acquire the actual default render endpoint, start, request stop, await RecordingStopped and dispose successfully. Its metadata was `captureBytes=0`, `audioPersisted=false`. Consequently **actual nonzero loopback PCM, audible content and ASR quality remain unverified**. Zero captured bytes can occur for a silent endpoint; the test does not treat silence as proof of useful audio. The smoke never saves or transmits captured PCM. DPAPI tests invoke actual local crypt32 APIs. Synthetic WAV files are deleted after each successful test run. No secret fixture values appear in this report.

## Remaining limits

The deterministic area/box resampler is not a high-order antialiasing filter; production recognition quality needs nonzero real-audio evaluation. Endpoint removal/reconfiguration, protected/exclusive-mode audio and long capture soak remain untested. A fractional resampler interval smaller than one output sample is discarded at termination. Deliberate channel drops appear as AudioStart gaps. Credentials require the same Windows user profile; a different profile may recover defaults with Warning. Atomic filesystem replacement is implemented, but power-loss fault injection was not performed. If RecordingStopped never arrives, Stop/Dispose times out after ten seconds and retains the session for a later disposal retry.
