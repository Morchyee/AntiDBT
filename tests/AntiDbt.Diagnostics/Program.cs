using System.Diagnostics;
using System.Text.Json;
using AntiDbt.Core;
using AntiDbt.Windows;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
Directory.CreateDirectory("artifacts");
if(args.Contains("--soak"))return await Soak.RunAsync();
if(args.Contains("--tone-capture"))
{
    // Explicit diagnostic: generate a quiet tone in memory, capture only counts/RMS.
    // Never persist captured PCM or send it to a provider.
    long bytes=0,nonzero=0;double squares=0;bool pass=false;string? error=null;
    try
    {
        await using var source=new WasapiAudioSource();using var output=new WasapiOut(AudioClientShareMode.Shared,100);
        var tone=new SignalGenerator(48000,2){Frequency=440,Gain=.02,Type=SignalGeneratorType.Sin};output.Init(tone.ToWaveProvider());
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(4));await source.StartAsync(cancel.Token);output.Play();
        try{await foreach(var chunk in source.ReadChunksAsync(cancel.Token)){bytes+=chunk.Pcm.Length;for(int i=0;i<chunk.Pcm.Length;i+=2){var sample=System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(chunk.Pcm.AsSpan(i));if(sample!=0)nonzero++;squares+=Math.Pow(sample/32768d,2);}}}catch(OperationCanceledException){}
        output.Stop();await source.StopAsync(default);pass=bytes>0&&nonzero>0;
    }
    catch(Exception e){error=e.GetType().Name;}
    var result=new{pass,bytes,nonzeroSamples=nonzero,rms=bytes>0?Math.Sqrt(squares/(bytes/2)):0,error,audioPersisted=false,audioTransmitted=false};
    await File.WriteAllTextAsync("artifacts/hardware-tone.json",JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));return pass?0:1;
}
var store=new SubtitleStore(2000);var generation=store.BeginSession();var durations=new List<double>();var start=Stopwatch.StartNew();var memoryBefore=GC.GetTotalMemory(true);
for(int i=0;i<40000;i++)
{
    var stamp=Stopwatch.GetTimestamp();var segment=(i/4).ToString();var rev=i%4+1;
    store.ApplySource(generation,new(segment,rev,"Synthetic subtitle revision "+i,i%4==3,TimeSpan.FromMilliseconds(i/4*800),TimeSpan.FromMilliseconds(i/4*800+200),"en"));
    store.ApplyTranslation(generation,segment,rev,"合成字幕修正 "+i);
    if(i%20==0)SubtitleProjection.Compact(store.Snapshot());
    durations.Add(Stopwatch.GetElapsedTime(stamp).TotalMilliseconds);
}
start.Stop();var memoryAfter=GC.GetTotalMemory(true);durations.Sort();
var performance=new{kind="synthetic Core processing; not cloud, capture or WPF frame latency",events=40000,rows=store.Snapshot().Count,elapsedMilliseconds=start.Elapsed.TotalMilliseconds,p50Milliseconds=durations[durations.Count/2],p95Milliseconds=durations[(int)(durations.Count*.95)],maxMilliseconds=durations[^1],retainedManagedBytesDelta=memoryAfter-memoryBefore,pass=store.Snapshot().Count==2000};
await File.WriteAllTextAsync("artifacts/performance.json",JsonSerializer.Serialize(performance,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(performance));return performance.pass?0:1;
