using AntiDbt.Core;
using System.Diagnostics;
using System.Text.Json;

var results = new List<object>(); var failures = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
void Test(string name, Action test) { var sw = Stopwatch.StartNew(); try { test(); results.Add(new { name, passed = true, ms = sw.Elapsed.TotalMilliseconds }); } catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.Message }); } }
AsrResult Speech(string text, int revision = 1, string id = "a", bool final = false, int start = 0) => new(id, revision, text, final, TimeSpan.FromMilliseconds(start), TimeSpan.FromMilliseconds(start + 200), "en");

Test("source appears before translation", () => { var s = new SubtitleStore(); var session = s.BeginSession(); s.ApplySource(session, Speech("Hello")); Check(s.Snapshot().Single().SourceText == "Hello", "source missing"); Check(SubtitleProjection.Compact(s.Snapshot()).Source == "Hello", "pending translation hid source"); });
Test("revision replaces same source segment", () => { var s = new SubtitleStore(); var n = s.BeginSession(); s.ApplySource(n, Speech("Hel")); s.ApplySource(n, Speech("Hello", 2)); Check(s.Snapshot().Count == 1 && s.Snapshot()[0].SourceText == "Hello", "revision appended instead of replacing"); });
Test("stale source rejected", () => { var s = new SubtitleStore(); var n = s.BeginSession(); s.ApplySource(n, Speech("new", 2)); Check(s.ApplySource(n, Speech("old", 1)) is null, "older revision accepted"); Check(s.Snapshot()[0].SourceText == "new", "source regressed"); });
Test("stale translation rejected after correction", () => { var s = new SubtitleStore(); var n = s.BeginSession(); s.ApplySource(n, Speech("one")); s.ApplySource(n, Speech("two", 2)); Check(!s.ApplyTranslation(n, "a", 1, "一"), "old translation accepted"); Check(s.ApplyTranslation(n, "a", 2, "二"), "latest translation rejected"); });
Test("source correction clears obsolete translation", () => { var s = new SubtitleStore(); var n = s.BeginSession(); s.ApplySource(n, Speech("one")); s.ApplyTranslation(n, "a", 1, "一"); s.ApplySource(n, Speech("two", 2)); Check(s.Snapshot()[0].TranslationText == "", "obsolete translation still displayed as current"); });
Test("previous session callbacks rejected", () => { var s = new SubtitleStore(); var a = s.BeginSession(); s.ApplySource(a, Speech("old")); var b = s.BeginSession(); Check(s.ApplySource(a, Speech("stale", 2)) is null, "previous source session accepted"); Check(!s.ApplyTranslation(a, "a", 1, "旧"), "previous translation accepted"); Check(s.Snapshot().Count == 0 && b != a, "session reset failed"); });
Test("reconnect isolation preserves history", () => { var s = new SubtitleStore(); var a = s.BeginSession(); s.ApplySource(a, Speech("old")); var b = s.BeginSession(false); s.ApplySource(b, Speech("new")); Check(s.Snapshot().Count == 2, "reconnect segment collided"); Check(!s.ApplyTranslation(a, "a", 1, "迟到"), "old connection translation accepted"); });
Test("bounded history drops oldest", () => { var s = new SubtitleStore(2); var n = s.BeginSession(); s.ApplySource(n, Speech("one", id:"1")); s.ApplySource(n, Speech("two", id:"2")); s.ApplySource(n, Speech("three", id:"3")); Check(s.Snapshot().Select(x => x.SourceText).SequenceEqual(new[]{"two","three"}), "history not bounded"); });
Test("duplicate events do not bump timestamps", () => { var s = new SubtitleStore(); var n = s.BeginSession(); s.ApplySource(n, Speech("hello")); Check(s.ApplySource(n, Speech("hello")) is null, "duplicate update delivered"); });
Test("final can upgrade equal revision", () => { var s = new SubtitleStore(); var n = s.BeginSession(); s.ApplySource(n, Speech("hello")); s.ApplySource(n, Speech("hello", final:true)); Check(s.Snapshot()[0].IsFinal, "final upgrade lost"); });
Test("empty correction removes stale display", () => { var s = new SubtitleStore(); var n = s.BeginSession(); s.ApplySource(n, Speech("hallucination")); s.ApplySource(n, Speech("", 2)); Check(SubtitleProjection.Compact(s.Snapshot()).Source == "", "empty correction ignored"); });
Test("expanded punctuation groups aligned tracks", () => { var s = new SubtitleStore(); var n = s.BeginSession(); s.ApplySource(n, Speech("你好。", id:"1", start: 0)); s.ApplyTranslation(n,"1",1,"Hello."); s.ApplySource(n, Speech("再见", id:"2", start: 3000)); var p = SubtitleProjection.Expanded(s.Snapshot()); Check(p.Count == 2 && p[0].Translation == "Hello." && p[1].Source == "再见", "grouping misaligned"); });
Test("pending history remains visible", () => { var s = new SubtitleStore(); var n = s.BeginSession(); s.ApplySource(n, Speech("pending")); Check(SubtitleProjection.Expanded(s.Snapshot()).Single().Source == "pending", "untranslated history hidden"); });
Test("latency rejects wrong clock ordering", () => { var stats = new LatencyMonitor(); stats.Record(TimeSpan.FromMilliseconds(-10)); stats.Record(TimeSpan.FromMilliseconds(20)); stats.Record(TimeSpan.FromMilliseconds(40)); Check(stats.Snapshot().Count == 2 && stats.Snapshot().MedianMilliseconds == 30, "latency aggregation incorrect"); });
Test("audio queue drops oldest under backpressure", () => { var q = new BoundedAudioQueue(2); for(int i=0;i<3;i++)q.Enqueue(new AudioChunk(new[]{(byte)i},TimeSpan.Zero,TimeSpan.Zero,0)); Check(q.Count == 2 && q.DroppedChunks == 1, "audio queue unbounded"); Check(q.TryDequeue(out var first) && first!.Pcm[0] == 1, "oldest not discarded"); });

