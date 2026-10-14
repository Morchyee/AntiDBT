using System.Diagnostics;
namespace AntiDbt.Core;
public sealed class TranslationScheduler : IAsyncDisposable
{
    sealed class Job(CaptionSegment latest)
    {
        public CaptionSegment Latest = latest;
        public CancellationTokenSource Cancellation = new();
    }
    readonly SubtitleStore store; readonly ITranslationProvider provider; readonly TranslationConfiguration settings; readonly string target;
    readonly object sync = new(); readonly SemaphoreSlim slots = new(2);
    readonly Dictionary<(long, string), Job> jobs = new(); readonly HashSet<Task> tasks = new(); readonly Dictionary<string, string> cache = new();
    bool disposed;
    int outstandingRequests;
    public LatencyMonitor RequestLatency { get; } = new();
    public event Action<string>? Error;
    public TranslationScheduler(SubtitleStore store, ITranslationProvider provider, TranslationConfiguration settings, string target)
    {
        this.store = store;
        this.provider = provider;
        this.settings = settings;
        this.target = target;
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
                current.Latest = segment;
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
                lock (sync)
                    segment = job.Latest;
                // A fixed coalescing window never restarts on each incoming interim event.
                if (!segment.IsFinal)
                    await Task.Delay(Math.Clamp(settings.DebounceMilliseconds, 0, 2000), token);
                await slots.WaitAsync(token);
                try
                {
                    lock (sync)
                        segment = job.Latest;
                    var context = store.Snapshot().Where(x => x.SessionId == segment.SessionId && x.IsFinal && x.ReceivedTimestamp < segment.ReceivedTimestamp && x.SegmentId != segment.SegmentId)
                        .TakeLast(Math.Clamp(settings.ContextSentences, 0, 12)).Select(x => x.SourceText).ToArray();
                    var key = segment.Language + "\0" + target + "\0" + segment.SourceText + "\0" + string.Join("\u001f", context);
                    string? text;
                    lock (sync)
                        cache.TryGetValue(key, out text);
                    if (text is null)
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
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
                        RequestLatency.Record(Stopwatch.GetElapsedTime(began));
                        token.ThrowIfCancellationRequested();
                        lock (sync)
                        {
                            if (cache.Count >= 128)
                                cache.Remove(cache.Keys.First());
                            cache[key] = text;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if (!store.ApplyTranslation(segment.SessionId, segment.SegmentId, segment.Revision, text))
                        store.ApplyProvisionalTranslation(segment.SessionId, segment.SegmentId, segment.Revision, text);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    if (store.ApplyTranslationError(segment.SessionId, segment.SegmentId, segment.Revision, "翻译请求超时"))
                        Error?.Invoke("翻译请求超时，请检查服务或重试");
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    if (store.ApplyTranslationError(segment.SessionId, segment.SegmentId, segment.Revision, "翻译服务调用失败"))
                        Error?.Invoke("翻译服务调用失败，请检查密钥、权限、配额和网络");
                }
                finally { slots.Release(); }
                lock (sync)
                {
                    if (job.Latest.Revision == segment.Revision)
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
            slots.Dispose();
        }
        catch (OperationCanceledException) { }
    }
}
