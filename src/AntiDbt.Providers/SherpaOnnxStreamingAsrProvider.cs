using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AntiDbt.Core;
using SherpaOnnx;

namespace AntiDbt.Providers;

public sealed class SherpaOnnxStreamingAsrProvider : IAsrProvider
{
    public IAsrSession CreateSession(AsrConfiguration configuration) => new Session(configuration);
    public static string ResolveModelDirectory(AsrConfiguration configuration) => Session.ResolveModelDirectory(configuration);

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

        private TimedSherpaRecognizer? _recognizer;
        private OnlineStream? _stream;
        private Task? _worker;
        private bool _connected;
        private bool _completed;
        private bool _disposed;

        private readonly SentenceConfiguration _sentenceSettings;
        private StreamingSentenceManager? _sentences;
        private long _acceptedSamples, _processedSamples;
        public long AcceptedSamples => Interlocked.Read(ref _acceptedSamples);
        public long ProcessedSamples => Interlocked.Read(ref _processedSamples);
        private TimeSpan _processedAudioTime = TimeSpan.Zero;

        public Session(AsrConfiguration config)
        {
            _config = config;
            _sentenceSettings = SentenceConfiguration.From(config);
        }

        public static string ResolveModelDirectory(AsrConfiguration config)
        {
            var raw = !string.IsNullOrWhiteSpace(config.Model)
                ? config.Model
                : (config.Options.TryGetValue("modelDir", out var modelDir) && !string.IsNullOrWhiteSpace(modelDir)
                    ? modelDir
                    : "models/sherpa-onnx");

            if (Path.IsPathRooted(raw))
                return raw;

            // 1. Check relative to AppContext.BaseDirectory
            var candidate1 = Path.Combine(AppContext.BaseDirectory, raw);
            if (Directory.Exists(candidate1) && File.Exists(Path.Combine(candidate1, "tokens.txt")))
                return candidate1;

            // 2. Check relative to CurrentDirectory
            var candidate2 = Path.Combine(Directory.GetCurrentDirectory(), raw);
            if (Directory.Exists(candidate2) && File.Exists(Path.Combine(candidate2, "tokens.txt")))
                return candidate2;

            // 3. Scan subdirectories under models/ if user configured default or generic path
            foreach (var root in new[] {
                Path.Combine(AppContext.BaseDirectory, "models"),
                Path.Combine(Directory.GetCurrentDirectory(), "models")
            })
            {
                if (Directory.Exists(root))
                {
                    if (File.Exists(Path.Combine(root, "tokens.txt")))
                        return root;
                    try
                    {
                        var subs = Directory.GetDirectories(root);
                        foreach (var sub in subs)
                        {
                            if (File.Exists(Path.Combine(sub, "tokens.txt")))
                                return sub;
                        }
                    }
                    catch { }
                }
            }

            return candidate1;
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
            if (float.TryParse(config.Options.GetValueOrDefault("rule2TrailingSilence", "0.5"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r2))
                recognizerConfig.Rule2MinTrailingSilence = r2;
            var sentenceSettings = SentenceConfiguration.From(config);
            if (float.TryParse(config.Options.GetValueOrDefault("rule3UtteranceLength", "9.0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r3) && float.IsFinite(r3) && r3 > 0)
                recognizerConfig.Rule3MinUtteranceLength = Math.Min(r3, (float)sentenceSettings.MaximumSeconds);
            else recognizerConfig.Rule3MinUtteranceLength = (float)sentenceSettings.MaximumSeconds;

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
                    _recognizer = new TimedSherpaRecognizer(config);
                    _stream = _recognizer.CreateStream();
                    _sentences = new(_sentenceSettings, _config.Language, !string.IsNullOrWhiteSpace(config.ModelConfig.Transducer.Joiner));
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

            if (pcm.Length == 0)
                return;

            // Bound decoder scheduling to 100ms even if a caller submits a multi-second block.
            for (int offset = 0; offset < pcm.Length; offset += 3200)
            {
                int numSamples = Math.Min(3200, pcm.Length - offset) / 2;
                var floatSamples = new float[numSamples];
                for (int i = 0; i < numSamples; i++)
                {
                    short s = BinaryPrimitives.ReadInt16LittleEndian(pcm.Span.Slice(offset + i * 2, 2));
                    floatSamples[i] = s / 32768f;
                }
                await _audioChannel.Writer.WriteAsync(floatSamples, cancellationToken);
                Interlocked.Add(ref _acceptedSamples, numSamples);
            }
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
                var sentences = _sentences;
                if (recognizer is null || stream is null || sentences is null) return;
                long silentSamples = 0;
                async Task EmitAsync(bool finished = false)
                {
                    var raw = recognizer.GetResult(stream);
                    var update = sentences.Observe(raw.Text, _processedAudioTime,
                        TimeSpan.FromSeconds((double)silentSamples / 16000), recognizer.IsEndpoint(stream), finished, raw.WordStarts);
                    foreach (var result in update.Results)
                        await _resultsChannel.Writer.WriteAsync(result, token);
                    if (update.ResetRecognizer && !finished)
                    {
                        // sherpa transducer Reset preserves encoder context and unread features.
                        // Do not recreate the stream, discard a PCM queue, or replay duplicate audio.
                        recognizer.Reset(stream);
                        silentSamples = 0;
                        CaptionDiagnostics.Record("asr", "native-reset", detail: update.Boundary, audioEnd: _processedAudioTime.TotalSeconds);
                    }
                }
                await foreach (var samples in _audioChannel.Reader.ReadAllAsync(token))
                {
                    for (int offset = 0; offset < samples.Length; offset += 320)
                    {
                        int length = Math.Min(320, samples.Length - offset);
                        double energy = 0;
                        for (int i = offset; i < offset + length; i++) energy += samples[i] * samples[i];
                        if (Math.Sqrt(energy / length) < _sentenceSettings.VadThreshold) silentSamples += length;
                        else silentSamples = 0;
                    }
                    stream.AcceptWaveform(16000, samples);
                    Interlocked.Add(ref _processedSamples, samples.Length);
                    _processedAudioTime = TimeSpan.FromSeconds((double)ProcessedSamples / 16000);
                    while (recognizer.IsReady(stream))
                    {
                        token.ThrowIfCancellationRequested();
                        recognizer.Decode(stream);
                    }
                    await EmitAsync();
                }
                token.ThrowIfCancellationRequested();
                // Decode model lookahead at EOF; padding is NOT counted as captured audio.
                stream.AcceptWaveform(16000, new float[9600]);
                stream.InputFinished();
                while (recognizer.IsReady(stream))
                {
                    token.ThrowIfCancellationRequested();
                    recognizer.Decode(stream);
                }
                await EmitAsync(true);
                CaptionDiagnostics.Record("asr", "audio-drained", detail: $"accepted={AcceptedSamples};processed={ProcessedSamples}", audioEnd: _processedAudioTime.TotalSeconds);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _resultsChannel.Writer.TryComplete(ex); }
            finally { _resultsChannel.Writer.TryComplete(); }
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
                    await _worker.WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch { }
            }

            lock (_gate)
            {
                _stream?.Dispose();
                _stream = null;
                _recognizer?.Dispose();
                _recognizer = null;
            }
            _cts.Dispose();
        }
    }
}
