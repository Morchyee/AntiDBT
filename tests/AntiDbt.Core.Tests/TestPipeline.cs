using AntiDbt.Core;
using System.Diagnostics;
using System.Threading.Channels;
sealed class TestAudioSource : IAudioSource
{
    readonly Channel<AudioChunk> chunks=Channel.CreateUnbounded<AudioChunk>();public string Name=>"synthetic";public bool Stopped;
    public void Push()=>chunks.Writer.TryWrite(new(new byte[]{1,0},TimeSpan.Zero,TimeSpan.FromMilliseconds(200),Stopwatch.GetTimestamp()));
    public void PushSilence()=>chunks.Writer.TryWrite(new(new byte[]{0,0},TimeSpan.Zero,TimeSpan.FromMilliseconds(200),Stopwatch.GetTimestamp()));
    public void Finish()=>chunks.Writer.TryComplete();
    public Task StartAsync(CancellationToken token)=>Task.CompletedTask;
    public IAsyncEnumerable<AudioChunk> ReadChunksAsync(CancellationToken token)=>chunks.Reader.ReadAllAsync(token);
    public Task StopAsync(CancellationToken token){Stopped=true;Finish();return Task.CompletedTask;}
    public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
}
sealed class TestAsrProvider : IAsrProvider
{
    public TestAsrSession? Last;
    public IAsrSession CreateSession(AsrConfiguration config)=>Last=new();
}
sealed class TestAsrSession : IAsrSession
{
    readonly Channel<AsrResult> results=Channel.CreateUnbounded<AsrResult>();int count;public bool Finished;
    public Task ConnectAsync(CancellationToken token)=>Task.CompletedTask;
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm,CancellationToken token){count++;results.Writer.TryWrite(new("a",count,"word"+count,false,TimeSpan.Zero,TimeSpan.FromMilliseconds(count*200),"en"));return Task.CompletedTask;}
    public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token)=>results.Reader.ReadAllAsync(token);
    public Task CompleteAsync(CancellationToken token){Finished=true;results.Writer.TryWrite(new("a",count+1,"word"+count,true,TimeSpan.Zero,TimeSpan.FromMilliseconds(count*200),"en"));results.Writer.TryComplete();return Task.CompletedTask;}
    public ValueTask DisposeAsync(){results.Writer.TryComplete();return ValueTask.CompletedTask;}
}
sealed class ReconnectingAsrProvider : IAsrProvider
{
    public int Connections;public int Disposals;
    public IAsrSession CreateSession(AsrConfiguration configuration)=>new ReconnectSession(this,Interlocked.Increment(ref Connections)==1);
    sealed class ReconnectSession(ReconnectingAsrProvider owner,bool fail) : IAsrSession
    {
        readonly Channel<AsrResult> events=Channel.CreateUnbounded<AsrResult>();
        public Task ConnectAsync(CancellationToken token)=>Task.CompletedTask;
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm,CancellationToken token)
        {if(fail)throw new IOException("local disconnect");events.Writer.TryWrite(new("new",1,"recovered",true,TimeSpan.Zero,TimeSpan.FromMilliseconds(200),"en"));return Task.CompletedTask;}
        public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token)=>events.Reader.ReadAllAsync(token);
        public Task CompleteAsync(CancellationToken token){events.Writer.TryComplete();return Task.CompletedTask;}
        public ValueTask DisposeAsync(){owner.Disposals++;events.Writer.TryComplete();return ValueTask.CompletedTask;}
    }
}
sealed class DelayedFinalAsr : IAsrProvider
{
    public IAsrSession CreateSession(AsrConfiguration config)=>new Session();
    sealed class Session : IAsrSession
    {
        readonly Channel<AsrResult> events=Channel.CreateUnbounded<AsrResult>();
        public Task ConnectAsync(CancellationToken token)=>Task.CompletedTask;
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm,CancellationToken token)=>Task.CompletedTask;
        public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token)=>events.Reader.ReadAllAsync(token);
        public async Task CompleteAsync(CancellationToken token){await Task.Delay(3300,token);events.Writer.TryWrite(new("final",1,"delayed final",true,TimeSpan.Zero,TimeSpan.FromMilliseconds(200),"en"));events.Writer.TryComplete();}
        public ValueTask DisposeAsync(){events.Writer.TryComplete();return ValueTask.CompletedTask;}
    }
}
sealed class DisposalAudio(bool failFirst=false) : IAudioSource
{
    readonly TestAudioSource inner=new();public int Disposals;public string Name=>"disposal fixture";
    public Task StartAsync(CancellationToken token)=>inner.StartAsync(token);
    public IAsyncEnumerable<AudioChunk> ReadChunksAsync(CancellationToken token)=>inner.ReadChunksAsync(token);
    public Task StopAsync(CancellationToken token)=>inner.StopAsync(token);
    public async ValueTask DisposeAsync(){Disposals++;await Task.Delay(30);if(failFirst&&Disposals==1)throw new IOException("first disposal failure");}
}

sealed class FlakySendFailReadAsr : IAsrProvider
{
    public int SessionsCreated;
    public int SendCalls;
    public IAsrSession CreateSession(AsrConfiguration config) => new Session(this);
    sealed class Session(FlakySendFailReadAsr owner) : IAsrSession
    {
        public Task ConnectAsync(CancellationToken token)
        {
            owner.SessionsCreated++;
            return Task.CompletedTask;
        }
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token)
        {
            owner.SendCalls++;
            return Task.CompletedTask;
        }
        public async IAsyncEnumerable<AsrResult> GetResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            await Task.Delay(5, token);
            throw new IOException("read stream broken immediately after send");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

sealed class FatalErrorAsr(ProviderErrorKind kind) : IAsrProvider
{
    public ProviderErrorKind Kind { get; } = kind;
    public int ConnectCalls;
    public IAsrSession CreateSession(AsrConfiguration config) => new Session(this);
    sealed class Session(FatalErrorAsr owner) : IAsrSession
    {
        public Task ConnectAsync(CancellationToken token)
        {
            owner.ConnectCalls++;
            throw new ProviderException(owner.Kind, "fatal error test");
        }
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) => Task.CompletedTask;
        public async IAsyncEnumerable<AsrResult> GetResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

sealed class UncooperativeSendAsr : IAsrProvider, IAsrSession
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Disposals;
    public IAsrSession CreateSession(AsrConfiguration config) => this;
    public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) { Entered.TrySetResult(); return Release.Task; }
    public async IAsyncEnumerable<AsrResult> GetResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token) { await Task.Delay(Timeout.Infinite, token); yield break; }
    public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
}

sealed class UncooperativeConnectAsr : IAsrProvider, IAsrSession
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Disposals;
    public IAsrSession CreateSession(AsrConfiguration config) => this;
    public Task ConnectAsync(CancellationToken token) { Entered.TrySetResult(); return Release.Task; }
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) => Task.CompletedTask;
    public async IAsyncEnumerable<AsrResult> GetResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token) { await Task.Delay(Timeout.Infinite, token); yield break; }
    public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
}

sealed class FatalReadAsr : IAsrProvider
{
    public int Created;
    public IAsrSession CreateSession(AsrConfiguration config) { Created++; return new Session(); }
    sealed class Session : IAsrSession
    {
        readonly TaskCompletionSource sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
        public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken token) { sent.TrySetResult(); await Task.Delay(1000, token); }
        public async IAsyncEnumerable<AsrResult> GetResultsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            await sent.Task.WaitAsync(token);
            await Task.Delay(10, token);
            throw new ProviderException(ProviderErrorKind.Authentication, "synthetic permanent authentication failure");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        public Task CompleteAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

