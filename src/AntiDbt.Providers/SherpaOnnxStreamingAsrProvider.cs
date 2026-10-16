using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AntiDbt.Core;
using SherpaOnnx;

namespace AntiDbt.Providers;

public sealed class SherpaOnnxStreamingAsrProvider : IAsrProvider
{
    public IAsrSession CreateSession(AsrConfiguration configuration) => new Session(configuration);

    public sealed class Session : IAsrSession
    {
        private readonly AsrConfiguration _config;
        private readonly Channel<float[]> _audioChannel = Channel.CreateBounded<float[]>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
        private readonly Channel<AsrResult> _resultsChannel = Channel.CreateBounded<AsrResult>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

        private readonly CancellationTokenSource _cts = new();
        private readonly object _gate = new();

        private OnlineRecognizer? _recognizer;
        private OnlineStream? _stream;
        private Task? _worker;
        private bool _connected;
        private bool _completed;
        private bool _disposed;

        private string _currentSegmentId = Guid.NewGuid().ToString("N");
        private int _currentRevision;
        private TimeSpan _currentSegmentStart = TimeSpan.Zero;
        private TimeSpan _processedAudioTime = TimeSpan.Zero;
        private string _lastReportedText = "";

        public Session(AsrConfiguration config)
        {
            _config = config;
        }

        public static string ResolveModelDirectory(AsrConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config.Model))
            {
                if (Path.IsPathRooted(config.Model))
                    return config.Model;
                return Path.Combine(AppContext.BaseDirectory, config.Model);
            }

            if (config.Options.TryGetValue("modelDir", out var modelDir) && !string.IsNullOrWhiteSpace(modelDir))
            {
                if (Path.IsPathRooted(modelDir))
                    return modelDir;
                return Path.Combine(AppContext.BaseDirectory, modelDir);
            }

