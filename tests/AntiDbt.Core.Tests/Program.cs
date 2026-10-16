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
await TestAsync("reconfigure stop immediately isolates session and skips delayed final",async()=>{
    var audio=new TestAudioSource();await using var p=new CaptionPipeline(audio,new DelayedFinalAsr(),new CountingTranslator(),new(){Translation=new(){DebounceMilliseconds=0}});
    await p.StartAsync();audio.Push();await Task.Delay(50);await p.StopAsync(StopReason.Reconfigure).WaitAsync(TimeSpan.FromSeconds(3));
    Check(p.Store.Snapshot().Count==0,"reconfigure stop leaked delayed final into store");
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

Test("Korean space preservation between words and no space in CJK Han", () => {
    var s = new SubtitleStore();
    var n = s.BeginSession();
    s.ApplySource(n, Speech("오늘은", id: "1", start: 0));
    s.ApplyTranslation(n, "1", 1, "오늘은");
    s.ApplySource(n, Speech("좋은 날입니다.", id: "2", start: 500));
    s.ApplyTranslation(n, "2", 1, "좋은 날입니다.");
    var p = SubtitleProjection.Expanded(s.Snapshot());
    Check(p.Count == 1, "continuous speech should form 1 paragraph");
    Check(p[0].Source == "오늘은 좋은 날입니다.", "Korean words should be separated by space");
    Check(p[0].Translation == "오늘은 좋은 날입니다.", "Korean translation words should be separated by space");

    var sHan = new SubtitleStore();
    var nHan = sHan.BeginSession();
    sHan.ApplySource(nHan, Speech("你好", id: "1", start: 0));
    sHan.ApplyTranslation(nHan, "1", 1, "你好");
    sHan.ApplySource(nHan, Speech("世界", id: "2", start: 500));
    sHan.ApplyTranslation(nHan, "2", 1, "世界");
    var pHan = SubtitleProjection.Expanded(sHan.Snapshot());
    Check(pHan[0].Source == "你好世界", "Han characters should not have space between them");
    Check(pHan[0].Translation == "你好世界", "Han translation characters should not have space between them");
});

Test("empty correction completely clears source and ghost translation in compact and expanded", () => {
    var s = new SubtitleStore();
    var n = s.BeginSession();
    s.ApplySource(n, Speech("Ghost audio", id: "1"));
    s.ApplyTranslation(n, "1", 1, "幽灵译文");
    s.ApplySource(n, Speech("", 2, id: "1"));
    var snap = s.Snapshot();
    var compact = SubtitleProjection.Compact(snap);
    Check(compact.Source == "", "source should be empty");
    Check(compact.Translation == "", "translation should be empty without ghost text");
    var expanded = SubtitleProjection.Expanded(snap);
    Check(expanded.Count == 0, "empty segment should not produce visible paragraph in expanded view");
});

Test("semantic revision mismatch clears provisional draft and rejects incompatible late draft", () => {
    var s = new SubtitleStore();
    var n = s.BeginSession();
    s.ApplySource(n, Speech("turn left", id: "1"));
    s.ApplyTranslation(n, "1", 1, "向左转");
    Check(s.Snapshot()[0].TranslationText == "向左转", "translation not applied");

    s.ApplySource(n, Speech("turn right", 2, id: "1"));
    Check(s.Snapshot()[0].TranslationText == "" && s.Snapshot()[0].ProvisionalTranslationText == "", "incompatible revision did not clear provisional draft");

    Check(!s.ApplyProvisionalTranslation(n, "1", 1, "turn left", "向左转"), "incompatible late draft was accepted");
    Check(s.Snapshot()[0].ProvisionalTranslationText == "", "provisional text contaminated by incompatible draft");

    s.ApplySource(n, Speech("turn left again", 3, id: "1"));
    s.ApplyTranslation(n, "1", 3, "再次向左转");
    s.ApplySource(n, Speech("turn left again and stop", 4, id: "1"));
    Check(s.Snapshot()[0].ProvisionalTranslationText == "再次向左转", "compatible prefix extension should preserve draft");
});

await TestAsync("translation failure sets error state and retry succeeds", async () => {
    var s = new SubtitleStore();
    var n = s.BeginSession();
    var p = new ControlledTranslator();
    await using var scheduler = new TranslationScheduler(s, p, new() { DebounceMilliseconds = 0 }, "zh");
    scheduler.Schedule(s.ApplySource(n, Speech("troubled sentence", id: "1", final: true))!);
    await p.WaitForCountAsync(1);
    p.Fail(0);
    await scheduler.FlushAsync(CancellationToken.None);

    var snap = s.Snapshot();
    Check(snap[0].TranslationError != null, "translation error was not recorded");
    var paragraphs = SubtitleProjection.Expanded(snap);
    Check(paragraphs.Count == 1 && paragraphs[0].HasError && paragraphs[0].CanRetry, "paragraph error or retry state missing");

    s.ClearTranslationError(n, "1", 1);
    Check(s.Snapshot()[0].TranslationError == null, "clear error failed");
    scheduler.Schedule(s.Snapshot()[0]);
    await p.WaitForCountAsync(2);
    p.Complete(1, "重试成功");
    await scheduler.FlushAsync(CancellationToken.None);
    Check(s.Snapshot()[0].TranslationText == "重试成功" && s.Snapshot()[0].TranslationError == null, "retry translation did not succeed");
});

await TestAsync("newer revision cancels and preempts translation retry", async () => {
    var s = new SubtitleStore();
    var n = s.BeginSession();
    var p = new ControlledTranslator();
    await using var scheduler = new TranslationScheduler(s, p, new() { DebounceMilliseconds = 0 }, "zh");
    scheduler.Schedule(s.ApplySource(n, Speech("first", id: "1", final: true))!);
    await p.WaitForCountAsync(1);
    p.Fail(0);
    await scheduler.FlushAsync(CancellationToken.None);

    Check(s.Snapshot()[0].TranslationError != null, "first attempt not failed");

    scheduler.Schedule(s.Snapshot()[0]);
    await p.WaitForCountAsync(2);

    scheduler.Schedule(s.ApplySource(n, Speech("second revision", 2, id: "1", final: true))!);
    await p.WaitForCountAsync(3);

    p.Complete(2, "第二版译文");
    p.Fail(1);
    await scheduler.FlushAsync(CancellationToken.None);

    Check(s.Snapshot()[0].TranslationText == "第二版译文", "newer revision did not prevail over retry");
});

Test("audio queue tracks duration, oldest age, and stats snapshot", () => {
    var q = new BoundedAudioQueue(10);
    long oldTs = Stopwatch.GetTimestamp() - (long)(1.5 * Stopwatch.Frequency);
    q.Enqueue(new AudioChunk(new byte[640], TimeSpan.Zero, TimeSpan.FromMilliseconds(20), oldTs));
    q.Enqueue(new AudioChunk(new byte[640], TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20), Stopwatch.GetTimestamp()));

    var stats = q.GetStats();
    Check(stats.Count == 2, "count mismatch");
    Check(stats.TotalDuration == TimeSpan.FromMilliseconds(40), "total duration mismatch");
    Check(stats.OldestChunkAge >= TimeSpan.FromMilliseconds(1400), "oldest age underreported");
    Check(stats.DroppedChunks == 0, "unexpected drops");

    Check(q.TryDequeue(out var first) && first != null, "dequeue failed");
    Check(q.TotalDuration == TimeSpan.FromMilliseconds(20), "duration after dequeue mismatch");
});

Test("audio queue TrimStale drops expired audio and increments discontinuity", () => {
    var q = new BoundedAudioQueue(10, TimeSpan.FromSeconds(2));
    long expiredTs = Stopwatch.GetTimestamp() - (long)(4.0 * Stopwatch.Frequency);
    long freshTs = Stopwatch.GetTimestamp();

    for (int i = 0; i < 5; i++)
        q.Enqueue(new AudioChunk(new byte[640], TimeSpan.FromMilliseconds(i * 20), TimeSpan.FromMilliseconds(20), expiredTs));
    q.Enqueue(new AudioChunk(new byte[640], TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), freshTs));

    Check(q.Count == 6, "initial count mismatch");
    int trimmed = q.TrimStale(TimeSpan.FromSeconds(2.0));
    Check(trimmed == 5, "trimmed count mismatch");
    Check(q.Count == 1, "remaining count mismatch");
    Check(q.DroppedChunks == 5, "dropped chunks mismatch");
    Check(q.Discontinuities == 1, "discontinuity count mismatch");
    Check(q.TryDequeue(out var remaining) && remaining!.CapturedTimestamp == freshTs, "fresh chunk lost");
});