async Task TestAsync(string name, Func<Task> test) { try { await test(); results.Add(new { name, passed = true }); } catch(Exception e) { failures++; results.Add(new { name, passed = false, error = e.Message }); } }
await TestAsync("scheduler discards late uncancellable translation", async () => {
    var s = new SubtitleStore(); var n = s.BeginSession(); var provider = new ControlledTranslator();
    await using var scheduler = new TranslationScheduler(s, provider, new() { DebounceMilliseconds = 0 }, "zh");
    var first = s.ApplySource(n, Speech("old",final:true))!; scheduler.Schedule(first);
    await provider.WaitForCountAsync(1);
    var second = s.ApplySource(n, Speech("latest",2,final:true))!; scheduler.Schedule(second);
    provider.Complete(0,"过期");await provider.WaitForCountAsync(2);provider.Complete(1,"最新");
    await scheduler.FlushAsync(CancellationToken.None);
    Check(s.Snapshot()[0].TranslationText == "最新", "late stale result overwrote latest");
});
await TestAsync("scheduler cancellation isolates new session", async () => {
    var s = new SubtitleStore(); var n = s.BeginSession(); var provider = new ControlledTranslator();
    await using var scheduler = new TranslationScheduler(s, provider, new() { DebounceMilliseconds = 0 }, "zh");
    scheduler.Schedule(s.ApplySource(n, Speech("old",final:true))!); await provider.WaitForCountAsync(1);
    scheduler.CancelPending(); s.BeginSession(); provider.Complete(0,"旧"); await scheduler.FlushAsync(CancellationToken.None);
    Check(s.Snapshot().Count == 0, "cancelled translation entered new session");
});
await TestAsync("scheduler de-duplicates completed identical translation", async () => {
    var s = new SubtitleStore(); var n=s.BeginSession(); var provider=new CountingTranslator();
    await using var scheduler=new TranslationScheduler(s,provider,new(){DebounceMilliseconds=0,ContextSentences=0},"zh");
    scheduler.Schedule(s.ApplySource(n,Speech("hello",id:"1",final:true))!); await scheduler.FlushAsync(CancellationToken.None);
    scheduler.Schedule(s.ApplySource(n,Speech("hello",id:"2",final:true))!); await scheduler.FlushAsync(CancellationToken.None);
    Check(provider.Calls == 1 && s.Snapshot()[1].TranslationText == "译:hello", "cache failed");
});

