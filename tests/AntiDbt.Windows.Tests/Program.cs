using System.Buffers.Binary;
using System.Text.Json;
using AntiDbt.Core;
using AntiDbt.Windows;
using NAudio.Wave;
var results = new List<object>(); int failures = 0;
async Task Test(string name, Func<Task> test) { try { await test(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.GetType().Name }); Console.WriteLine("FAIL " + name + ": " + e.Message); } }
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
await Test("rate-and-callback-invariance", () => { var data = new byte[48000 * 8]; for (int i = 0; i < data.Length; i += 4) BitConverter.GetBytes((float)Math.Sin(i / 80d)).CopyTo(data, i); var whole = new StreamingPcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)).Convert(data); var split = new StreamingPcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)); var chunks = new List<byte>(); for (int i = 0; i < data.Length; i += 137) chunks.AddRange(split.Convert(data.AsSpan(i, Math.Min(137, data.Length - i)))); Check(whole.Length == 32000); Check(whole.SequenceEqual(chunks)); return Task.CompletedTask; });
await Test("downmix-clipping-pcm-widths", () => { foreach (int bits in new[] { 16, 24, 32 }) { var converter = new StreamingPcmConverter(new WaveFormat(16000, bits, 2)); var data = new byte[bits / 8 * 2]; Check(converter.Convert(data).SequenceEqual(new byte[2])); } var floats = new[] { 2f, 2f, -2f, -2f, 1f, -1f }.SelectMany(BitConverter.GetBytes).ToArray(); var output = new StreamingPcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(16000, 2)).Convert(floats); Check(BinaryPrimitives.ReadInt16LittleEndian(output) == 32767); Check(BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(2)) == -32768); Check(BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(4)) == 0); return Task.CompletedTask; });
await Test("ring-overflow-tail-and-alignment", () => { var ring = new PcmRing(8); ring.Write(Enumerable.Range(0, 12).Select(x => (byte)x).ToArray()); Check(ring.Read(6).SequenceEqual(new byte[] { 4, 5, 6, 7, 8, 9 })); Check(ring.Read(8).SequenceEqual(new byte[] { 10, 11 })); bool threw = false; try { ring.Read(1); } catch (ArgumentException) { threw = true; } Check(threw, "Odd-byte reads must be rejected"); return Task.CompletedTask; });
await Test("bounded-channel-drops-oldest", async () => { await using var source = new SyntheticSource(); await source.StartAsync(default); var chunks = new List<AudioChunk>(); await foreach (var chunk in source.ReadChunksAsync(default)) chunks.Add(chunk); Check(chunks.Count == 50 && chunks[0].AudioStart == TimeSpan.FromSeconds(2)); });
string temp = Path.Combine(AppContext.BaseDirectory, "testtemp", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
await Test("dpapi-roundtrip-no-plaintext-and-recovery", async () => { var store = new SettingsStore(temp); var settings = new AppSettings { Asr = new() { ApiKey = "fake-test-key-a", Secrets = new() { { "token", "fake-test-secret-a" } }, Options = new() { { "mode", "test" } } }, Translation = new() { ApiKey = "fake-test-key-b", Secrets = new() { { "token", "fake-test-secret-b" } } } }; await store.SaveAsync(settings); var loaded = await store.LoadAsync(); Check(loaded.Asr.ApiKey == settings.Asr.ApiKey && loaded.Translation.Secrets["token"] == settings.Translation.Secrets["token"] && loaded.Asr.Options["mode"] == "test"); var text = await File.ReadAllTextAsync(Path.Combine(temp, "settings.json")); Check(!text.Contains("fake-test")); await File.WriteAllTextAsync(Path.Combine(temp, "settings.json"), "corrupt"); await store.LoadAsync(); Check(store.Warning != null); });
await Test("wave-tail-timing-disposal-cancellation", async () => { var path = Path.Combine(temp, "input.wav"); using (var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 1))) writer.Write(new byte[7000]); await using (var source = new WaveFileAudioSource(path, false)) { await source.StartAsync(default); var chunks = new List<AudioChunk>(); await foreach (var chunk in source.ReadChunksAsync(default)) chunks.Add(chunk); Check(chunks.Count == 2 && chunks[0].Pcm.Length == 6400 && chunks[1].Pcm.Length == 600); Check(chunks[1].AudioStart == TimeSpan.FromSeconds(.2) && chunks[0].CapturedTimestamp > 0); await source.StopAsync(default); } using (var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } await using (var source = new WaveFileAudioSource(path)) { using var cancel = new CancellationTokenSource(); await source.StartAsync(cancel.Token); cancel.Cancel(); await source.StopAsync(default); } });
await Test("review-lifecycle-start-after-dispose", async () => { var source = new WaveFileAudioSource(Path.Combine(temp, "input.wav")); await source.DisposeAsync(); await source.DisposeAsync(); await source.StopAsync(default); bool rejected = false; try { await source.StartAsync(default); } catch (ObjectDisposedException) { rejected = true; } Check(rejected); });
await Test("review-wave-unsupported-reader-release", async () => { var path = Path.Combine(temp, "unsupported.wav"); using (var writer = new WaveFileWriter(path, new WaveFormat(16000, 8, 1))) writer.Write(new byte[128]); await using var source = new WaveFileAudioSource(path); try { await source.StartAsync(default); } catch (NotSupportedException) { } using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
await Test("review-ring-overflow-clock", async () => { await using var source = new OversizedSource(); await source.StartAsync(default); var chunks = new List<AudioChunk>(); await foreach (var chunk in source.ReadChunksAsync(default)) chunks.Add(chunk); Check(chunks[0].AudioStart == TimeSpan.FromSeconds(12), "Ring drop plus channel drop clock must be 12 seconds"); });
await Test("review-delayed-capture-stop-and-cancel", async () =>
{
    var session = new DelayedCapture(); await using var source = new WasapiAudioSource(() => session); await source.StartAsync(default);
    using var cancel = new CancellationTokenSource(); var stopping = source.StopAsync(cancel.Token); Check(!stopping.IsCompleted, "Stop completed before RecordingStopped"); cancel.Cancel(); try { await stopping; throw new Exception("Stop cancellation ignored"); } catch (OperationCanceledException) { }
    var disposal = source.DisposeAsync().AsTask(); Check(!disposal.IsCompleted && !session.Disposed, "Disposed before RecordingStopped"); session.Emit(new byte[6400]); session.End(); await disposal; Check(session.Disposed && session.StopRequests == 1);
    var chunks = new List<AudioChunk>(); await foreach (var chunk in source.ReadChunksAsync(default)) chunks.Add(chunk); Check(chunks.Count == 1, "Final callback lost"); await source.DisposeAsync(); await source.StopAsync(default);
    bool rejected = false; try { await source.StartAsync(default); } catch (ObjectDisposedException) { rejected = true; }
    Check(rejected);
});
await Test("review-capture-error-and-concurrent-disposal", async () =>
{
    var session = new DelayedCapture(); var source = new WasapiAudioSource(() => session); await source.StartAsync(default); var stopping = source.StopAsync(default); var dispose = source.DisposeAsync().AsTask(); session.End(new IOException("synthetic stop failure")); await Task.WhenAll(stopping, dispose); bool observed = false; try { await foreach (var chunk in source.ReadChunksAsync(default)) { } } catch (IOException) { observed = true; }
    Check(observed); await source.DisposeAsync();
    var unstarted = new WasapiAudioSource(() => throw new Exception("Disposed source acquired capture")); await unstarted.DisposeAsync(); try { await unstarted.StartAsync(default); throw new Exception("Start accepted"); } catch (ObjectDisposedException) { }
});
await Test("review-settings-cleanup-gate-release", async () =>
{
    var dir = Path.Combine(temp, "cleanup"); Directory.CreateDirectory(Path.Combine(dir, "settings.json")); var store = new SettingsStore(dir, _ => throw new IOException("synthetic cleanup failure")); try { await store.SaveAsync(new()); throw new Exception("Expected save failure"); } catch (IOException) { }
    await store.LoadAsync().WaitAsync(TimeSpan.FromSeconds(1));
});
await Test("review-44100-and-signed-pcm-vectors", () =>
{
    var data = new byte[44100 * 8]; for (int i = 0; i < data.Length; i += 4) BitConverter.GetBytes((float)Math.Sin(i / 97d)).CopyTo(data, i); var whole = new StreamingPcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)).Convert(data); var split = new StreamingPcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)); var parts = new List<byte>(); for (int i = 0; i < data.Length; i += 111) parts.AddRange(split.Convert(data.AsSpan(i, Math.Min(111, data.Length - i)))); Check(whole.Length == 32000 && whole.SequenceEqual(parts));
    foreach (int bits in new[] { 16, 24, 32 }) { var bytes = new List<byte>(); foreach (long value in new[] { 1L << (bits - 2), -(1L << (bits - 2)) }) for (int i = 0; i < bits / 8; i++) bytes.Add((byte)(value >> (8 * i))); var pcm = new StreamingPcmConverter(new WaveFormat(16000, bits, 1)).Convert(bytes.ToArray()); Check(BinaryPrimitives.ReadInt16LittleEndian(pcm) == 16384 && BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(2)) == -16384); }
    return Task.CompletedTask;
});
await Test("review-settings-version-and-ciphertext", async () =>
{
    var dir = Path.Combine(temp, "schema"); var store = new SettingsStore(dir); await store.SaveAsync(new()); var path = Path.Combine(dir, "settings.json"); var original = await File.ReadAllTextAsync(path); var node = System.Text.Json.Nodes.JsonNode.Parse(original)!; node["Version"] = 99; await File.WriteAllTextAsync(path, node.ToJsonString()); await store.LoadAsync(); Check(store.Warning != null); node = System.Text.Json.Nodes.JsonNode.Parse(original)!; node["ProtectedSecrets"] = Convert.ToBase64String(new byte[64]); await File.WriteAllTextAsync(path, node.ToJsonString()); await store.LoadAsync(); Check(store.Warning != null);
});
await Test("review-concurrent-start-dispose", async () =>
{
    using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var session = new DelayedCapture { StartAction = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(2)); }, AutomaticStop = true }; var source = new WasapiAudioSource(() => session); var starting = Task.Run(() => source.StartAsync(default)); Check(entered.Wait(TimeSpan.FromSeconds(2))); var disposing = Task.Run(async () => await source.DisposeAsync()); release.Set(); await Task.WhenAll(starting, disposing).WaitAsync(TimeSpan.FromSeconds(3)); Check(session.Disposed && session.StopRequests == 1); await source.DisposeAsync();
});
if (args.Contains("--capture-smoke")) await Test("hardware-capture-smoke", async () => { await using var source = new WasapiAudioSource(); using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await source.StartAsync(cancel.Token); int bytes = 0; try { await foreach (var chunk in source.ReadChunksAsync(cancel.Token)) bytes += chunk.Pcm.Length; } catch (OperationCanceledException) { } await source.StopAsync(default); Console.WriteLine(JsonSerializer.Serialize(new { captureBytes = bytes, audioPersisted = false })); });
Directory.Delete(temp, true); var artifact = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/windows-tests.json")); Directory.CreateDirectory(Path.GetDirectoryName(artifact)!); await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(new { failures, hardwareRequested = args.Contains("--capture-smoke"), results }, new JsonSerializerOptions { WriteIndented = true })); return failures == 0 ? 0 : 1;