await TestAsync("pipeline freshness policy bounds audio age during reconnection", async () => {
    var audio = new TestAudioSource();
    var asr = new ReconnectingAsrProvider();
    await using var p = new CaptionPipeline(audio, asr, new CountingTranslator(), new() { Translation = new() { DebounceMilliseconds = 0 } });
    await p.StartAsync();

    // Push 65 chunks into queue with capacity 50 while ASR is reconnecting
    for (int i = 0; i < 65; i++)
    {
        audio.Push();
    }
    audio.Finish();
    await p.Completion.WaitAsync(TimeSpan.FromSeconds(5));

    Check(p.AudioStats.DroppedChunks == 15, $"expected exactly 15 dropped chunks for 65 chunks in cap 50, got {p.AudioStats.DroppedChunks}");
    Check(p.AudioStats.Discontinuities >= 1, $"expected at least 1 discontinuity event, got {p.AudioStats.Discontinuities}");
});

await TestAsync("pipeline retry budget exhausted when send succeeds but read fails repeatedly", async () => {
    var audio = new TestAudioSource();
    var asr = new FlakySendFailReadAsr();
    await using var p = new CaptionPipeline(audio, asr, new CountingTranslator(), new() { Translation = new() { DebounceMilliseconds = 0 } });
    p.InitialRetryDelay = TimeSpan.FromMilliseconds(5);
    p.MaxRetryDelay = TimeSpan.FromMilliseconds(20);
    p.StableHealthyPeriod = TimeSpan.FromMilliseconds(500);
    PipelineState lastState = PipelineState.Stopped;
    p.StatusChanged += s => lastState = s.State;
    await p.StartAsync();

    using var pushCts = new CancellationTokenSource();
    var pushTask = Task.Run(async () =>
    {
        while (!pushCts.IsCancellationRequested)
        {
            audio.Push();
            await Task.Delay(20, pushCts.Token);
        }
    });

    try
    {
        // With budget enforced, pipeline must terminate after 10 retries (11 attempts total)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await p.Completion.WaitAsync(cts.Token);

        Check(asr.SessionsCreated <= 11, $"retry budget exceeded: created {asr.SessionsCreated} sessions");
        Check(lastState == PipelineState.Error, $"expected Error state but got {lastState}");
    }
    finally
    {
        pushCts.Cancel();
        audio.Finish();
        try { await pushTask; } catch { }
    }
});