await TestAsync("pipeline drains finite audio and releases resources", async () => {
    var source=new TestAudioSource();var asr=new TestAsrProvider();
    await using var pipeline=new CaptionPipeline(source,asr,new CountingTranslator(),new(){Translation=new(){DebounceMilliseconds=0}});
    source.Push();source.Push();source.Finish();await pipeline.StartAsync();await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    Check(pipeline.Store.Snapshot().Single().SourceText=="word2", "pipeline did not propagate ASR");
    Check(pipeline.Store.Snapshot().Single().TranslationText=="译:word2", "pipeline translation missing");
    Check(source.Stopped && asr.Last!.Finished, "audio or ASR not finalized");
});
await TestAsync("pipeline stop while no audio is bounded", async () => {
    var source=new TestAudioSource();await using var pipeline=new CaptionPipeline(source,new TestAsrProvider(),new CountingTranslator(),new());
    await pipeline.StartAsync();await pipeline.StopAsync().WaitAsync(TimeSpan.FromSeconds(4));Check(source.Stopped,"capture not stopped");
});

await TestAsync("continuous revisions still start incremental translations", async()=>{
    var s=new SubtitleStore();var n=s.BeginSession();var p=new CountingTranslator();
    await using var scheduler=new TranslationScheduler(s,p,new(){DebounceMilliseconds=100},"zh");
    for(int i=1;i<=20;i++){scheduler.Schedule(s.ApplySource(n,Speech("changing"+i,i))!);await Task.Delay(25);}
    Check(p.Calls>=2,"continuous revisions starved translation");await scheduler.FlushAsync(CancellationToken.None);
});
await TestAsync("cancelled uncooperative jobs cannot hold new generation slots",async()=>{
    var s=new SubtitleStore();var n=s.BeginSession();var p=new ControlledTranslator();
    await using var scheduler=new TranslationScheduler(s,p,new(){DebounceMilliseconds=0},"zh");
    scheduler.Schedule(s.ApplySource(n,Speech("stuck1",id:"1",final:true))!);scheduler.Schedule(s.ApplySource(n,Speech("stuck2",id:"2",final:true))!);await p.WaitForCountAsync(2);
    scheduler.CancelPending();n=s.BeginSession();scheduler.Schedule(s.ApplySource(n,Speech("new",id:"3",final:true))!);
    try{await p.WaitForCountAsync(3);p.Complete(2,"新");}finally{p.Complete(0,"迟到");p.Complete(1,"迟到");}
    await scheduler.FlushAsync(CancellationToken.None);Check(s.Snapshot().Single().TranslationText=="新","new generation blocked");
});
Test("compact includes all fragments of current sentence",()=>{
    var s=new SubtitleStore();var n=s.BeginSession();s.ApplySource(n,Speech("Previous.",id:"1"));s.ApplySource(n,Speech("Current",id:"2"));s.ApplySource(n,Speech("sentence.",id:"3"));
    Check(SubtitleProjection.Compact(s.Snapshot()).Source=="Current sentence.","compact lost prefix or included previous sentence");
});
await TestAsync("failed old revision cannot drop queued final translation",async()=>{
    var s=new SubtitleStore();var n=s.BeginSession();var p=new ControlledTranslator();await using var scheduler=new TranslationScheduler(s,p,new(){DebounceMilliseconds=0},"zh");
    scheduler.Schedule(s.ApplySource(n,Speech("partial"))!);await p.WaitForCountAsync(1);scheduler.Schedule(s.ApplySource(n,Speech("final",2,final:true))!);p.Fail(0);
    await p.WaitForCountAsync(2);p.Complete(1,"完成");await scheduler.FlushAsync(CancellationToken.None);Check(s.Snapshot().Single().TranslationText=="完成","queued final dropped");
});
Test("provisional draft never replaces exact translation",()=>{
    var s=new SubtitleStore();var n=s.BeginSession();s.ApplySource(n,Speech("first"));s.ApplySource(n,Speech("latest",2));
    Check(s.ApplyProvisionalTranslation(n,"a",1,"草稿"),"draft not available");Check(s.Snapshot()[0].TranslationText==""&&SubtitleProjection.Compact(s.Snapshot()).TranslationPending,"draft mistaken for exact text");
    s.ApplyTranslation(n,"a",2,"最新");Check(!s.ApplyProvisionalTranslation(n,"a",1,"过期"),"older draft overwrote latest");
});
Test("deleted provisional segment cannot accept late translation",()=>{
    var s=new SubtitleStore();var n=s.BeginSession();s.ApplySource(n,Speech("obsolete"));s.ApplySource(n,Speech("",2) with{IsDeleted=true});
    Check(s.Snapshot().Count==0&&!s.ApplyTranslation(n,"a",1,"旧"),"deleted row reappeared");
    Check(s.ApplySource(n,Speech("stale",1)) is null&&s.Snapshot().Count==0,"old source recreated deleted row");
});
Test("continuous speech joins into single paragraph without newline", () => {
    var s = new SubtitleStore();
    var n = s.BeginSession();
    s.ApplySource(n, Speech("Hello, how are you?", id: "1", start: 0));
    s.ApplyTranslation(n, "1", 1, "你好，你好吗？");
    s.ApplySource(n, Speech("I'm fine, thank you.", id: "2", start: 800));
    s.ApplyTranslation(n, "2", 1, "我很好，谢谢你。");
    var p = SubtitleProjection.Expanded(s.Snapshot());
    Check(p.Count == 1, "continuous speech was split into multiple paragraphs");
    Check(p[0].Source == "Hello, how are you? I'm fine, thank you.", "English speech not joined with proper space");
    Check(p[0].Translation == "你好，你好吗？我很好，谢谢你。", "Chinese translation has unwanted space or missing content");
    Check(!p[0].Source.Contains('\n') && !p[0].Translation.Contains('\n'), "forced newline inserted");
});
Test("significant pause separates paragraphs", () => {
    var s = new SubtitleStore();
    var n = s.BeginSession();
    s.ApplySource(n, Speech("First sentence.", id: "1", start: 0));
    s.ApplyTranslation(n, "1", 1, "第一句。");
    s.ApplySource(n, Speech("Second sentence after pause.", id: "2", start: 4000));
    s.ApplyTranslation(n, "2", 1, "暂停后的第二句。");
    var p = SubtitleProjection.Expanded(s.Snapshot());
    Check(p.Count == 2, "pause of 4s should start a new paragraph");
    Check(p[0].Source == "First sentence." && p[1].Source == "Second sentence after pause.", "paragraph split contents incorrect");
});
await TestAsync("pipeline reconnect resumes bounded buffered audio",async()=>{
    var audio=new TestAudioSource();var asr=new ReconnectingAsrProvider();await using var p=new CaptionPipeline(audio,asr,new CountingTranslator(),new(){Translation=new(){DebounceMilliseconds=0}});
    await p.StartAsync();audio.Push();audio.Push();audio.Push();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(4));while(asr.Connections<2||!p.Store.Snapshot().Any(x=>x.SourceText=="recovered"))await Task.Delay(10,deadline.Token);
    audio.Finish();await p.Completion.WaitAsync(TimeSpan.FromSeconds(4));Check(asr.Connections==2&&asr.Disposals==2,"connection cleanup failed");Check(p.Store.Snapshot().Last().TranslationText=="译:recovered","recovered translation absent");
});
await TestAsync("ordinary stop preserves delayed final within drain budget",async()=>{
    var audio=new TestAudioSource();await using var p=new CaptionPipeline(audio,new DelayedFinalAsr(),new CountingTranslator(),new(){Translation=new(){DebounceMilliseconds=0}});
    await p.StartAsync();audio.Push();await Task.Delay(50);await p.StopAsync().WaitAsync(TimeSpan.FromSeconds(7));
    Check(p.Store.Snapshot().Single().SourceText=="delayed final"&&p.Store.Snapshot().Single().TranslationText=="译:delayed final","stop truncated final");
});
await TestAsync("pipeline resource disposal can retry after failure",async()=>{
    var source=new DisposalAudio(true);var p=new CaptionPipeline(source,new TestAsrProvider(),new CountingTranslator(),new());await p.StartAsync();
    try{await p.DisposeAsync();throw new Exception("expected failure");}catch(IOException){}
    await p.DisposeAsync();await p.DisposeAsync();Check(source.Disposals==2,"cleanup retry or idempotence failed");
});
await TestAsync("concurrent pipeline disposals share resource cleanup",async()=>{
    var source=new DisposalAudio();var p=new CaptionPipeline(source,new TestAsrProvider(),new CountingTranslator(),new());await p.StartAsync();
    await Task.WhenAll(p.DisposeAsync().AsTask(),p.DisposeAsync().AsTask());Check(source.Disposals==1,"concurrent cleanup executed twice");
});
Test("early tombstone blocks delayed source creation",()=>{
    var s=new SubtitleStore();var n=s.BeginSession();s.ApplySource(n,Speech("",2) with{IsDeleted=true});Check(s.ApplySource(n,Speech("late",1)) is null,"early deletion lost");
});
await TestAsync("zero audio suppression threshold and nonzero recovery",async()=>{
    var source=new TestAudioSource();var asr=new TestAsrProvider();await using var p=new CaptionPipeline(source,asr,new CountingTranslator(),new(){SilenceSeconds=1,Translation=new(){DebounceMilliseconds=0}});
    for(int i=0;i<6;i++)source.PushSilence();source.Push();source.Finish();await p.StartAsync();await p.Completion.WaitAsync(TimeSpan.FromSeconds(4));
    Check(p.Store.Snapshot().Single().SourceText=="word6","zero threshold or nonzero resume incorrect");
});

