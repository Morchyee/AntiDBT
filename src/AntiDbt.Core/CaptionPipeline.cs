using System.Diagnostics;
namespace AntiDbt.Core;
public enum PipelineState
{
    Stopped, Connecting, Listening, Retrying, Stopping, Error
}
public sealed record PipelineStatus(PipelineState State, string Message, long DroppedChunks = 0);
public sealed class CaptionPipeline : IAsyncDisposable
{
    private readonly IAudioSource _audio; private readonly IAsrProvider _asr; private readonly AppSettings _settings;
    private readonly TranslationScheduler _translations; private readonly BoundedAudioQueue _queue = new(50);
    private CancellationTokenSource? _lifetime; private Task? _capture;
    private volatile bool _stopRequested, _captureEnded; private bool _disposed;
    private readonly SemaphoreSlim _stopGate = new(1, 1); private bool _stopCompleted; private volatile bool _stopAttempted;
    private readonly SemaphoreSlim _disposeGate = new(1, 1); private bool _disposalStarted;
    public SubtitleStore Store
    {
        get;
    }
    public LatencyMonitor AsrLatency { get; } = new();
    public LatencyMonitor TranslationLatency => _translations.RequestLatency;
    public AudioQueueStats AudioStats => _queue.GetStats();
    public long DroppedAudioChunks => _queue.DroppedChunks;
    public long AudioDiscontinuities => _queue.Discontinuities;
    public TimeSpan StableHealthyPeriod { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMilliseconds(5000);
    public bool ClearStoreOnStart { get; set; }
    public PipelineTelemetryCollector Telemetry { get; } = new();
    public event Action<PipelineStatus>? StatusChanged;
    public event Action<double>? AudioLevelChanged;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public CaptionPipeline(IAudioSource audio, IAsrProvider asr, ITranslationProvider translator, AppSettings settings, SubtitleStore? store = null)
    {
        _audio = audio;
        _asr = asr;
        _settings = settings;
        Store = store ?? new(settings.MaxHistory);
        _translations = new(Store, translator, settings.Translation, settings.TargetLanguage);
        _translations.Error += message => Publish(PipelineState.Listening, message);
    }
    public Task StartAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed || _disposalStarted, this);
        if (_lifetime is not null)
            throw new InvalidOperationException("Pipeline instances are single use.");
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Completion = RunAsync(_lifetime.Token);
        return Task.CompletedTask;
    }
    private void Publish(PipelineState state, string message) => StatusChanged?.Invoke(new(state, message, _queue.DroppedChunks));
    private async Task CaptureAsync(CancellationToken token)
    {
        var zeroDuration = TimeSpan.Zero;
        try
        {
            await foreach (var chunk in _audio.ReadChunksAsync(token))
            {
                double sum = 0;
                bool zero = true;
                for (int i = 0; i + 1 < chunk.Pcm.Length; i += 2)
                {
                    short sample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(chunk.Pcm.AsSpan(i));
                    zero &= sample == 0;
                    double value = sample / 32768d;
                    sum += value * value;
                }
                AudioLevelChanged?.Invoke(Math.Sqrt(sum / Math.Max(1, chunk.Pcm.Length / 2)));
                zeroDuration = zero ? zeroDuration + chunk.AudioDuration : TimeSpan.Zero;
                if (!zero || zeroDuration <= TimeSpan.FromSeconds(Math.Clamp(_settings.SilenceSeconds, 1, 60)))
                    _queue.Enqueue(chunk);
            }
        }
        finally { _captureEnded = true; }
    }
    private async Task RunAsync(CancellationToken token)
    {
        bool failed = false;
        int failures = 0;
        bool first = true;
        try
        {
            Publish(PipelineState.Connecting, "正在启动系统音频采集…");
            await _audio.StartAsync(token);
            _capture = CaptureAsync(token);
            while (!token.IsCancellationRequested)
            {
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
                await using var session = _asr.CreateSession(_settings.Asr);
                Task? reader = null;
                long sessionConnectedTimestamp = 0;
                try
                {
                    Publish(failures == 0 ? PipelineState.Connecting : PipelineState.Retrying, "正在连接识别服务…");
                    using (var connectDeadline = CancellationTokenSource.CreateLinkedTokenSource(connection.Token))
                    {
                        connectDeadline.CancelAfter(TimeSpan.FromSeconds(12));
                        await session.ConnectAsync(connectDeadline.Token);
                    }
                    if (token.IsCancellationRequested)
                        break;
                    sessionConnectedTimestamp = Stopwatch.GetTimestamp();
                    _translations.CancelPending();
                    var generation = Store.BeginSession(ClearStoreOnStart && first);
                    first = false;
                    Publish(PipelineState.Listening, "正在聆听系统音频");
                    var anchors = new List<(double End, long Timestamp)>();
                    var timelineLock = new object();
                    double sentSeconds = 0;
                    bool receivedResult = false;
                    reader = ReadResultsAsync(session, generation, anchors, timelineLock, connection, () => receivedResult = true);
                    long lastSent = Stopwatch.GetTimestamp();
                    while (!token.IsCancellationRequested && (!_captureEnded || _queue.Count > 0))
                    {
                        if (reader.IsCompleted)
                        {
                            await reader;
                            throw new IOException("Recognition stream ended unexpectedly.");
                        }
                        int trimmed = _queue.TrimStale(TimeSpan.FromSeconds(2.0));
                        if (trimmed > 0)
                            Publish(PipelineState.Listening, $"正在聆听系统音频（跳过 {trimmed} 块过期积压）");

                        AudioChunk? chunk = null;
                        if (!_queue.TryDequeue(out chunk))
                        {
                            if (!_stopRequested && Stopwatch.GetElapsedTime(lastSent) > TimeSpan.FromSeconds(2))
                                chunk = new(new byte[6400], TimeSpan.Zero, TimeSpan.FromMilliseconds(200), Stopwatch.GetTimestamp());
                            else
                            {
                                await Task.Delay(20, connection.Token);
                                continue;
                            }
                        }
                        using (var sendDeadline = CancellationTokenSource.CreateLinkedTokenSource(connection.Token))
                        {
                            sendDeadline.CancelAfter(TimeSpan.FromSeconds(5));
                            await session.SendAudioAsync(chunk!.Pcm, sendDeadline.Token);
                        }
                        if (failures > 0 && receivedResult && Stopwatch.GetElapsedTime(sessionConnectedTimestamp) >= StableHealthyPeriod)
                        {
                            failures = 0;
                        }
                        lastSent = Stopwatch.GetTimestamp();
                        sentSeconds += chunk.AudioDuration.TotalSeconds;
                        lock (timelineLock)
                        {
                            anchors.Add((sentSeconds, chunk.CapturedTimestamp));
                            if (anchors.Count > 1000)
                                anchors.RemoveAt(0);
                        }
                        // Pacing prevents a recovery backlog from monopolizing the socket or violating real-time API limits.
                        if (_queue.Count > 0)
                            await Task.Delay(Math.Min(200, Math.Max(1, (int)chunk.AudioDuration.TotalMilliseconds)), connection.Token);
                    }
                    if (_capture is not null)
                        await _capture;
                    using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    await session.CompleteAsync(drain.Token);
                    try
                    {
                        await reader.WaitAsync(drain.Token);
                    }
                    catch (OperationCanceledException) { connection.Cancel(); }
                    using var translateDrain = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        await _translations.FlushAsync(translateDrain.Token);
                    }
                    catch (OperationCanceledException) { _translations.CancelPending(); }
                    break;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception rawError)
                {
                    Exception effectiveError = rawError;
                    if (reader is not null)
                    {
                        if (reader.IsFaulted && reader.Exception?.InnerException is not null)
                        {
                            effectiveError = reader.Exception.InnerException;
                        }
                        else if (!reader.IsCompleted)
                        {
                            try
                            {
                                await reader.WaitAsync(TimeSpan.FromMilliseconds(200));
                            }
                            catch
                            {
                                if (reader.IsFaulted && reader.Exception?.InnerException is not null)
                                    effectiveError = reader.Exception.InnerException;
                            }
                        }
                    }

                    if (effectiveError is ProviderException fatal && fatal.Kind is ProviderErrorKind.Configuration
                        or ProviderErrorKind.Authentication
                        or ProviderErrorKind.Quota
                        or ProviderErrorKind.ModelUnavailable
                        or ProviderErrorKind.UnsupportedLanguage)
                    {
                        throw fatal;
                    }

                    if (!_stopRequested && !_captureEnded && !token.IsCancellationRequested && failures < 10)
                    {
                        connection.Cancel();
                        _translations.CancelPending();
                        Store.BeginSession(false);
                        failures++;
                        Publish(PipelineState.Retrying, $"识别连接中断，正在重试 {failures}/10");
                        TimeSpan retry;
                        if (effectiveError is ProviderException { Kind: ProviderErrorKind.RateLimited, RetryAfter: { } interval })
                        {
                            if (interval > TimeSpan.FromMinutes(2))
                            {
                                throw new ProviderException(ProviderErrorKind.RateLimited, $"服务限流冷却时间为 {interval.TotalSeconds:F0} 秒，超出自动重试上限，请稍后手动重试。", interval);
                            }
                            retry = interval < TimeSpan.Zero ? TimeSpan.Zero : interval;
                        }
                        else
                        {
                            double factor = Math.Pow(2, Math.Min(failures - 1, 4));
                            double delayMs = Math.Min(MaxRetryDelay.TotalMilliseconds, InitialRetryDelay.TotalMilliseconds * factor);
                            retry = TimeSpan.FromMilliseconds(delayMs);
                        }
                        await Task.Delay(retry, token);
                    }
                    else
                    {
                        throw effectiveError;
                    }
                }
                finally
                {
                    connection.Cancel();
                    if (reader is not null)
                    {
                        try
                        {
                            await reader.WaitAsync(TimeSpan.FromSeconds(1));
                        }
                        catch { _ = reader.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ProviderException error)
        {
            failed = true;
            Publish(PipelineState.Error, $"{error.Kind}: {error.Message}");
        }
        catch (Exception)
        {
            failed = true;
            Publish(PipelineState.Error, "音频或识别服务异常，请检查输出设备、API 权限和网络后重试");
        }
        finally
        {
            using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _audio.StopAsync(stopDeadline.Token);
            }
            catch { }
            if (_capture is not null)
            {
                try
                {
                    await _capture.WaitAsync(stopDeadline.Token);
                }
                catch { }
            }
            _translations.CancelPending();
            _queue.Clear();
            if (!failed)
                Publish(PipelineState.Stopped, "已停止");
        }
    }
    private async Task ReadResultsAsync(IAsrSession session, long generation, List<(double End, long Timestamp)> anchors, object timelineLock, CancellationTokenSource connection, Action? onResult = null)
    {
        try
        {
            await foreach (var result in session.GetResultsAsync(connection.Token))
            {
                onResult?.Invoke();
                if (result.IsDeleted)
                {
                    Store.ApplySource(generation, result);
                    _translations.CancelSegment(generation, result.SegmentId);
                    continue;
                }
                var segment = Store.ApplySource(generation, result);
                if (segment is null)
                    continue;
                long? audioEnd = null;
                lock (timelineLock)
                {
                    var match = anchors.FirstOrDefault(x => x.End >= result.AudioEnd.TotalSeconds);
                    if (match.Timestamp != 0)
                        audioEnd = match.Timestamp - (long)((match.End - result.AudioEnd.TotalSeconds) * Stopwatch.Frequency);
                }
                if (audioEnd is { } timestamp)
                {
                    var elapsed = Stopwatch.GetElapsedTime(timestamp);
                    if (elapsed >= TimeSpan.Zero && elapsed <= TimeSpan.FromSeconds(30))
                        AsrLatency.Record(elapsed);
                }
                _translations.Schedule(segment);
            }
        }
        catch (Exception)
        {
            try { connection.Cancel(); } catch { }
            throw;
        }
    }
    public bool RetryTranslation(long session, string segmentId, int revision)
    {
        if (session != Store.CurrentSession)
            return false;
        CaptionSegment? target = null;
        foreach (var row in Store.Snapshot())
        {
            if (row.SessionId == session && row.SegmentId == segmentId && row.Revision == revision && row.IsFinal)
            {
                target = row;
                break;
            }
        }
        if (target is null || string.IsNullOrWhiteSpace(target.SourceText))
            return false;
        if (!Store.ClearTranslationError(session, segmentId, revision))
            return false;
        _translations.Schedule(target with { TranslationError = null });
        return true;
    }
    public Task StopAsync(CancellationToken token) => StopAsync(StopReason.UserStop, token);
    public async Task StopAsync(StopReason reason = StopReason.UserStop, CancellationToken token = default)
    {
        await _stopGate.WaitAsync(token);
        try
        {
            if (_stopCompleted || Completion.IsCompleted)
            {
                _stopCompleted = true;
                return;
            }
            if (_lifetime is null)
            {
                _stopCompleted = true;
                return;
            }
            _stopAttempted = true;
            _stopRequested = true;
            if (reason is StopReason.Reconfigure or StopReason.Shutdown)
            {
                Publish(PipelineState.Stopping, reason == StopReason.Reconfigure ? "正在切换配置并停止旧会话…" : "正在停止…");
                _lifetime.Cancel();
                _translations.CancelPending();
                if (reason == StopReason.Reconfigure)
                    Store.BeginSession(false);
                using var quickDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                quickDeadline.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await _audio.StopAsync(quickDeadline.Token);
                    await Completion.WaitAsync(quickDeadline.Token);
                    _stopCompleted = true;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("Pipeline reconfiguration stop timed out before background session completion.");
                }
                return;
            }

            Publish(PipelineState.Stopping, "正在停止并处理尾部字幕…");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await _audio.StopAsync(deadline.Token);
                await Completion.WaitAsync(deadline.Token);
                _stopCompleted = true;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                _lifetime.Cancel();
                using var drainTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                drainTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await Completion.WaitAsync(drainTimeout.Token);
                    _stopCompleted = true;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("Pipeline stop timed out before background session completion.");
                }
            }
            _translations.CancelPending();
            Store.BeginSession(false);
        }
        finally { _stopGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await _disposeGate.WaitAsync();
        try
        {
            if (_disposed)
                return;
            _disposalStarted = true;
            _lifetime?.Cancel();

            Exception? stopEx = null;
            if (Completion.IsCompleted)
            {
                _stopCompleted = true;
            }
            else if (!_stopCompleted && !_stopAttempted)
            {
                try
                {
                    await StopAsync(StopReason.Shutdown);
                }
                catch (Exception ex)
                {
                    stopEx = ex;
                }
            }

            Exception? audioEx = null;
            try
            {
                await _audio.DisposeAsync();
            }
            catch (Exception ex)
            {
                audioEx = ex;
            }

            Exception? transEx = null;
            try
            {
                await _translations.DisposeAsync();
            }
            catch (Exception ex)
            {
                transEx = ex;
            }

            if (audioEx is not null)
                throw audioEx;
            if (transEx is not null)
                throw transEx;

            if (Completion.IsCompleted)
            {
                _disposed = true;
                _stopCompleted = true;
                _lifetime?.Dispose();
                if (stopEx is not null && stopEx is not TimeoutException)
                    throw stopEx;
            }
            else
            {
                throw new TimeoutException("Pipeline DisposeAsync timed out; background session remains active.", stopEx);
            }
        }
        finally { _disposeGate.Release(); }
    }
}
