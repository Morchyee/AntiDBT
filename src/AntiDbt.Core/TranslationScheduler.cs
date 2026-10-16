using System.Diagnostics;
namespace AntiDbt.Core;
public sealed class TranslationScheduler : IAsyncDisposable
{
    sealed class Job(CaptionSegment latest)
    {
        public CaptionSegment Latest = latest;
        public CancellationTokenSource Cancellation = new();
        public CancellationTokenSource? Attempt;
    }
    readonly SubtitleStore store; readonly ITranslationProvider provider; readonly TranslationConfiguration settings; readonly string target;
    readonly object sync = new();
    readonly List<(Job Job, TaskCompletionSource Ready)> waiting = new();
    int activeSlots;
    readonly Dictionary<(long, string), Job> jobs = new(); readonly HashSet<Task> tasks = new(); readonly Dictionary<string, string> cache = new();
    readonly PipelineTelemetryCollector? telemetry;
    bool disposed;
    int outstandingRequests;
    long _rateLimitCooldownUntilTimestamp;
    ProviderErrorKind? _terminalErrorKind;

    public bool IsRateLimited(out TimeSpan remaining)
    {
        long now = Stopwatch.GetTimestamp();
        long until = Interlocked.Read(ref _rateLimitCooldownUntilTimestamp);
        if (now < until)
        {
            remaining = TimeSpan.FromSeconds((double)(until - now) / Stopwatch.Frequency);
            return true;
        }
        remaining = TimeSpan.Zero;
        return false;
    }

    public bool HasTerminalError(out ProviderErrorKind? errorKind)
    {
        errorKind = _terminalErrorKind;
        return errorKind is not null;
    }

    public void ResetTerminalError()
    {
        _terminalErrorKind = null;
    }

    private static double? ComputeTotalPipelineMs(CaptionSegment segment)
    {
        if (segment.AudioCapturedTimestamp.HasValue)
        {
            var elapsed = Stopwatch.GetElapsedTime(segment.AudioCapturedTimestamp.Value).TotalMilliseconds;
            return elapsed >= 0 ? elapsed : null;
        }
        if (segment.ReceivedTimestamp > 0)
        {
            return (segment.QueueWaitMs ?? 0) + (segment.AsrLatencyMs ?? 0) + Stopwatch.GetElapsedTime(segment.ReceivedTimestamp).TotalMilliseconds;
        }
        return null;
    }

