using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AntiDbt.Core;

namespace AntiDbt.Providers;

public sealed class HybridStreamingAsrProvider : IAsrProvider
{
    readonly IAsrProvider? _localProvider;
    readonly IAsrProvider? _cloudProvider;
    readonly Func<byte[], AsrConfiguration, CancellationToken, Task<string?>>? _cloudTranscriber;

    public HybridStreamingAsrProvider(
        IAsrProvider? localProvider = null,
        IAsrProvider? cloudProvider = null,
        Func<byte[], AsrConfiguration, CancellationToken, Task<string?>>? cloudTranscriber = null)
    {
        _localProvider = localProvider;
        _cloudProvider = cloudProvider;
        _cloudTranscriber = cloudTranscriber;
    }

    public IAsrSession CreateSession(AsrConfiguration configuration) =>
        new HybridSession(configuration, _localProvider, _cloudProvider, _cloudTranscriber);

    sealed class HybridSession : IAsrSession
    {
        readonly AsrConfiguration _config;
        readonly IAsrProvider _effectiveLocal;
        readonly IAsrProvider? _effectiveCloud;
        readonly Func<byte[], AsrConfiguration, CancellationToken, Task<string?>>? _cloudTranscriber;

        readonly Channel<AsrResult> _results = Channel.CreateBounded<AsrResult>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

        readonly Channel<FinalJob> _finalJobs = Channel.CreateBounded<FinalJob>(new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

        readonly CancellationTokenSource _stop = new();
        readonly object _gate = new();
        readonly List<byte[]> _currentSentencePcm = [];
        int _currentSentencePcmBytes = 0;
        long _bufferStartSample;
        readonly SemaphoreSlim _publishGate = new(1, 1);
        const int MaxSentencePcmBytes = 16000 * 2 * 45; // 45s of 16kHz 16-bit mono (~1.44 MB)

        IAsrSession? _localSession;
        Task? _pumpTask;
        Task? _finalWorkerTask;
        bool _started;
        bool _completed;
        bool _disposed;

        int _segmentCounter = 1;
        int _currentRevision = 0;
        string _currentSegmentId = "hybrid-1";
        TimeSpan _currentSentenceStart = TimeSpan.Zero;

        sealed record FinalJob(
            string SegmentId,
            int Revision,
            byte[] Pcm,
            TimeSpan AudioStart,
            TimeSpan AudioEnd,
            string LocalText,
            string? Language);

        public HybridSession(
            AsrConfiguration config,
            IAsrProvider? localProvider,
            IAsrProvider? cloudProvider,
            Func<byte[], AsrConfiguration, CancellationToken, Task<string?>>? cloudTranscriber)
        {
            _config = config;
            _effectiveLocal = localProvider ?? new SherpaOnnxStreamingAsrProvider();
            _effectiveCloud = cloudProvider ?? (string.IsNullOrWhiteSpace(config.ApiKey) && !config.Options.ContainsKey("cloudApiKey") ? null : new GroqWhisperProvider());
            _cloudTranscriber = cloudTranscriber;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_started)
                    throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
                _started = true;
            }

            _localSession = _effectiveLocal.CreateSession(_config);
            await _localSession.ConnectAsync(cancellationToken);

            _finalWorkerTask = ProcessFinalJobsAsync(_stop.Token);
            _pumpTask = PumpLocalAsync(_stop.Token);
        }