await TestAsync("Final preempts uncooperative preview before release",async()=>{
    var s=new SubtitleStore();var n=s.BeginSession();var p=new ControlledTranslator();
    await using var scheduler=new TranslationScheduler(s,p,new(){DebounceMilliseconds=0},"zh");
    scheduler.Schedule(s.ApplySource(n,Speech("partial"))!);await p.WaitForCountAsync(1);
    scheduler.Schedule(s.ApplySource(n,Speech("final",2,final:true))!);
    try{await p.WaitForCountAsync(2);p.Complete(1,"最终");await scheduler.FlushAsync(CancellationToken.None);Check(s.Snapshot().Single().TranslationText=="最终","Final missing");}
    finally{p.Complete(0,"obsolete");}
});
await TestAsync("Final wakes fixed debounce",async()=>{
    var s=new SubtitleStore();var n=s.BeginSession();var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var p=new BarrierTranslator((r,t)=>{started.TrySetResult();return Task.FromResult("final");});
    await using var scheduler=new TranslationScheduler(s,p,new(){DebounceMilliseconds=2000},"zh");
    scheduler.Schedule(s.ApplySource(n,Speech("partial"))!);await Task.Delay(100);
    scheduler.Schedule(s.ApplySource(n,Speech("final",2,final:true))!);
    await started.Task.WaitAsync(TimeSpan.FromMilliseconds(600));await scheduler.FlushAsync(CancellationToken.None);
});
await TestAsync("queued Final gets released slot before queued previews",async()=>{
    var s=new SubtitleStore();var n=s.BeginSession();var entered=new List<string>();
    var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var next=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var release=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var other=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var p=new BarrierTranslator((r,t)=>{lock(entered){entered.Add(r.SegmentId);if(entered.Count==2)started.TrySetResult();if(entered.Count==3)next.TrySetResult(r.SegmentId);}return r.SegmentId=="1"?release.Task:r.SegmentId=="2"?other.Task:Task.FromResult("ok");});
    await using var scheduler=new TranslationScheduler(s,p,new(){DebounceMilliseconds=0},"zh");
    scheduler.Schedule(s.ApplySource(n,Speech("block1",id:"1",final:true))!);scheduler.Schedule(s.ApplySource(n,Speech("block2",id:"2",final:true))!);await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    scheduler.Schedule(s.ApplySource(n,Speech("preview",id:"3"))!);await Task.Delay(100);
    scheduler.Schedule(s.ApplySource(n,Speech("final",id:"4",final:true))!);await Task.Delay(100);release.TrySetResult("ok");
    try{Check(await next.Task.WaitAsync(TimeSpan.FromSeconds(2))=="4","preview took Final slot");}finally{other.TrySetResult("ok");}
    await scheduler.FlushAsync(CancellationToken.None);
});

await TestAsync("same revision Final upgrade preempts preview",async()=>{
    var s=new SubtitleStore();var n=s.BeginSession();var p=new ControlledTranslator();
    await using var scheduler=new TranslationScheduler(s,p,new(){DebounceMilliseconds=0},"zh");
    scheduler.Schedule(s.ApplySource(n,Speech("same text"))!);await p.WaitForCountAsync(1);
    scheduler.Schedule(s.ApplySource(n,Speech("same text",final:true))!);
    try{await p.WaitForCountAsync(2);p.Complete(1,"final");await scheduler.FlushAsync(CancellationToken.None);Check(s.Snapshot().Single().TranslationText=="final","same revision Final dropped");}
    finally{p.Complete(0,"obsolete");}
});
Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/core-tests.json", JsonSerializer.Serialize(new { total = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { total = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;