            return Path.Combine(AppContext.BaseDirectory, "models", "sherpa-onnx");
        }

        private static string? FindModelFile(string dir, string pattern, string? preferExtension = null)
        {
            if (!Directory.Exists(dir))
                return null;

            var matches = Directory.GetFiles(dir, pattern);
            if (matches.Length == 0)
                return null;

            if (preferExtension != null)
            {
                var preferred = matches.FirstOrDefault(m => m.EndsWith(preferExtension, StringComparison.OrdinalIgnoreCase));
                if (preferred != null)
                    return preferred;
            }

            return matches[0];
        }

        public static OnlineRecognizerConfig BuildConfig(AsrConfiguration config)
        {
            var dir = ResolveModelDirectory(config);
            if (!Directory.Exists(dir))
            {
                throw new ProviderException(
                    ProviderErrorKind.ModelUnavailable,
                    $"本地语音识别模型目录不存在: {dir}。请在设置中配置有效模型路径，或下载离线模型。");
            }

            var tokensPath = Path.Combine(dir, "tokens.txt");
            if (!File.Exists(tokensPath))
            {
                throw new ProviderException(
                    ProviderErrorKind.ModelUnavailable,
                    $"本地模型缺失 tokens.txt: {dir}。");
            }

            var encoder = FindModelFile(dir, "*encoder*.onnx", ".int8.onnx");
            var decoder = FindModelFile(dir, "*decoder*.onnx", ".int8.onnx");
            var joiner = FindModelFile(dir, "*joiner*.onnx", ".int8.onnx");

            if (encoder == null || decoder == null)
            {
                throw new ProviderException(
                    ProviderErrorKind.ModelUnavailable,
                    $"本地模型目录缺失关键模型文件（未找到 encoder 或 decoder onnx）: {dir}。");
            }

            var recognizerConfig = new OnlineRecognizerConfig();
            recognizerConfig.FeatConfig.SampleRate = 16000;
            recognizerConfig.FeatConfig.FeatureDim = 80;

            if (joiner != null)
            {
                recognizerConfig.ModelConfig.Transducer.Encoder = encoder;
                recognizerConfig.ModelConfig.Transducer.Decoder = decoder;
                recognizerConfig.ModelConfig.Transducer.Joiner = joiner;
            }
            else
            {
                recognizerConfig.ModelConfig.Paraformer.Encoder = encoder;
                recognizerConfig.ModelConfig.Paraformer.Decoder = decoder;
            }

            recognizerConfig.ModelConfig.Tokens = tokensPath;
            recognizerConfig.ModelConfig.Provider = config.Options.GetValueOrDefault("provider", "cpu");

            int threads = 2;
            if (config.Options.TryGetValue("numThreads", out var threadsStr) && int.TryParse(threadsStr, out var parsedThreads))
            {
                threads = Math.Clamp(parsedThreads, 1, 8);
            }
            recognizerConfig.ModelConfig.NumThreads = threads;
            recognizerConfig.ModelConfig.Debug = 0;

            recognizerConfig.DecodingMethod = config.Options.GetValueOrDefault("decodingMethod", "greedy_search");
            recognizerConfig.EnableEndpoint = 1;

            if (float.TryParse(config.Options.GetValueOrDefault("rule1TrailingSilence", "2.4"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r1))
                recognizerConfig.Rule1MinTrailingSilence = r1;
            if (float.TryParse(config.Options.GetValueOrDefault("rule2TrailingSilence", "1.2"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r2))
                recognizerConfig.Rule2MinTrailingSilence = r2;
            if (float.TryParse(config.Options.GetValueOrDefault("rule3UtteranceLength", "20.0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r3))
                recognizerConfig.Rule3MinUtteranceLength = r3;

            return recognizerConfig;
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_connected)
                    throw new ProviderException(ProviderErrorKind.Configuration, "语音会话已连接。");

                try
                {
                    var config = BuildConfig(_config);
                    _recognizer = new OnlineRecognizer(config);
                    _stream = _recognizer.CreateStream();
                    _connected = true;
                    _worker = Task.Run(() => ProcessLoopAsync(_cts.Token), CancellationToken.None);
                }
                catch (ProviderException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new ProviderException(ProviderErrorKind.ModelUnavailable, $"初始化本地流式 ASR 模型失败: {ex.Message}");
                }
            }
            return Task.CompletedTask;
        }

        public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pcm.Length % 2 != 0)
                throw new ArgumentException("PCM 音频块长度无效（必须为 16-bit 偶数字节）。");

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_connected || _completed)
                    throw new InvalidOperationException("语音会话未就绪或已结束。");
            }

            int numSamples = pcm.Length / 2;
            var floatSamples = new float[numSamples];
            for (int i = 0; i < numSamples; i++)
            {
                short s = BinaryPrimitives.ReadInt16LittleEndian(pcm.Span.Slice(i * 2, 2));
                floatSamples[i] = s / 32768f;
            }

            await _audioChannel.Writer.WriteAsync(floatSamples, cancellationToken);
        }

        public async IAsyncEnumerable<AsrResult> GetResultsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            await foreach (var result in _resultsChannel.Reader.ReadAllAsync(linked.Token))
            {
                yield return result;
            }
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_connected || _completed || _disposed)
                    return;
                _completed = true;
            }

            _audioChannel.Writer.TryComplete();

            if (_worker != null)
            {
                try
                {
                    await _worker.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) { }
            }
        }

        private async Task ProcessLoopAsync(CancellationToken token)
        {
            try
            {
                var recognizer = _recognizer;
                var stream = _stream;
                if (recognizer == null || stream == null)
                    return;

                await foreach (var samples in _audioChannel.Reader.ReadAllAsync(token))
                {
                    stream.AcceptWaveform(16000, samples);
                    _processedAudioTime += TimeSpan.FromSeconds((double)samples.Length / 16000);

                    while (recognizer.IsReady(stream))
                    {
                        recognizer.Decode(stream);
                    }

                    bool isEndpoint = recognizer.IsEndpoint(stream);
                    var rawResult = recognizer.GetResult(stream);
                    var text = rawResult?.Text?.Trim() ?? "";

                    if (isEndpoint)
                    {
                        if (!string.IsNullOrEmpty(text))
                        {
                            _currentRevision++;
                            var finalResult = new AsrResult(
                                _currentSegmentId,
                                _currentRevision,
                                text,
                                IsFinal: true,
                                _currentSegmentStart,
                                _processedAudioTime,
                                _config.Language);

                            await _resultsChannel.Writer.WriteAsync(finalResult, token);
                        }

                        recognizer.Reset(stream);
                        _currentSegmentStart = _processedAudioTime;
                        _currentSegmentId = Guid.NewGuid().ToString("N");
                        _currentRevision = 0;
                        _lastReportedText = "";
                    }
                    else if (!string.IsNullOrEmpty(text) && text != _lastReportedText)
                    {
                        _currentRevision++;
                        _lastReportedText = text;
                        var partialResult = new AsrResult(
                            _currentSegmentId,
                            _currentRevision,
                            text,
                            IsFinal: false,
                            _currentSegmentStart,
                            _processedAudioTime,
                            _config.Language);

                        await _resultsChannel.Writer.WriteAsync(partialResult, token);
                    }
                }

                // Audio stream completed, drain final tokens
                stream.InputFinished();
                while (recognizer.IsReady(stream))
                {
                    recognizer.Decode(stream);
                }

                var finalDrain = recognizer.GetResult(stream);
                var remainingText = finalDrain?.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(remainingText))
                {
                    _currentRevision++;
                    var finalResult = new AsrResult(
                        _currentSegmentId,
                        _currentRevision,
                        remainingText,
                        IsFinal: true,
                        _currentSegmentStart,
                        _processedAudioTime,
                        _config.Language);

                    await _resultsChannel.Writer.WriteAsync(finalResult, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Surface unexpected worker failures
                _resultsChannel.Writer.TryComplete(ex);
                return;
            }
            finally
            {
                _resultsChannel.Writer.TryComplete();
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            _cts.Cancel();
            _audioChannel.Writer.TryComplete();
            _resultsChannel.Writer.TryComplete();

            if (_worker != null)
            {
                try
                {
                    await _worker.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch { }
            }

            _stream?.Dispose();
            _recognizer?.Dispose();
            _cts.Dispose();
        }
    }
}
