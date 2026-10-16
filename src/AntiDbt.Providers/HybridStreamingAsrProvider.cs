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

                if (pcm.Length > 0)
                    _currentSentencePcm.Add(pcm.ToArray());
            }

            if (_localSession is not null)
                await _localSession.SendAudioAsync(pcm, cancellationToken);
        }

        async Task PumpLocalAsync(CancellationToken token)
        {
            try
            {
                if (_localSession is null) return;
                await foreach (var res in _localSession.GetResultsAsync(token))
                {
                    if (!res.IsFinal)
                    {
                        string segId;
                        int rev;
                        TimeSpan start, end;
                        lock (_gate)
                        {
                            segId = _currentSegmentId;
                            rev = ++_currentRevision;
                            start = _currentSentenceStart;
                            end = res.AudioEnd;
                        }

                        await _results.Writer.WriteAsync(new AsrResult(
                            segId,
                            rev,
                            res.Text,
                            IsFinal: false,
                            AudioStart: start,
                            AudioEnd: end,
                            Language: res.Language
                        ), token);
                    }
                    else
                    {
                        byte[] pcm;
                        string segId;
                        int finalRev;
                        TimeSpan segStart, segEnd;
                        lock (_gate)
                        {
                            pcm = _currentSentencePcm.SelectMany(x => x).ToArray();
                            _currentSentencePcm.Clear();
                            segId = _currentSegmentId;
                            finalRev = ++_currentRevision;
                            segStart = _currentSentenceStart;
                            segEnd = res.AudioEnd;

                            _segmentCounter++;
                            _currentSegmentId = $"hybrid-{_segmentCounter}";
                            _currentRevision = 0;
                            _currentSentenceStart = segEnd;
                        }

                        var job = new FinalJob(segId, finalRev, pcm, segStart, segEnd, res.Text, res.Language);
                        await _finalJobs.Writer.WriteAsync(job, token);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _results.Writer.TryComplete(ex);
            }
            finally
            {
                _finalJobs.Writer.TryComplete();
            }
        }

        async Task ProcessFinalJobsAsync(CancellationToken token)
        {
            try
            {
                await foreach (var job in _finalJobs.Reader.ReadAllAsync(token))
                {
                    string finalText = job.LocalText;
                    if (job.Pcm.Length > 0)
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

                        if (!string.IsNullOrWhiteSpace(cloudText))
                            finalText = cloudText;
                    }

                    await _results.Writer.WriteAsync(new AsrResult(
                        job.SegmentId,
                        job.Revision,
                        finalText,
                        IsFinal: true,
                        AudioStart: job.AudioStart,
                        AudioEnd: job.AudioEnd,
                        Language: job.Language
                    ), token);
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
