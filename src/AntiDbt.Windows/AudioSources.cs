using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AntiDbt.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace AntiDbt.Windows;

// Stateful area resampler: each input frame is held over its sample interval.
public sealed class StreamingPcmConverter
{
    readonly WaveFormat format; byte[] remainder = []; long phase; double area;
    public StreamingPcmConverter(WaveFormat format)
    {
        this.format = format;
        var encoding = format is WaveFormatExtensible ext ? ext.SubFormat : Guid.Empty;
        if (format.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat) && encoding != new Guid("00000001-0000-0010-8000-00aa00389b71") && encoding != new Guid("00000003-0000-0010-8000-00aa00389b71"))
            throw new NotSupportedException("Only PCM and IEEE float supported");
        if ((format.Encoding == WaveFormatEncoding.IeeeFloat || encoding == new Guid("00000003-0000-0010-8000-00aa00389b71")) && format.BitsPerSample != 32)
            throw new NotSupportedException("Only 32-bit IEEE float supported");
        if (format.SampleRate <= 0 || format.Channels <= 0 || format.BitsPerSample is not (16 or 24 or 32))
            throw new NotSupportedException("Unsupported PCM format");
    }
    public byte[] Convert(ReadOnlySpan<byte> input)
    {
        var data = new byte[remainder.Length + input.Length];
        remainder.CopyTo(data, 0);
        input.CopyTo(data.AsSpan(remainder.Length));
        int complete = data.Length / format.BlockAlign * format.BlockAlign;
        using var output = new MemoryStream();
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        for (int offset = 0; offset < complete; offset += format.BlockAlign)
        {
            double value = 0;
            for (int channel = 0; channel < format.Channels; channel++)
            {
                var sample = data.AsSpan(offset + channel * format.BitsPerSample / 8);
                double v = floating ? BitConverter.ToSingle(sample) : format.BitsPerSample switch
                {
                    16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768.0,
                    24 => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608.0,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0
                };
                value += double.IsFinite(v) ? v : 0;
            }
            value /= format.Channels;
            long left = 16000;
            while (left > 0)
            {
                long take = Math.Min(left, format.SampleRate - phase);
                area += value * take;
                phase += take;
                left -= take;
                if (phase == format.SampleRate)
                {
                    double scaled = Math.Clamp(area / format.SampleRate, -1, 1) * 32768;
                    short s = (short)Math.Clamp(Math.Round(scaled), short.MinValue, short.MaxValue);
                    output.WriteByte((byte)s);
                    output.WriteByte((byte)(s >> 8));
                    phase = 0;
                    area = 0;
                }
            }
        }
        remainder = data[complete..];
        return output.ToArray();
    }
}
public sealed class PcmRing
{
    readonly byte[] buffer; int head, count;
    public PcmRing(int capacity = 640000)
    {
        if (capacity <= 0 || capacity % 2 != 0)
            throw new ArgumentException("Even capacity required");
        buffer = new byte[capacity];
    }
    public int Count => count;
    public int Write(ReadOnlySpan<byte> data)
    {
        int dropped = Math.Max(0, count + data.Length - buffer.Length);
        if (data.Length % 2 != 0)
            throw new ArgumentException("Whole samples required");
        foreach (var b in data)
        {
            if (count == buffer.Length)
            {
                head = (head + 1) % buffer.Length;
                count--;
            }
            buffer[(head + count) % buffer.Length] = b;
            count++;
        }
        return dropped / 2;
    }
    public byte[] Read(int length)
    {
        if (length < 0 || length % 2 != 0)
            throw new ArgumentException("Whole samples required");
        length = Math.Min(length, count);
        var result = new byte[length];
        for (int i = 0; i < length; i++)
            result[i] = buffer[(head + i) % buffer.Length];
        head = (head + length) % buffer.Length;
        count -= length;
        return result;
    }
}
public abstract class PcmAudioSource : IAudioSource
{
    readonly Channel<AudioChunk> chunks = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = false, SingleReader = false });
    readonly PcmRing ring = new(); readonly object gate = new(); long samples; bool finished;
    protected StreamingPcmConverter? Converter;
    public abstract string Name
    {
        get;
    }
    protected void Feed(ReadOnlySpan<byte> data)
    {
        lock (gate)
        {
            if (finished)
                return;
            samples += ring.Write(Converter!.Convert(data));
            while (ring.Count >= 6400)
                Emit(ring.Read(6400));
        }
    }
    void Emit(byte[] pcm)
    {
        chunks.Writer.TryWrite(new(pcm, TimeSpan.FromSeconds(samples / 16000d), TimeSpan.FromSeconds(pcm.Length / 32000d), Stopwatch.GetTimestamp()));
        samples += pcm.Length / 2;
    }
    protected void Finish(Exception? error = null)
    {
        lock (gate)
        {
            if (finished)
                return;
            finished = true;
            if (ring.Count > 0)
                Emit(ring.Read(ring.Count));
            chunks.Writer.TryComplete(error);
        }
    }
    public abstract Task StartAsync(CancellationToken cancellationToken);
    public IAsyncEnumerable<AudioChunk> ReadChunksAsync(CancellationToken cancellationToken) => chunks.Reader.ReadAllAsync(cancellationToken);
    public abstract Task StopAsync(CancellationToken cancellationToken);
    public abstract ValueTask DisposeAsync();
}
public sealed record AudioDevice(string Id, string Name, bool IsDefault);

