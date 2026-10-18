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

    private static readonly HashSet<string> s_trailingConjunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "but", "so", "for", "nor", "yet", "because", "although", "though",
        "while", "whereas", "if", "unless", "since", "as", "after", "before", "once",
        "supposing", "provided", "providing"
    };

    private static readonly HashSet<string> s_trailingPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "with", "for", "to", "in", "on", "at", "by", "from", "about", "into",
        "through", "during", "between", "under", "over", "without", "per", "of",
        "against", "among", "toward", "towards", "upon", "within", "like"
    };

    private static readonly HashSet<string> s_trailingAuxiliaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "is", "are", "was", "were", "am", "be", "been", "being",
        "have", "has", "had", "do", "does", "did",
        "will", "would", "shall", "should", "can", "could", "may", "might", "must"
    };

    private static readonly HashSet<string> s_trailingDeterminers = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "this", "that", "these", "those",
        "my", "your", "his", "her", "its", "our", "their", "each", "every", "some", "any"
    };

    private static readonly HashSet<string> s_trailingSubordinators = new(StringComparer.OrdinalIgnoreCase)
    {
        "which", "who", "whom", "whose", "whether", "where", "when", "whenever", "wherever", "how", "why"
    };

    private static readonly HashSet<string> s_trailingPronouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "we", "i", "you", "he", "she", "it", "they"
    };

    public static bool IsIncompleteClause(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        char lastChar = trimmed[^1];
        if (lastChar is '.' or '?' or '!' or '。' or '？' or '！') return false;
        if (lastChar is ',' or ';' or ':' or '-' or '…') return true;

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;

        var lastWord = words[^1].Trim(',', ';', ':', '-', '\"', '\'').ToLowerInvariant();
        if (s_trailingConjunctions.Contains(lastWord)) return true;
        if (s_trailingPrepositions.Contains(lastWord)) return true;
        if (s_trailingAuxiliaries.Contains(lastWord)) return true;
        if (s_trailingDeterminers.Contains(lastWord)) return true;
        if (s_trailingSubordinators.Contains(lastWord)) return true;
        if (words.Length >= 2 && s_trailingPronouns.Contains(lastWord)) return true;

        return false;
    }

    private readonly HashSet<(long SessionId, string SegmentId)> _retranslated = new();
    private readonly HashSet<(long SessionId, string SegmentId)> _translatedWithLookahead = new();

    private void TryTriggerContextualRevision(CaptionSegment current)
    {
        if (!settings.EnableContextAwareTranslation || settings.ContextSentences == 0) return;
        var snapshot = store.Snapshot();
        var previous = snapshot.LastOrDefault(x => x.SessionId == current.SessionId && x.IsFinal && x.ReceivedTimestamp < current.ReceivedTimestamp);
        if (previous is null || !IsIncompleteClause(previous.SourceText)) return;
        lock (sync)
        {
            if (disposed || _translatedWithLookahead.Contains((previous.SessionId, previous.SegmentId)) || !_retranslated.Add((previous.SessionId, previous.SegmentId))) return;
        }

        bool supportsContext = settings.Provider is "DeepL" or "GroqLlm" or "OpenAiCompatible";
        var revisionJob = new Job(previous);
        var revisionTask = Task.Run(async () =>
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 30)));
                await AcquireSlotAsync(revisionJob, deadline.Token);
                try
                {
                    if (!supportsContext)
                    {
                        string combinedSource = previous.SourceText + " " + current.SourceText;
                        var req = new TranslationRequest(previous.SessionId, previous.SegmentId, previous.Revision, combinedSource, previous.Language, target, Array.Empty<string>());
                        string combinedTranslation = await provider.TranslateAsync(req, deadline.Token);
                        if (!string.IsNullOrWhiteSpace(combinedTranslation))
                        {
                            store.AssignTranslationDisplayGroup(previous.SessionId, new[] { previous.SegmentId, current.SegmentId }, combinedTranslation);
                            CaptionDiagnostics.Record("semantic", "display-group-applied", previous.SessionId, previous.SegmentId, previous.Revision,
                                sourceLength: combinedSource.Length, translationLength: combinedTranslation.Length, detail: current.SegmentId,
                                sourceText: combinedSource, translationText: combinedTranslation);
                        }
                    }
                    else
                    {
                        var prevContext = store.Snapshot().Where(x => x.SessionId == previous.SessionId && x.IsFinal && x.ReceivedTimestamp < previous.ReceivedTimestamp)
                            .TakeLast(Math.Clamp(settings.ContextSentences, 0, 12)).Select(x => x.SourceText).ToArray();
                        var req = new TranslationRequest(previous.SessionId, previous.SegmentId, previous.Revision, previous.SourceText, previous.Language, target, prevContext, current.SourceText);
                        string revised = await provider.TranslateAsync(req, deadline.Token);
                        if (!string.IsNullOrWhiteSpace(revised))
                        {
                            store.ApplyTranslation(previous.SessionId, previous.SegmentId, previous.Revision, revised);
                            CaptionDiagnostics.Record("semantic", "contextual-revision-applied", previous.SessionId, previous.SegmentId, previous.Revision,
                                sourceLength: previous.SourceText.Length, translationLength: revised.Length, detail: $"triggeredBy={current.SegmentId}",
                                sourceText: previous.SourceText, translationText: revised, lookahead: current.SourceText);
                        }
                    }
                }
                finally
                {
                    ReleaseSlot();
                }
            }
            catch (Exception ex)
            {
                CaptionDiagnostics.Record("semantic", "revision-failed", previous.SessionId, previous.SegmentId, previous.Revision, detail: ex.Message);
            }
        });

        lock (sync)
        {
            if (!disposed)
            {
                tasks.Add(revisionTask);
                _ = revisionTask.ContinueWith(done => { lock (sync) tasks.Remove(done); }, TaskScheduler.Default);
            }
        }
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
        var actual = store.Snapshot().FirstOrDefault(x => x.SessionId == segment.SessionId && x.SegmentId == segment.SegmentId);
        if (actual is null || actual.Revision != segment.Revision ||
            actual.TranslationText.Length > 0 && actual.TranslationRevision == actual.Revision)
            return;
        if (segment.IsFinal)
        {
            TryTriggerContextualRevision(segment);
        }
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
                    {
                        await Task.Delay(Math.Clamp(settings.DebounceMilliseconds, 0, 2000), attempt.Token);
                    }
                    else if (settings.EnableContextAwareTranslation && IsIncompleteClause(segment.SourceText))
                    {
                        int waitMs = settings.DebounceMilliseconds == 0 ? 0 : Math.Clamp(settings.SemanticWaitMilliseconds, 200, 1000);
                        var nextSeg = store.Snapshot().FirstOrDefault(x => x.SessionId == segment.SessionId && x.ReceivedTimestamp > segment.ReceivedTimestamp && !string.IsNullOrWhiteSpace(x.SourceText));
                        if (nextSeg is null && waitMs > 0)
                        {
                            CaptionDiagnostics.Record("semantic", "wait-for-lookahead", segment.SessionId, segment.SegmentId, segment.Revision,
                                detail: $"waitMs={waitMs}", sourceText: segment.SourceText);
                            try
                            {
                                await Task.Delay(waitMs, attempt.Token);
                            }
                            catch (OperationCanceledException) when (attempt.IsCancellationRequested && !token.IsCancellationRequested)
                            {
                                throw;
                            }
                        }
                    }
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

                        string? lookahead = null;
                        if (settings.EnableContextAwareTranslation)
                        {
                            var nextSeg = store.Snapshot().FirstOrDefault(x => x.SessionId == segment.SessionId && x.ReceivedTimestamp > segment.ReceivedTimestamp && !string.IsNullOrWhiteSpace(x.SourceText));
                            if (nextSeg is not null)
                            {
                                lookahead = nextSeg.SourceText;
                                lock (sync) _translatedWithLookahead.Add((segment.SessionId, segment.SegmentId));
                                CaptionDiagnostics.Record("semantic", "lookahead-acquired", segment.SessionId, segment.SegmentId, segment.Revision,
                                    detail: nextSeg.SegmentId, sourceText: segment.SourceText, lookahead: lookahead);
                            }
                        }

                        var key = segment.Language + "\0" + target + "\0" + segment.SourceText + "\0" + string.Join("\u001f", context) + "\0" + (lookahead ?? "");
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
                            CaptionDiagnostics.Record("translation", "request-dispatched", segment.SessionId, segment.SegmentId, segment.Revision,
                                sourceLength: segment.SourceText.Length, detail: $"contextCount={context.Length};hasLookahead={lookahead != null}",
                                sourceText: segment.SourceText, context: string.Join(" \u2223 ", context), lookahead: lookahead);
                            Task<string> request;
                            try
                            {
                                request = provider.TranslateAsync(new(segment.SessionId, segment.SegmentId, segment.Revision, segment.SourceText, segment.Language, target, context, lookahead), deadline.Token);
                            }
                            catch { Interlocked.Decrement(ref outstandingRequests); throw; }
                            _ = request.ContinueWith(t => { Interlocked.Decrement(ref outstandingRequests); if (t.IsFaulted) _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                            text = await request.WaitAsync(deadline.Token);
                            if (string.IsNullOrWhiteSpace(text))
                                throw new IOException("Translation service returned empty text.");
                            var serviceMs = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                            RequestLatency.Record(TimeSpan.FromMilliseconds(serviceMs));
                            CaptionDiagnostics.Record("translation", "response-received", segment.SessionId, segment.SegmentId, segment.Revision,
                                sourceLength: segment.SourceText.Length, translationLength: text.Length,
                                sourceText: segment.SourceText, translationText: text);
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
                        if (!attempt.IsCancellationRequested && !token.IsCancellationRequested &&
                            store.ApplyTranslationError(segment.SessionId, segment.SegmentId, segment.Revision, "翻译请求超时"))
                            Error?.Invoke("翻译请求超时，请检查服务或重试");
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