sealed class SyntheticSource : PcmAudioSource
{
    public override string Name => "synthetic";
    public override Task StartAsync(CancellationToken token) { Converter = new(new WaveFormat(16000, 16, 1)); for (int i = 0; i < 60; i++) Feed(new byte[6400]); Finish(); return Task.CompletedTask; }
    public override Task StopAsync(CancellationToken token) { Finish(); return Task.CompletedTask; }
    public override ValueTask DisposeAsync() { Finish(); return ValueTask.CompletedTask; }
}
sealed class OversizedSource : PcmAudioSource
{
    public override string Name => "oversized";
    public override Task StartAsync(CancellationToken token) { Converter = new(new WaveFormat(16000, 16, 1)); Feed(new byte[22 * 32000]); Finish(); return Task.CompletedTask; }
    public override Task StopAsync(CancellationToken token) { Finish(); return Task.CompletedTask; }
    public override ValueTask DisposeAsync() { Finish(); return ValueTask.CompletedTask; }
}
sealed class DelayedCapture : ICaptureSession
{
    public string Name => "synthetic capture";
    public WaveFormat WaveFormat => new(16000, 16, 1);
    public event Action<ReadOnlyMemory<byte>>? Data;
    public event Action<Exception?>? Stopped;
    public bool Disposed; public int StopRequests;
    public Action? StartAction; public bool AutomaticStop;
    public void Start() => StartAction?.Invoke();
    public void RequestStop() { StopRequests++; if (AutomaticStop) End(); }
    public void Emit(byte[] data) => Data?.Invoke(data);
    public void End(Exception? error = null) => Stopped?.Invoke(error);
    public void Dispose() => Disposed = true;
}