internal interface ICaptureSession : IDisposable
{
    string Name
    {
        get;
    }
    WaveFormat WaveFormat
    {
        get;
    }
    event Action<ReadOnlyMemory<byte>>? Data;
    event Action<Exception?>? Stopped;
    void Start();
    void RequestStop();
}

internal sealed class LoopbackSession : ICaptureSession
{
    readonly MMDevice device;
    readonly WasapiLoopbackCapture capture;
    public string Name => device.FriendlyName;
    public WaveFormat WaveFormat => capture.WaveFormat;
    public event Action<ReadOnlyMemory<byte>>? Data;
    public event Action<Exception?>? Stopped;
    public LoopbackSession(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? dev = null;
        if (!string.IsNullOrEmpty(deviceId))
        {
            try { dev = enumerator.GetDevice(deviceId); }
            catch { dev = null; }
        }
        device = dev ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        try
        {
            capture = new WasapiLoopbackCapture(device);
            capture.DataAvailable += (_, e) => Data?.Invoke(e.Buffer.AsMemory(0, e.BytesRecorded));
            capture.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
        }
        catch { device.Dispose(); throw; }
    }
    public void Start() => capture.StartRecording();
    public void RequestStop() => capture.StopRecording();
    public void Dispose()
    {
        capture.Dispose();
        device.Dispose();
    }
}

public sealed class WasapiAudioSource : PcmAudioSource
{
    readonly Func<ICaptureSession> factory;
    readonly SemaphoreSlim lifecycle = new(1, 1);
    readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    ICaptureSession? capture;
    CancellationTokenRegistration registration;
    bool started, disposed;
    int stopRequested;
    public WasapiAudioSource(string? deviceId = null) : this(() => new LoopbackSession(deviceId)) { }
    internal WasapiAudioSource(Func<ICaptureSession> factory) => this.factory = factory;
    public override string Name => capture?.Name ?? "System audio";

    public static IReadOnlyList<AudioDevice> ListDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var list = new List<AudioDevice>();
        foreach (var item in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (item)
                list.Add(new(item.ID, item.FriendlyName, item.ID == defaultDevice.ID));
        return list;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
                throw new InvalidOperationException("Source is single use");
            started = true;
            try
            {
                capture = factory();
                Converter = new(capture.WaveFormat);
                capture.Data += data => Feed(data.Span);
                capture.Stopped += error => { Finish(error); stopped.TrySetResult(); };
                capture.Start();
                var session = capture;
                registration = cancellationToken.Register(() => RequestStop(session));
            }
            catch (Exception error)
            {
                capture?.Dispose();
                capture = null;
                Finish(error);
                stopped.TrySetResult();
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }

    void RequestStop(ICaptureSession session)
    {
        if (Interlocked.Exchange(ref stopRequested, 1) == 0)
            session.RequestStop();
    }

    async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        started = true;
        if (capture == null)
        {
            Finish();
            return;
        }
        RequestStop(capture);
        // StopRecording is a request. Only RecordingStopped completes the output stream.
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(cancellationToken);
        }
        finally { lifecycle.Release(); }
    }

    public override async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            disposed = true;
            // Timeout leaves the session alive for a later disposal retry; do not destroy a running capture.
            await StopCoreAsync(default);
            registration.Dispose();
            capture?.Dispose();
            capture = null;
        }
        finally { lifecycle.Release(); }
    }
}

public sealed class WaveFileAudioSource(string path, bool realtime = true) : PcmAudioSource
{
    readonly SemaphoreSlim lifecycle = new(1, 1);
    CancellationTokenSource? lifetime;
    Task? pump;
    bool started, disposed;
    public override string Name => Path.GetFileName(path);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
                throw new InvalidOperationException("Source is single use");
            started = true;
            WaveFileReader? reader = null;
            try
            {
                reader = new WaveFileReader(path);
                Converter = new(reader.WaveFormat);
                lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var ownedReader = reader;
                var token = lifetime.Token;
                pump = Task.Run(async () =>
                {
                    using (ownedReader)
                    {
                        try
                        {
                            var buffer = new byte[ownedReader.WaveFormat.BlockAlign * 4096];
                            int read;
                            while ((read = ownedReader.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                Feed(buffer.AsSpan(0, read));
                                if (realtime)
                                    await Task.Delay(TimeSpan.FromSeconds(
                                    read / (double)ownedReader.WaveFormat.AverageBytesPerSecond), token);
                            }
                            Finish();
                        }
                        catch (OperationCanceledException) { Finish(); }
                        catch (Exception error) { Finish(error); }
                    }
                });
                reader = null; // Ownership transferred only after validation and pump creation.
            }
            catch (Exception error) { Finish(error); throw; }
            finally { reader?.Dispose(); }
        }
        finally { lifecycle.Release(); }
    }

    async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        started = true;
        lifetime?.Cancel();
        if (pump != null)
            await pump.WaitAsync(cancellationToken);
        Finish();
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(cancellationToken);
        }
        finally { lifecycle.Release(); }
    }
    public override async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            disposed = true;
            await StopCoreAsync(default);
            lifetime?.Dispose();
            lifetime = null;
        }
        finally { lifecycle.Release(); }
    }
}