        public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_started || _completed || _disposed || pcm.Length % 2 != 0)
                    throw ProviderHttp.Failure(ProviderErrorKind.Configuration);

                if (pcm.Length > 0 && (_effectiveCloud is not null || _cloudTranscriber is not null))
                {
                    var bytes = pcm.ToArray();
                    _currentSentencePcm.Add(bytes);
                    _currentSentencePcmBytes += bytes.Length;
                    while (_currentSentencePcm.Count > 0 && _currentSentencePcmBytes > MaxSentencePcmBytes)
                    {
                        var dropped = _currentSentencePcm[0];
                        _currentSentencePcm.RemoveAt(0);
                        _currentSentencePcmBytes -= dropped.Length;
                        _bufferStartSample += dropped.Length / 2;
                        CaptionDiagnostics.Record("hybrid", "audio-buffer-overflow", detail: $"samples={dropped.Length / 2}");
                    }
                }
            }

            if (_localSession is not null)
                await _localSession.SendAudioAsync(pcm, cancellationToken);
        }

        private byte[] TakeSentencePcm(TimeSpan end)
        {
            long endSample = (long)Math.Round(end.TotalSeconds * 16000);
            int required = (int)Math.Min(_currentSentencePcmBytes, Math.Max(0, endSample - _bufferStartSample) * 2);
            var pcm = new byte[required];
            int copied = 0;
            while (copied < required)
            {
                var first = _currentSentencePcm[0];
                int take = Math.Min(required - copied, first.Length);
                first.AsSpan(0, take).CopyTo(pcm.AsSpan(copied));
                if (take == first.Length) _currentSentencePcm.RemoveAt(0);
                else _currentSentencePcm[0] = first[take..];
                copied += take;
                _currentSentencePcmBytes -= take;
            }
            _bufferStartSample += required / 2;
            return pcm;
        }

        private async Task PublishResultsAsync(IReadOnlyList<AsrResult> results, CancellationToken token)
        {
            if (results.Count == 0) return;
            await _publishGate.WaitAsync(token);
            try
            {
                for (int i = 0; i < results.Count; i++)
                    await _results.Writer.WriteAsync(results[i] with { BatchSize = i == 0 ? results.Count : 1 }, token);
            }
            finally { _publishGate.Release(); }
        }

        async Task PumpLocalAsync(CancellationToken token)
        {
            try
            {
                if (_localSession is null) return;
                var batch = new List<AsrResult>();
                int expected = 1;
                await foreach (var res in _localSession.GetResultsAsync(token))
                {
                    if (batch.Count == 0) expected = res.BatchSize;
                    batch.Add(res);
                    if (batch.Count < expected) continue;
                    var outgoing = new List<AsrResult>();
                    var jobs = new List<FinalJob>();
                    foreach (var change in batch)
                    {
                        lock (_gate)
                        {
                            string id = _currentSegmentId;
                            int revision = ++_currentRevision;
                            var start = _currentSentenceStart;
                            outgoing.Add(new(id, revision, change.Text, change.IsFinal, start, change.AudioEnd, change.Language));
                            if (!change.IsFinal) continue;
                            if (_effectiveCloud is not null || _cloudTranscriber is not null)
                                jobs.Add(new(id, revision + 1, TakeSentencePcm(change.AudioEnd), start, change.AudioEnd, change.Text, change.Language));
                            _currentSegmentId = $"hybrid-{++_segmentCounter}";
                            _currentRevision = 0;
                            _currentSentenceStart = change.AudioEnd;
                        }
                    }
                    // Publish Final + next Partial together before any slow cloud correction.
                    await PublishResultsAsync(outgoing, token);
                    foreach (var job in jobs) await _finalJobs.Writer.WriteAsync(job, token);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { _results.Writer.TryComplete(ex); }
            finally { _finalJobs.Writer.TryComplete(); }
        }
        private static bool IsWhisperArtifact(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            var trimmed = text.Trim();
            if (trimmed.Equals("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("(silence)", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("[silence]", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("(music)", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("[music]", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("[applause]", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("(applause)", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("(laughter)", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("[laughter]", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        async Task ProcessFinalJobsAsync(CancellationToken token)
        {
            try
            {
                await foreach (var job in _finalJobs.Reader.ReadAllAsync(token))
                {
                    string finalText = job.LocalText;
                    if (job.Pcm.Length >= 3200) // Minimum 100ms PCM for Whisper
                    {
                        string? cloudText = null;
                        try
                        {
                            if (_cloudTranscriber is not null)
                            {
                                using var transcriberCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                                transcriberCts.CancelAfter(TimeSpan.FromSeconds(5));
                                cloudText = await _cloudTranscriber(job.Pcm, _config, transcriberCts.Token);
                            }
                            else if (_effectiveCloud is not null)
                            {
                                var apiKey = !string.IsNullOrWhiteSpace(_config.ApiKey)
                                    ? _config.ApiKey
                                    : _config.Options.GetValueOrDefault("cloudApiKey", "");

                                if (!string.IsNullOrWhiteSpace(apiKey))
                                {
                                    var cloudConfig = new AsrConfiguration
                                    {
                                        Provider = _config.Options.GetValueOrDefault("cloudProvider", "GroqWhisper"),
                                        Endpoint = _config.Options.GetValueOrDefault("cloudEndpoint", "https://api.groq.com/openai/v1/audio/transcriptions"),
                                        Model = _config.Options.GetValueOrDefault("cloudModel", "whisper-large-v3-turbo"),
                                        ApiKey = apiKey,
                                        Language = _config.Language,
                                        Options = new Dictionary<string, string>(_config.Options)
                                    };

                                    using var cloudCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                                    int timeout = int.TryParse(_config.Options.GetValueOrDefault("cloudTimeoutSeconds", "5"), out var s) ? s : 5;
                                    cloudCts.CancelAfter(TimeSpan.FromSeconds(timeout));

                                    await using var cloudSession = _effectiveCloud.CreateSession(cloudConfig);
                                    await cloudSession.ConnectAsync(cloudCts.Token);
                                    await cloudSession.SendAudioAsync(job.Pcm, cloudCts.Token);
                                    await cloudSession.CompleteAsync(cloudCts.Token);
                                    await foreach (var cloudRes in cloudSession.GetResultsAsync(cloudCts.Token))
                                    {
                                        if (cloudRes.IsFinal && !string.IsNullOrWhiteSpace(cloudRes.Text))
                                        {
                                            cloudText = cloudRes.Text.Trim();
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Graceful fallback to local text on error/timeout
                            cloudText = null;
                        }

                        if (!string.IsNullOrWhiteSpace(cloudText) && !IsWhisperArtifact(cloudText))
                            finalText = cloudText;
                    }

                    if (finalText != job.LocalText)
                        await PublishResultsAsync(new[] { new AsrResult(job.SegmentId, job.Revision, finalText, true,
                            job.AudioStart, job.AudioEnd, job.Language) }, token);
                }
                _results.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { _results.Writer.TryComplete(); }
            catch (Exception ex)
            {
                _results.Writer.TryComplete(ex);
            }
        }

        public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken cancellationToken) =>
            _results.Reader.ReadAllAsync(cancellationToken);

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_completed) return;
                _completed = true;
            }

            if (_localSession is not null)
                await _localSession.CompleteAsync(cancellationToken);

            if (_pumpTask is not null)
            {
                try { await _pumpTask.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { }
            }

            _finalJobs.Writer.TryComplete();

            if (_finalWorkerTask is not null)
            {
                try { await _finalWorkerTask.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { }
            }

            _results.Writer.TryComplete();
        }

        public async ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _completed = true;
            }

            _stop.Cancel();

            if (_localSession is not null)
            {
                try { await _localSession.DisposeAsync(); }
                catch { }
            }

            _finalJobs.Writer.TryComplete();
            _results.Writer.TryComplete();
            _stop.Dispose();
        }
    }
}