await TestAsync("pipeline terminates immediately without retry on fatal authentication error", async () => {
    var audio = new TestAudioSource();
    var asr = new FatalErrorAsr(ProviderErrorKind.Authentication);
    await using var p = new CaptionPipeline(audio, asr, new CountingTranslator(), new() { Translation = new() { DebounceMilliseconds = 0 } });
    PipelineState lastState = PipelineState.Stopped;
    p.StatusChanged += s => lastState = s.State;
    await p.StartAsync();

    audio.Push();
    audio.Finish();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await p.Completion.WaitAsync(cts.Token);

    Check(asr.ConnectCalls == 1, $"authentication error should not retry: made {asr.ConnectCalls} attempts");
    Check(lastState == PipelineState.Error, $"expected Error state but got {lastState}");
});

await TestAsync("pipeline terminates immediately without retry on fatal quota error", async () => {
    var audio = new TestAudioSource();
    var asr = new FatalErrorAsr(ProviderErrorKind.Quota);
    await using var p = new CaptionPipeline(audio, asr, new CountingTranslator(), new() { Translation = new() { DebounceMilliseconds = 0 } });
    PipelineState lastState = PipelineState.Stopped;
    p.StatusChanged += s => lastState = s.State;
    await p.StartAsync();

    audio.Push();
    audio.Finish();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await p.Completion.WaitAsync(cts.Token);

    Check(asr.ConnectCalls == 1, $"quota error should not retry: made {asr.ConnectCalls} attempts");
    Check(lastState == PipelineState.Error, $"expected Error state but got {lastState}");
});

