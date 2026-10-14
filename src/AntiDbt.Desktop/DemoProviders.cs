using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AntiDbt.Core;
namespace AntiDbt.Desktop;
// Explicit local demonstration only. These authored fixtures do not recognize audio.
internal sealed class DemoAudioSource : IAudioSource
{
    readonly CancellationTokenSource stop = new();
    public string Name => "本地合成演示";
    public Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public async IAsyncEnumerable<AudioChunk> ReadChunksAsync([EnumeratorCancellation] CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        for (int i = 0; i < 75 && !linked.IsCancellationRequested; i++)
        {
            var pcm = new byte[6400];
            pcm[0] = 1;
            yield return new(pcm, TimeSpan.FromMilliseconds(i * 200), TimeSpan.FromMilliseconds(200), Stopwatch.GetTimestamp());
            try
            {
                await Task.Delay(200, linked.Token);
            }
            catch (OperationCanceledException) { yield break; }
        }
    }
    public Task StopAsync(CancellationToken token)
    {
        stop.Cancel();
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        stop.Dispose();
        return ValueTask.CompletedTask;
    }
}
internal sealed class DemoAsrProvider : IAsrProvider
{
    public IAsrSession CreateSession(AsrConfiguration configuration) => new Session();
    sealed class Session : IAsrSession
    {
        readonly Channel<AsrResult> events = Channel.CreateUnbounded<AsrResult>(); int count;
        static readonly string[] samples = ["Live subtitles", "Live subtitles show the original", "Live subtitles show the original immediately.", "Translations", "Translations follow", "Translations follow as the sentence develops.", "Corrections remain", "Corrections remain aligned with", "Corrections remain aligned with the latest text."];
        public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token)
        {
            int n = count++;
            if (n % 5 == 0 && n / 5 < samples.Length)
            {
                int index = n / 5;
                events.Writer.TryWrite(new((index / 3).ToString(), index % 3 + 1, samples[index], index % 3 == 2, TimeSpan.FromSeconds(index / 3 * 3), TimeSpan.FromSeconds(n * .2), "en"));
            }
            return Task.CompletedTask;
        }
        public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token) => events.Reader.ReadAllAsync(token);
        public Task CompleteAsync(CancellationToken token)
        {
            events.Writer.TryComplete();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
internal sealed class DemoTranslationProvider : ITranslationProvider
{
    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken token)
    {
        await Task.Delay(180, token);
        return request.Text.StartsWith("Live") ? "实时字幕立即显示原文。" : request.Text.StartsWith("Translations") ? "译文随着句子展开及时更新。" : "修正始终与最新文本对齐。";
    }
}