    public LatencyMonitor RequestLatency { get; } = new();
    public event Action<string>? Error;
    public TranslationScheduler(SubtitleStore store, ITranslationProvider provider, TranslationConfiguration settings, string target, PipelineTelemetryCollector? telemetry = null)
    {
        this.store = store;
        this.provider = provider;
        this.settings = settings;
        this.target = target;
        this.telemetry = telemetry;
    }
    public void Schedule(CaptionSegment segment)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var key = (segment.SessionId, segment.SegmentId);
            if (string.IsNullOrWhiteSpace(segment.SourceText))
            {
                CancelSegment(segment.SessionId, segment.SegmentId);
                return;
            }
            if (jobs.TryGetValue(key, out var current))
            {
                bool preempt = segment.IsFinal && (!current.Latest.IsFinal || segment.Revision != current.Latest.Revision);
                current.Latest = segment;
                if (preempt) current.Attempt?.Cancel();
                return;
            }
            if (jobs.Count >= 128)
            {
                var oldest = jobs.First();
                oldest.Value.Cancellation.Cancel();
                jobs.Remove(oldest.Key);
            }
            var job = new Job(segment);
            jobs.Add(key, job);
            var task = ProcessAsync(job);
            tasks.Add(task);
            _ = task.ContinueWith(done => { lock (sync) tasks.Remove(done); }, TaskScheduler.Default);
        }
    }
    async Task ProcessAsync(Job job)
    {
        await Task.Yield();
        var token = job.Cancellation.Token;
        CaptionSegment segment = job.Latest;
        try
        {
            while (true)
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                lock (sync)
                {
                    segment = job.Latest;
                    job.Attempt = attempt;
                }
                try
                {
                    // A fixed coalescing window never restarts on each incoming interim event.
                    if (!segment.IsFinal)
                        await Task.Delay(Math.Clamp(settings.DebounceMilliseconds, 0, 2000), attempt.Token);
                    await AcquireSlotAsync(job, attempt.Token);
                    try
                    {
                        lock (sync)
                        {
                            attempt.Token.ThrowIfCancellationRequested();
                            segment = job.Latest;
                        }
                        if (_terminalErrorKind is { } terminalKind)
                        {
                            telemetry?.Record(new SegmentTelemetryRecord
                            {
                                SessionId = segment.SessionId,
                                SegmentId = segment.SegmentId,
                                Revision = segment.Revision,
                                IsFinal = segment.IsFinal,
                                Language = segment.Language,
                                QueueWaitMs = segment.QueueWaitMs,
                                AsrServiceMs = segment.AsrLatencyMs,
                                Outcome = TelemetryOutcome.Failed,
                                ErrorKind = terminalKind.ToString()
                            });
                            string msg = $"翻译服务配置异常 ({terminalKind})，请检查配置";
                            if (store.ApplyTranslationError(segment.SessionId, segment.SegmentId, segment.Revision, msg))
                                Error?.Invoke(msg);
                            return;
                        }
                        if (IsRateLimited(out var remaining) && remaining > TimeSpan.Zero)
                        {
                            if (!segment.IsFinal)
                                return;
                            telemetry?.Record(new SegmentTelemetryRecord
                            {
                                SessionId = segment.SessionId,
                                SegmentId = segment.SegmentId,
                                Revision = segment.Revision,
                                IsFinal = segment.IsFinal,
                                Language = segment.Language,
                                QueueWaitMs = segment.QueueWaitMs,
                                AsrServiceMs = segment.AsrLatencyMs,
                                Outcome = TelemetryOutcome.Failed,
                                ErrorKind = ProviderErrorKind.RateLimited.ToString()
                            });
                            string msg = $"翻译服务限流冷却中（剩余 {Math.Ceiling(remaining.TotalSeconds)} 秒）";
                            if (store.ApplyTranslationError(segment.SessionId, segment.SegmentId, segment.Revision, msg))
                                Error?.Invoke(msg);
                            return;
                        }
                        var context = store.Snapshot().Where(x => x.SessionId == segment.SessionId && x.IsFinal && x.ReceivedTimestamp < segment.ReceivedTimestamp && x.SegmentId != segment.SegmentId)
                            .TakeLast(Math.Clamp(settings.ContextSentences, 0, 12)).Select(x => x.SourceText).ToArray();
                        var key = segment.Language + "\0" + target + "\0" + segment.SourceText + "\0" + string.Join("\u001f", context);
                        string? text;
                        lock (sync)
                            cache.TryGetValue(key, out text);
                        if (text is not null)
                        {
                            telemetry?.Record(new SegmentTelemetryRecord
                            {
                                SessionId = segment.SessionId,
                                SegmentId = segment.SegmentId,
                                Revision = segment.Revision,
                                IsFinal = segment.IsFinal,
                                Language = segment.Language,
                                QueueWaitMs = segment.QueueWaitMs,
                                AsrServiceMs = segment.AsrLatencyMs,
                                MtDebounceWaitMs = 0,
                                MtServiceMs = 0,
                                TotalPipelineMs = ComputeTotalPipelineMs(segment),
                                Outcome = TelemetryOutcome.CacheHit
                            });
                        }
                        else
                        {
                            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token);
                            deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 120)));
                            var began = Stopwatch.GetTimestamp();
                            if (Interlocked.Increment(ref outstandingRequests) > 16)
                            {
                                Interlocked.Decrement(ref outstandingRequests);
                                throw new IOException("Translation plugin did not release requests.");
                            }
                            Task<string> request;
                            try
                            {
                                request = provider.TranslateAsync(new(segment.SessionId, segment.SegmentId, segment.Revision, segment.SourceText, segment.Language, target, context), deadline.Token);
                            }
                            catch { Interlocked.Decrement(ref outstandingRequests); throw; }
                            _ = request.ContinueWith(t => { Interlocked.Decrement(ref outstandingRequests); if (t.IsFaulted) _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                            text = await request.WaitAsync(deadline.Token);
                            var serviceMs = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                            RequestLatency.Record(TimeSpan.FromMilliseconds(serviceMs));
                            telemetry?.Record(new SegmentTelemetryRecord
                            {
                                SessionId = segment.SessionId,
                                SegmentId = segment.SegmentId,
                                Revision = segment.Revision,
                                IsFinal = segment.IsFinal,
                                Language = segment.Language,
                                QueueWaitMs = segment.QueueWaitMs,
                                AsrServiceMs = segment.AsrLatencyMs,
                                MtDebounceWaitMs = segment.IsFinal ? 0 : settings.DebounceMilliseconds,
                                MtServiceMs = serviceMs,
                                TotalPipelineMs = ComputeTotalPipelineMs(segment),
                                Outcome = TelemetryOutcome.Success
                            });
                            attempt.Token.ThrowIfCancellationRequested();
                            lock (sync)
                            {
                                if (cache.Count >= 128)
                                    cache.Remove(cache.Keys.First());
                                cache[key] = text;
                            }
                        }
                        attempt.Token.ThrowIfCancellationRequested();
                        if (!store.ApplyTranslation(segment.SessionId, segment.SegmentId, segment.Revision, text))
                            store.ApplyProvisionalTranslation(segment.SessionId, segment.SegmentId, segment.Revision, segment.SourceText, text);
                    }
                    catch (OperationCanceledException)
                    {
                        telemetry?.Record(new SegmentTelemetryRecord
                        {
                            SessionId = segment.SessionId,
                            SegmentId = segment.SegmentId,
                            Revision = segment.Revision,
                            IsFinal = segment.IsFinal,
                            Language = segment.Language,
                            QueueWaitMs = segment.QueueWaitMs,
                            AsrServiceMs = segment.AsrLatencyMs,
                            Outcome = TelemetryOutcome.Cancelled
                        });
                    }
                    catch (ProviderException error) when (!token.IsCancellationRequested)
                    {
                        if (error.Kind == ProviderErrorKind.RateLimited)
                        {
                            var delay = error.RetryAfter ?? TimeSpan.FromSeconds(5);
                            long until = Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency);
                            Interlocked.Exchange(ref _rateLimitCooldownUntilTimestamp, until);
                        }
                        else if (error.Kind is ProviderErrorKind.Authentication
                                 or ProviderErrorKind.Quota
                                 or ProviderErrorKind.Configuration
                                 or ProviderErrorKind.UnsupportedLanguage
                                 or ProviderErrorKind.ModelUnavailable)
                        {
                            _terminalErrorKind = error.Kind;
                        }

                        telemetry?.Record(new SegmentTelemetryRecord
                        {
                            SessionId = segment.SessionId,
                            SegmentId = segment.SegmentId,
                            Revision = segment.Revision,
                            IsFinal = segment.IsFinal,
                            Language = segment.Language,
                            QueueWaitMs = segment.QueueWaitMs,
                            AsrServiceMs = segment.AsrLatencyMs,
                            Outcome = TelemetryOutcome.Failed,
                            ErrorKind = error.Kind.ToString()
                        });
                        if (store.ApplyTranslationError(segment.SessionId, segment.SegmentId, segment.Revision, error.Message))
                            Error?.Invoke(error.Message);
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        telemetry?.Record(new SegmentTelemetryRecord
                        {
                            SessionId = segment.SessionId,
                            SegmentId = segment.SegmentId,
                            Revision = segment.Revision,
                            IsFinal = segment.IsFinal,
                            Language = segment.Language,
                            QueueWaitMs = segment.QueueWaitMs,
                            AsrServiceMs = segment.AsrLatencyMs,
                            Outcome = TelemetryOutcome.Failed,
                            ErrorKind = "GenericFailure"
                        });
                        if (store.ApplyTranslationError(segment.SessionId, segment.SegmentId, segment.Revision, "翻译服务调用失败"))
                            Error?.Invoke("翻译服务调用失败，请检查密钥、权限、配额和网络");
                    }
                    finally { ReleaseSlot(); }
                }
                catch (OperationCanceledException) when (attempt.IsCancellationRequested && !token.IsCancellationRequested) { }
                finally { lock (sync) job.Attempt = null; }
                token.ThrowIfCancellationRequested();
                lock (sync)
                {
                    if (!attempt.IsCancellationRequested && job.Latest.Revision == segment.Revision && job.Latest.IsFinal == segment.IsFinal)
                    {
                        var key = (segment.SessionId, segment.SegmentId);
                        if (jobs.TryGetValue(key, out var active) && ReferenceEquals(active, job))
                            jobs.Remove(key);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            telemetry?.Record(new SegmentTelemetryRecord
            {
                SessionId = segment.SessionId,
                SegmentId = segment.SegmentId,
                Revision = segment.Revision,
                IsFinal = segment.IsFinal,
                Language = segment.Language,
                QueueWaitMs = segment.QueueWaitMs,
                AsrServiceMs = segment.AsrLatencyMs,
                Outcome = TelemetryOutcome.Cancelled
            });
            if (!token.IsCancellationRequested && store.ApplyTranslationError(segment.SessionId, segment.SegmentId, segment.Revision, "翻译请求超时"))
                Error?.Invoke("翻译请求超时，请检查服务或重试");
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && store.ApplyTranslationError(segment.SessionId, segment.SegmentId, segment.Revision, "翻译服务调用失败"))
                Error?.Invoke("翻译服务调用失败，请检查密钥、权限、配额和网络");
        }
        finally
        {
            lock (sync)
            {
                var key = (segment.SessionId, segment.SegmentId);
                if (jobs.TryGetValue(key, out var active) && ReferenceEquals(active, job))
                    jobs.Remove(key);
                job.Cancellation.Dispose();
            }
        }
    }
    async Task AcquireSlotAsync(Job job, CancellationToken token)
    {
        TaskCompletionSource ready;
        lock (sync)
        {
            token.ThrowIfCancellationRequested();
            if (activeSlots < 2 && waiting.Count == 0) { activeSlots++; return; }
            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting.Add((job, ready));
        }
        try { await ready.Task.WaitAsync(token); }
        catch
        {
            bool granted;
            lock (sync) granted = waiting.RemoveAll(x => ReferenceEquals(x.Ready, ready)) == 0;
            if (granted) ReleaseSlot();
            throw;
        }
    }
    void ReleaseSlot()
    {
        lock (sync)
        {
            if (waiting.Count == 0) { activeSlots--; return; }
            int index = waiting.FindIndex(x => x.Job.Latest.IsFinal);
            if (index < 0) index = 0;
            var next = waiting[index]; waiting.RemoveAt(index);
            next.Ready.TrySetResult();
        }
    }
    public void CancelSegment(long session, string id)
    {
        lock (sync)
        {
            if (jobs.Remove((session, id), out var job))
                job.Cancellation.Cancel();
        }
    }
    public void CancelPending()
    {
        lock (sync)
        {
            foreach (var job in jobs.Values)
                job.Cancellation.Cancel();
            jobs.Clear();
        }
    }
    public async Task FlushAsync(CancellationToken token)
    {
        Task[] pending;
        lock (sync)
            pending = tasks.ToArray();
        await Task.WhenAll(pending).WaitAsync(token);
    }
    public async ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
        }
        CancelPending();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await FlushAsync(timeout.Token);
        }
        catch (OperationCanceledException) { }
    }
}