await TestAsync("telemetry collector computes accurate stage quantiles and outcome counts", async () => {
    await Task.CompletedTask;
    var collector = new PipelineTelemetryCollector();
    for (int i = 1; i <= 20; i++)
    {
        collector.Record(new SegmentTelemetryRecord
        {
            SessionId = 1,
            SegmentId = $"seg-{i}",
            Revision = 1,
            IsFinal = i > 10,
            Language = "en",
            QueueWaitMs = i * 2.0,
            AsrServiceMs = 100.0 + i * 5,
            MtDebounceWaitMs = 50.0,
            MtServiceMs = 200.0 + i * 10,
            UiApplyMs = 5.0,
            TotalPipelineMs = 350.0 + i * 17,
            Outcome = i == 20 ? TelemetryOutcome.Failed : (i == 19 ? TelemetryOutcome.CacheHit : (i == 18 ? TelemetryOutcome.Cancelled : TelemetryOutcome.Success))
        });
    }

    var summary = collector.Summarize(new()
    {
        Asr = new() { Provider = "VolcengineAsr2", Model = "async" },
        Translation = new() { Provider = "DeepL" },
        SourceLanguage = "auto",
        TargetLanguage = "zh-Hans"
    });

    Check(summary.SampleCount == 20, $"expected 20 samples, got {summary.SampleCount}");
    Check(summary.FinalCount == 10, $"expected 10 finals, got {summary.FinalCount}");
    Check(summary.PartialCount == 10, $"expected 10 partials, got {summary.PartialCount}");
    Check(summary.CacheHitCount == 1, $"expected 1 cache hit, got {summary.CacheHitCount}");
    Check(summary.CancelledCount == 1, $"expected 1 cancelled, got {summary.CancelledCount}");
    Check(summary.FailedCount == 1, $"expected 1 failed, got {summary.FailedCount}");
    Check(Math.Abs(summary.QueueWait.MedianMilliseconds - 21.0) < 1.0, $"queue wait median mismatch: {summary.QueueWait.MedianMilliseconds}");
    Check(summary.Disclaimer.Contains("端到端耗时测量范围"), "disclaimer should specify scope");
});

await TestAsync("telemetry export is anonymized without credentials, keys or raw transcripts", async () => {
    var collector = new PipelineTelemetryCollector();
    collector.Record(new SegmentTelemetryRecord
    {
        SessionId = 1,
        SegmentId = "secret-segment-id",
        Revision = 1,
        IsFinal = true,
        Language = "en",
        QueueWaitMs = 10.0,
        AsrServiceMs = 150.0,
        MtDebounceWaitMs = 30.0,
        MtServiceMs = 250.0,
        UiApplyMs = 8.0,
        TotalPipelineMs = 448.0,
        Outcome = TelemetryOutcome.Success
    });

    var settings = new AppSettings
    {
        Asr = new() { Provider = "VolcengineAsr2", ApiKey = "sensitive-asr-key-9988", Model = "async" },
        Translation = new() { Provider = "OpenAiCompatible", ApiKey = "sensitive-mt-key-7766", Model = "gpt-4o" },
        SourceLanguage = "en",
        TargetLanguage = "zh-Hans"
    };

    var json = collector.ExportJson(settings);

    Check(!json.Contains("sensitive-asr-key-9988"), "export must not contain ASR key");
    Check(!json.Contains("sensitive-mt-key-7766"), "export must not contain MT key");
    Check(!json.Contains("secret-segment-id"), "summary export must not contain raw segment content");
    Check(json.Contains("VolcengineAsr2") && json.Contains("OpenAiCompatible"), "export should record provider names");
    Check(json.Contains("Disclaimer"), "export should include measurement scope disclaimer");

    var artifactDir = Path.Combine(AppContext.BaseDirectory, "artifacts", "phase5.3");
    Directory.CreateDirectory(artifactDir);
    await File.WriteAllTextAsync(Path.Combine(artifactDir, "diagnostic-telemetry-sample.json"), json);
});

