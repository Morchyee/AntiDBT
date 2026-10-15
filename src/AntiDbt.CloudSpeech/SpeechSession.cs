using AntiDbt.Core;
using System.Threading.Channels;
namespace AntiDbt.CloudSpeech;

public sealed record SpeechEvent(string Text, bool Final, TimeSpan Start, TimeSpan End, string? Language = null);
public interface ISpeechTransport : IAsyncDisposable
{
    event Action<SpeechEvent>? Result;
    event Action<ProviderErrorKind>? Failed;
    event Action? Stopped;
    Task StartAsync(CancellationToken token);
    Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken token);
    Task CompleteAsync(CancellationToken token);
    // Cancels actual SDK I/O before a waiter releases the single-writer gate.
    void Abort() { }
}
internal sealed class SpeechSession(Func<ISpeechTransport> factory, TimeSpan? limit = null) : IAsrSession
{
    readonly Channel<AsrResult> results = Channel.CreateBounded<AsrResult>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait });
    readonly CancellationTokenSource lifetime = new();
    readonly CancellationTokenSource timerStop = new();
    readonly SemaphoreSlim writes = new(1);
    readonly object sync = new();
    ISpeechTransport? transport;
    Task? cleanup;
    int utterance, revision, disposed, ended, inputClosed, connected;
    public async Task ConnectAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (Interlocked.Exchange(ref connected, 1) != 0) throw new InvalidOperationException("Session already connected.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try {
            transport = factory();
            transport.Result += OnResult; transport.Failed += Fail; transport.Stopped += Stop;
            // Start before SDK stream creation, conservatively including connection setup.
            if (limit is { } duration) _ = ExpireAsync(duration);
            await transport.StartAsync(deadline.Token).WaitAsync(deadline.Token);
        } catch (Exception e) {
            var kind = e is ProviderException p ? p.Kind : e is OperationCanceledException ? ProviderErrorKind.Timeout : ProviderErrorKind.Service;
            Fail(kind); await CleanupAsync();
            if (e is OperationCanceledException && token.IsCancellationRequested) throw;
            throw SpeechErrors.Create(kind);
        }
    }
    async Task ExpireAsync(TimeSpan duration) {
        try { await Task.Delay(duration, timerStop.Token); Fail(ProviderErrorKind.Timeout); await CleanupAsync(); }
        catch (OperationCanceledException) { }
    }
    void OnResult(SpeechEvent value) {
        lock (sync) {
            if (disposed != 0 || ended != 0 || string.IsNullOrWhiteSpace(value.Text)) return;
            if (!results.Writer.TryWrite(new($"utterance-{utterance}", ++revision, value.Text, value.Final, value.Start, value.End, value.Language))) Fail(ProviderErrorKind.Service);
            if (value.Final) { utterance++; revision = 0; }
        }
    }
    void Fail(ProviderErrorKind kind) {
        if (Interlocked.Exchange(ref ended, 1) != 0) return;
        results.Writer.TryComplete(SpeechErrors.Create(kind));
        timerStop.Cancel();
        lifetime.Cancel();
        try { transport?.Abort(); } catch { }
        _ = CleanupAsync();
    }
    void Stop() { Interlocked.Exchange(ref ended, 1); results.Writer.TryComplete(); timerStop.Cancel(); }
    void EnsureWritable() {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (transport == null) throw new InvalidOperationException("Session not connected.");
        if (ended != 0 || inputClosed != 0) throw SpeechErrors.Create(ProviderErrorKind.Service);
    }
    public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) {
        token.ThrowIfCancellationRequested(); EnsureWritable();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await writes.WaitAsync(deadline.Token);
        try {
            EnsureWritable();
            await transport!.WriteAsync(pcm, deadline.Token).WaitAsync(deadline.Token);
        } catch (Exception e) {
            var kind = e is OperationCanceledException ? ProviderErrorKind.Timeout : ProviderErrorKind.Network;
            Fail(kind); await CleanupAsync();
            if (e is OperationCanceledException && token.IsCancellationRequested) throw;
            throw SpeechErrors.Create(kind);
        } finally { writes.Release(); }
    }
    public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token) => results.Reader.ReadAllAsync(token);
    public async Task CompleteAsync(CancellationToken token) {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (transport == null || Interlocked.Exchange(ref inputClosed, 1) != 0) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        await writes.WaitAsync(deadline.Token);
        try { await transport.CompleteAsync(deadline.Token).WaitAsync(deadline.Token); }
        catch (Exception e) {
            var kind = e is OperationCanceledException ? ProviderErrorKind.Timeout : ProviderErrorKind.Network;
            Fail(kind); await CleanupAsync();
            if (e is OperationCanceledException && token.IsCancellationRequested) throw;
            throw SpeechErrors.Create(kind);
        } finally { writes.Release(); }
    }
    Task CleanupAsync() {
        lock (sync) return cleanup ??= ReleaseAsync();
    }
    async Task ReleaseAsync() {
        await Task.Yield(); // Never stop/dispose a native recognizer from its event callback.
        if (transport == null) return;
        transport.Result -= OnResult; transport.Failed -= Fail; transport.Stopped -= Stop;
        try { await transport.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
    }
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); timerStop.Cancel(); results.Writer.TryComplete();
        try { transport?.Abort(); } catch { }
        await CleanupAsync();
        // CTS stays alive for SDK callbacks/operations that ignore cancellation.
    }
}
internal static class SpeechErrors {
    public static ProviderException Create(ProviderErrorKind kind) => new(kind, $"Speech {kind}: check service configuration and connection.");
}
internal static class SpeechValidation {
    public static string Language(AsrConfiguration config) {
        string language = string.IsNullOrWhiteSpace(config.Language) ? "en-US" : config.Language;
        if (!System.Text.RegularExpressions.Regex.IsMatch(language, "^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})+$")) throw Bad();
        return language;
    }
    public static ProviderException Bad() => SpeechErrors.Create(ProviderErrorKind.Configuration);
}

