using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using AntiDbt.Core;
internal static class Soak
{
    public static async Task<int> RunAsync()
    {
        var asr=new Speech();var mt=new Translator();await using var pipeline=new CaptionPipeline(new Audio(),asr,mt,new(){Translation=new(){DebounceMilliseconds=80},MaxHistory=100});
        var started=Stopwatch.StartNew();var before=GC.GetTotalMemory(true);await pipeline.StartAsync();await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(70));var retained=GC.GetTotalMemory(true)-before;
        var rows=pipeline.Store.Snapshot();var pass=asr.Connections>=3&&rows.Count>0&&rows.Count<=100&&rows.Any(x=>x.TranslationText.Length>0)&&pipeline.Completion.IsCompleted;
        var report=new{pass,kind="60-second synthetic pipeline with reconnects, translation jitter and failures; no cloud/hardware",elapsedSeconds=started.Elapsed.TotalSeconds,connections=asr.Connections,translationCalls=mt.Calls,rows=rows.Count,translated=rows.Count(x=>x.TranslationText.Length>0),retainedManagedBytesDelta=retained};
        await File.WriteAllTextAsync("artifacts/soak.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));return pass?0:1;
    }
    sealed class Audio : IAudioSource
    {
        readonly CancellationTokenSource stop=new();public string Name=>"synthetic soak";
        public Task StartAsync(CancellationToken token)=>Task.CompletedTask;
        public async IAsyncEnumerable<AudioChunk> ReadChunksAsync([EnumeratorCancellation]CancellationToken token)
        {
            using var combined=CancellationTokenSource.CreateLinkedTokenSource(token,stop.Token);
            for(int i=0;i<300&&!combined.IsCancellationRequested;i++)
            {var bytes=new byte[6400];bytes[0]=1;yield return new(bytes,TimeSpan.FromMilliseconds(i*200),TimeSpan.FromMilliseconds(200),Stopwatch.GetTimestamp());await Task.Delay(200,combined.Token);}
        }
        public Task StopAsync(CancellationToken token){stop.Cancel();return Task.CompletedTask;}
        public ValueTask DisposeAsync(){stop.Dispose();return ValueTask.CompletedTask;}
    }
    sealed class Speech : IAsrProvider
    {
        public int Connections;
        public IAsrSession CreateSession(AsrConfiguration config)=>new Session(Interlocked.Increment(ref Connections)<3);
        sealed class Session(bool drop) : IAsrSession
        {
            int count;readonly Channel<AsrResult> events=Channel.CreateUnbounded<AsrResult>();
            public Task ConnectAsync(CancellationToken token)=>Task.CompletedTask;
            public Task SendAudioAsync(ReadOnlyMemory<byte> pcm,CancellationToken token)
            {count++;if(drop&&count==40)throw new IOException("local reconnect");events.Writer.TryWrite(new((count/15).ToString(),count%15+1,"Synthetic sentence "+count,count%15==14,TimeSpan.FromMilliseconds(count/15*3000),TimeSpan.FromMilliseconds(count*200),"en"));return Task.CompletedTask;}
            public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token)=>events.Reader.ReadAllAsync(token);
            public Task CompleteAsync(CancellationToken token){events.Writer.TryComplete();return Task.CompletedTask;}
            public ValueTask DisposeAsync(){events.Writer.TryComplete();return ValueTask.CompletedTask;}
        }
    }
    sealed class Translator : ITranslationProvider
    {
        public int Calls;
        public async Task<string> TranslateAsync(TranslationRequest request,CancellationToken token)
        {int number=Interlocked.Increment(ref Calls);await Task.Delay(100+number%4*80,token);if(number%11==0)throw new IOException("local translation error");return "合成译文 "+request.Text;}
    }
}