await TestAsync("uncooperative ASR write times out StopAsync and preserves failure state on DisposeAsync", async () => {
    var audio = new TestAudioSource();
    var asr = new UncooperativeSendAsr();
    var p = new CaptionPipeline(audio, asr, new CountingTranslator(), new());
    await p.StartAsync();
    audio.Push();
    await asr.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

    bool stopTimedOut = false;
    try
    {
        await p.StopAsync(StopReason.Reconfigure);
    }
    catch (TimeoutException)
    {
        stopTimedOut = true;
    }
    Check(stopTimedOut, "StopAsync should throw TimeoutException on uncooperative ASR");
    Check(!p.Completion.IsCompleted, "Completion must not be marked complete when session is still running");

    bool disposeTimedOut = false;
    try
    {
        await p.DisposeAsync();
    }
    catch (TimeoutException)
    {
        disposeTimedOut = true;
    }
    Check(disposeTimedOut, "DisposeAsync should throw TimeoutException while session remains active");
    Check(asr.Disposals == 0, "ASR session must not be claimed disposed while still running");

    asr.Release.TrySetResult();
    await p.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    await p.DisposeAsync();
    Check(asr.Disposals == 1, $"ASR session must be disposed exactly once, got {asr.Disposals}");
});

await TestAsync("uncooperative ASR connect times out StopAsync and prevents false completion", async () => {
    var audio = new TestAudioSource();
    var asr = new UncooperativeConnectAsr();
    var p = new CaptionPipeline(audio, asr, new CountingTranslator(), new());
    await p.StartAsync();
    await asr.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

    bool stopTimedOut = false;
    try
    {
        await p.StopAsync(StopReason.Reconfigure);
    }
    catch (TimeoutException)
    {
        stopTimedOut = true;
    }
    Check(stopTimedOut, "StopAsync should throw TimeoutException when connect blocks");
    Check(!p.Completion.IsCompleted, "Completion must not be completed while connect is in flight");

    asr.Release.TrySetResult();
    await p.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    await p.DisposeAsync();
    Check(asr.Disposals == 1, $"ASR session must be disposed exactly once, got {asr.Disposals}");
});

await TestAsync("permanent reader error is preserved and not replaced by concurrent send cancellation", async () => {
    var audio = new TestAudioSource();
    var asr = new FatalReadAsr();
    await using var p = new CaptionPipeline(audio, asr, new CountingTranslator(), new() { Translation = new() { DebounceMilliseconds = 0 } });
    p.InitialRetryDelay = TimeSpan.FromMilliseconds(1);
    p.MaxRetryDelay = TimeSpan.FromMilliseconds(1);
    var statuses = new List<string>();
    p.StatusChanged += x => statuses.Add(x.State.ToString());
    await p.StartAsync();
    audio.Push();
    await p.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    Check(asr.Created == 1, $"expected exactly 1 session created, got {asr.Created}");
    Check(!statuses.Contains("Retrying"), "permanent authentication error must not trigger retry");
    Check(statuses.LastOrDefault() == "Error", $"last state should be Error, got {statuses.LastOrDefault()}");
});

Test("incompatible long revision with negation does not retain old translation", () => {
    var s = new SubtitleStore();
    var generation = s.BeginSession();
    const string before = "I really do want to go there today.";
    const string after = "I really do not want to go there today.";
    s.ApplySource(generation, Speech(before, 1, "negation"));
    s.ApplyTranslation(generation, "negation", 1, "我今天确实想去那里。");
    s.ApplySource(generation, Speech(after, 2, "negation"));
    Check(s.Snapshot()[0].ProvisionalTranslationText == "", "incompatible negation revision should clear provisional translation");
    Check(SubtitleProjection.Expanded(s.Snapshot())[0].Translation == "", "visible translation should be empty rather than showing old affirmative translation");
});

Test("semantic changes (direction, numbers, entity, units) reject old draft", () => {
    Check(!SubtitleStore.IsCompatibleDraft("Turn left at the next street", "Turn right at the next street"), "direction change should be incompatible");
    Check(!SubtitleStore.IsCompatibleDraft("There are 5 apples on the table", "There are 6 apples on the table"), "number change should be incompatible");
    Check(!SubtitleStore.IsCompatibleDraft("John called me yesterday afternoon", "Jack called me yesterday afternoon"), "entity change should be incompatible");
    Check(!SubtitleStore.IsCompatibleDraft("The station is 10 km away from here", "The station is 10 miles away from here"), "unit change should be incompatible");
    Check(!SubtitleStore.IsCompatibleDraft("cat", "category"), "word fragment extension should be incompatible");
    Check(!SubtitleStore.IsCompatibleDraft("You know the secret.", "You know the secret?"), "statement to question should be incompatible");
    Check(!SubtitleStore.IsCompatibleDraft("I want to go", "I want to go nowhere"), "suffix with negation token should be incompatible");
    Check(SubtitleStore.IsCompatibleDraft("Hello world", "Hello world this is a test"), "legitimate word-boundary prefix extension should be compatible");
});

Test("multisegment paragraph retry points to failed segment not successful segment", () => {
    var s = new SubtitleStore();
    var generation = s.BeginSession();
    s.ApplySource(generation, Speech("First sentence.", 1, "failed-A", final: true, start: 0));
    s.ApplyTranslationError(generation, "failed-A", 1, "network failure");
    s.ApplySource(generation, Speech("Second sentence.", 1, "successful-B", final: true, start: 1000));
    s.ApplyTranslation(generation, "successful-B", 1, "第二句。");
    var paragraphs = SubtitleProjection.Expanded(s.Snapshot(), generation);
    Check(paragraphs.Count == 1, "expected 1 joined paragraph");
    var p = paragraphs.Single();
    Check(p.HasError, "paragraph must report error");
    Check(p.CanRetry, "paragraph must allow retry");
    Check(p.SegmentId == "failed-A", $"paragraph retry must point to failed segment 'failed-A', got '{p.SegmentId}'");
    Check(p.SessionId == generation, "session id mismatch");
});

Test("multisegment paragraph retry ignores old session failure for retry eligibility", () => {
    var s = new SubtitleStore();
    var oldSession = s.BeginSession();
    s.ApplySource(oldSession, Speech("Old failure.", 1, "old-A", final: true, start: 0));
    s.ApplyTranslationError(oldSession, "old-A", 1, "timeout");
    var newSession = s.BeginSession(false);
    s.ApplySource(newSession, Speech("New success.", 1, "new-B", final: true, start: 5000));
    s.ApplyTranslation(newSession, "new-B", 1, "新成功。");
    var paragraphs = SubtitleProjection.Expanded(s.Snapshot(), newSession);
    Check(paragraphs.Count == 2, "expected 2 paragraphs across sessions");
    Check(paragraphs[0].HasError && !paragraphs[0].CanRetry, "old session paragraph must not be retryable in new session");
    Check(!paragraphs[1].HasError && !paragraphs[1].CanRetry, "new session paragraph without error must not be retryable");
});

await TestAsync("pipeline retry translation enforces session, revision, final, and cooldown", async () => {
    var audio = new TestAudioSource();
    var asr = new TestAsrProvider();
    var translator = new CountingTranslator();
    await using var p = new CaptionPipeline(audio, asr, translator, new());
    long session = p.Store.BeginSession();

    // 1. Success row cannot be retried
    p.Store.ApplySource(session, Speech("Good morning", 1, "seg1", final: true));
    p.Store.ApplyTranslation(session, "seg1", 1, "早上好");
    Check(!p.RetryTranslation(session, "seg1", 1), "successful row should be rejected by RetryTranslation");

    // 2. Row without error cannot be retried
    p.Store.ApplySource(session, Speech("Pending row", 1, "seg2", final: true));
    Check(!p.RetryTranslation(session, "seg2", 1), "row without error should be rejected by RetryTranslation");

    // 3. Failed final row can be retried
    p.Store.ApplySource(session, Speech("Error row", 1, "seg3", final: true));
    p.Store.ApplyTranslationError(session, "seg3", 1, "503 service unavailable");
    Check(p.RetryTranslation(session, "seg3", 1), "failed final row should be accepted by RetryTranslation");

    // 4. Immediate second retry within cooldown is rejected
    p.Store.ApplyTranslationError(session, "seg3", 1, "503 service unavailable again");
    Check(!p.RetryTranslation(session, "seg3", 1), "immediate retry within 2-second cooldown should be rejected");

    // 5. Older revision cannot be retried when newer revision exists
    p.Store.ApplySource(session, Speech("Error row updated", 2, "seg3", final: true));
    p.Store.ApplyTranslationError(session, "seg3", 2, "503 again");
    Check(!p.RetryTranslation(session, "seg3", 1), "older revision must be rejected when newer revision exists");

    // 6. Wrong session cannot be retried
    Check(!p.RetryTranslation(session + 99, "seg3", 2), "different session must be rejected");
});

await TestAsync("production pipeline wires telemetry and records stage metrics during normal execution", async () => {
    var audio = new TestAudioSource();
    var asr = new TestAsrProvider();
    var translator = new CountingTranslator();
    await using var p = new CaptionPipeline(audio, asr, translator, new() { Translation = new() { DebounceMilliseconds = 0 } });
    await p.StartAsync();

    audio.Push();
    for (int i = 0; i < 100 && !p.Store.Snapshot().Any(x => x.TranslationText.Length > 0); i++)
        await Task.Delay(20);

    var records = p.Telemetry.Snapshot();
    Check(records.Count > 0, "pipeline telemetry must contain records from production translation flow");
    var rec = records[0];
    Check(rec.Outcome == TelemetryOutcome.Success, $"expected Success outcome, got {rec.Outcome}");
    Check(rec.TotalPipelineMs.HasValue && rec.TotalPipelineMs.Value >= 0, "TotalPipelineMs must be measured");
    Check(rec.MtServiceMs.HasValue && rec.MtServiceMs.Value >= 0, "MtServiceMs must be measured");

    var summary = p.Telemetry.Summarize();
    Check(summary.SampleCount > 0, $"expected SampleCount > 0, got {summary.SampleCount}");

    await p.StopAsync();
});

Test("subtitle store dynamic MaxHistory truncates history and preserves session", () => {
    var store = new SubtitleStore(100);
    var session = store.BeginSession();
    for (int i = 1; i <= 20; i++)
    {
        store.ApplySource(session, Speech($"Sentence {i}", 1, $"seg-{i}", final: true, start: i * 1000));
        store.ApplyTranslation(session, $"seg-{i}", 1, $"译文 {i}");
    }
    Check(store.Snapshot().Count == 20, $"expected 20 items, got {store.Snapshot().Count}");

    // Dynamically change MaxHistory to 10
    store.MaxHistory = 10;
    Check(store.MaxHistory == 10, "MaxHistory property should reflect updated limit");
    var snapshot = store.Snapshot();
    Check(snapshot.Count == 10, $"history should be truncated to 10 items, got {snapshot.Count}");
    Check(snapshot[0].SegmentId == "seg-11", $"oldest remaining item should be seg-11, got {snapshot[0].SegmentId}");
    Check(snapshot.Last().SegmentId == "seg-20", $"latest item should be seg-20, got {snapshot.Last().SegmentId}");
    Check(store.CurrentSession == session, "session isolation must be preserved after MaxHistory change");
});

Test("semantic revision matrix prevents provisional draft retain on value or semantic reversal", () => {
    var pairs = new[]
    {
        ("negation", "I really do want to go there today.", "I really do not want to go there today.", false),
        ("number", "There are 5 apples on the table", "There are 6 apples on the table", false),
        ("entity", "John called me yesterday afternoon", "Jack called me yesterday afternoon", false),
        ("direction", "Turn left at the next street", "Turn right at the next street", false),
        ("units", "The station is 10 km away", "The station is 10 miles away", false),
        ("deletion", "I can hardly wait for tomorrow", "I can wait for tomorrow", false),
        ("insertion", "We will leave today", "We will probably leave today", false),
        ("ordinary_prefix", "Hello world", "Hello world this is a test", true),
        ("decimal_extension", "The dose is 5", "The dose is 5.5 mg", false),
        ("digit_group_extension", "There are 1", "There are 1,000 people", false)
    };
    foreach (var pair in pairs)
    {
        var s = new SubtitleStore();
        var g = s.BeginSession();
        s.ApplySource(g, Speech(pair.Item2, revision: 1, id: "x", final: true));
        s.ApplyTranslation(g, "x", 1, "OLD_TRANSLATION");
        s.ApplySource(g, Speech(pair.Item3, revision: 2, id: "x", final: true));
        var shown = SubtitleProjection.Expanded(s.Snapshot(), g).Single().Translation;
        bool actualRetain = shown.Length > 0;
        Check(actualRetain == pair.Item4, $"semantic test '{pair.Item1}': expected retain={pair.Item4}, actual={actualRetain} (before='{pair.Item2}', after='{pair.Item3}')");
    }
});

await TestAsync("five second provisional preview expiry clears unconfirmed translation", async () => {
    var s = new SubtitleStore();
    var g = s.BeginSession();
    s.ApplySource(g, Speech("Hello", revision: 1, id: "ttl", final: false));
    s.ApplyTranslation(g, "ttl", 1, "OLD");
    s.ApplySource(g, Speech("Hello world", revision: 2, id: "ttl", final: false));
    var before = SubtitleProjection.Expanded(s.Snapshot(), g).Single().Translation;
    Check(before == "OLD", "expected provisional translation before expiry");
    await Task.Delay(5200);
    var afterIdle = SubtitleProjection.Expanded(s.Snapshot(), g).Single().Translation;
    Check(afterIdle == "", $"provisional translation should expire after 5 seconds idle, got '{afterIdle}'");
    s.ApplySource(g, Speech("Hello world today", revision: 3, id: "ttl", final: false));
    var afterFurther = SubtitleProjection.Expanded(s.Snapshot(), g).Single().Translation;
    Check(afterFurther == "", $"provisional translation should not be retained after expiry, got '{afterFurther}'");
});

await TestAsync("HTTP 429 Retry-After blocks manual retry during cooldown", async () => {
    int calls = 0;
    var translator = new BarrierTranslator((req, token) => {
        calls++;
        throw new ProviderException(ProviderErrorKind.RateLimited, "controlled rate limit", TimeSpan.FromSeconds(120));
    });
    var audio = new TestAudioSource();
    var asr = new TestAsrProvider();
    await using var p = new CaptionPipeline(audio, asr, translator, new() { Translation = new() { DebounceMilliseconds = 0 } });
    await p.StartAsync();
    audio.Push();
    for (int i = 0; i < 100 && !p.Store.Snapshot().Any(x => x.TranslationError != null); i++)
        await Task.Delay(20);
    Check(calls == 1, $"expected 1 initial call, got {calls}");
    var row = p.Store.Snapshot().Single();
    bool accepted = p.RetryTranslation(row.SessionId, row.SegmentId, row.Revision);
    Check(!accepted, "RetryTranslation must be rejected when provider is under active RateLimited cooldown");
    await Task.Delay(50);
    Check(calls == 1, $"no second HTTP request should be sent during cooldown, calls={calls}");
    await p.StopAsync();
});

await TestAsync("terminal HTTP errors (401/402) block manual retry without configuration change", async () => {
    foreach (var kind in new[] { ProviderErrorKind.Authentication, ProviderErrorKind.Quota })
    {
        int calls = 0;
        var translator = new BarrierTranslator((req, token) => {
            calls++;
            throw new ProviderException(kind, $"controlled terminal error: {kind}");
        });
        var audio = new TestAudioSource();
        var asr = new TestAsrProvider();
        await using var p = new CaptionPipeline(audio, asr, translator, new() { Translation = new() { DebounceMilliseconds = 0 } });
        await p.StartAsync();
        audio.Push();
        for (int i = 0; i < 100 && !p.Store.Snapshot().Any(x => x.TranslationError != null); i++)
            await Task.Delay(20);
        Check(calls == 1, $"expected 1 initial call for {kind}, got {calls}");
        var row = p.Store.Snapshot().Single();
        bool accepted = p.RetryTranslation(row.SessionId, row.SegmentId, row.Revision);
        Check(!accepted, $"RetryTranslation must be rejected for terminal error {kind} without config change");
        await Task.Delay(50);
        Check(calls == 1, $"calls must remain 1 without config change for {kind}");
        await p.StopAsync();
    }
});

await TestAsync("telemetry TotalPipelineMs anchors to audio capture and records stop cancellation", async () => {
    var audio = new TestAudioSource();
    var asr = new TestAsrProvider();
    var blocking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var translator = new BarrierTranslator(async (req, token) => {
        blocking.TrySetResult();
        await Task.Delay(Timeout.Infinite, token);
        return "never";
    });
    await using var p = new CaptionPipeline(audio, asr, translator, new() { Translation = new() { DebounceMilliseconds = 0 } });
    await p.StartAsync();
    audio.Push();
    await blocking.Task.WaitAsync(TimeSpan.FromSeconds(3));
    await p.StopAsync(StopReason.Reconfigure);
    var records = p.Telemetry.Snapshot();
    Check(records.Any(r => r.Outcome == TelemetryOutcome.Cancelled), "StopAsync must record Cancelled telemetry record");
});

Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/core-tests.json", JsonSerializer.Serialize(new { total = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { total = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;
