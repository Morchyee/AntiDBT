using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AntiDbt.Core;
using AntiDbt.Providers;

internal static class ReviewCases
{
    private static string mtBody = "";
    private record U(long start_time, long end_time, string text, bool definite);
    private static readonly Dictionary<string,U[][]> Fixtures = new()
    {
        ["shift"] = [[new(0,100,"hi",false)], [new(20,120,"hello",false)], [new(20,160,"hello",false)], [new(20,160,"hello",true)]],
        ["split-merge"] = [[new(0,200,"hello world",false)], [new(0,80,"hello",false),new(80,200,"world",false)], [new(10,220,"hello world",true)]],
        ["suffix"] = [[new(0,100,"stable",false),new(100,300,"second third",false)], [new(0,100,"stable",false),new(100,200,"second",false),new(200,300,"third",false)], [new(0,100,"stable",true),new(100,200,"second",true),new(200,300,"third",true)]],
        ["prefix"] = [[new(0,100,"first",true),new(100,200,"second",false)], [new(0,100,"first",true),new(100,200,"second",false)], [new(0,100,"first",true),new(110,220,"second corrected",true),new(220,300,"third",false)], [new(0,100,"first",true),new(110,220,"second corrected",true),new(220,300,"third",true)]],
        ["extended"] = [[new(0,100,"extended",true)]]
    };
    internal static void Map(WebApplication app)
    {
        app.MapPost("/review-mt", async context => { using var reader=new StreamReader(context.Request.Body); mtBody=await reader.ReadToEndAsync(); await context.Response.WriteAsJsonAsync(new {code=20000000,data=new {translation_list=new[] {new {translation="译文"}}}}); });
        foreach (var fixture in Fixtures)
        {
            app.Map("/review-"+fixture.Key,async context =>
            {
                using var socket=await context.WebSockets.AcceptWebSocketAsync();
                var bytes=new byte[8192]; WebSocketReceiveResult received;
                do { received=await socket.ReceiveAsync(bytes,CancellationToken.None); } while(!received.EndOfMessage);
                for(int i=0;i<fixture.Value.Length;i++)
                {
                    var payload=JsonSerializer.SerializeToUtf8Bytes(new {result=new {utterances=fixture.Value[i]}});
                    bool extended=fixture.Key=="extended"; int header=extended?8:4;
                    var frame=new byte[header+8+payload.Length];
                    frame[0]=(byte)(extended?0x12:0x11); frame[1]=(byte)(i==fixture.Value.Length-1?0x93:0x91); frame[2]=0x10;
                    if(extended) { frame[4]=0x12;frame[5]=0x34;frame[6]=0x56;frame[7]=0x78; }
                    BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(header),i==fixture.Value.Length-1?-(i+1):i+1);
                    BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(header+4),payload.Length); payload.CopyTo(frame,header+8);
                    await socket.SendAsync(frame,WebSocketMessageType.Binary,true,CancellationToken.None);
                }
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"",CancellationToken.None);
            });
        }
    }
    internal static async Task Run(string address, Func<string,Func<Task>,Task> check)
    {
        void Assert(bool test,string message) { if(!test) throw new Exception(message); }
        async Task<List<AsrResult>> Gather(string fixture)
        {
            var config=ProviderCatalog.DefaultSettings().Asr with {Endpoint=address.Replace("http:","ws:")+"/review-"+fixture,ApiKey="dummy-key"};
            await using var session=ProviderCatalog.CreateAsr(config).CreateSession(config);
            using var cancellation=new CancellationTokenSource(5000);
            await session.ConnectAsync(cancellation.Token); var results=new List<AsrResult>();
            await foreach(var result in session.GetResultsAsync(cancellation.Token)) results.Add(result);
            return results;
        }
        await check("review shifted start and timestamp-only revision",async()=>
        {
            var results=await Gather("shift");
            Assert(results.Count==4 && results.Select(x=>x.SegmentId).Distinct().Count()==1,"provisional identity must survive shifted start");
            Assert(results[2].AudioEnd==TimeSpan.FromMilliseconds(160) && results[2].Revision==3,"same text timing must revise");
        });
        await check("review split and merge tombstones remove obsolete store rows",async()=>
        {
            var results=await Gather("split-merge"); var store=new SubtitleStore(); long session=store.BeginSession();
            foreach(var result in results) store.ApplySource(session,result);
            var rows=store.Snapshot();
            Assert(results.Count(x=>x.IsDeleted)==3,"split and merge must delete replaced provisional suffix");
            Assert(results.Count(x => x.BatchSize > 1) == 2 && results.Where(x => x.BatchSize > 1).All(x => x.BatchSize == 3), "split/merge must expose complete atomic reconciliation batches");
            Assert(rows.Count==1 && rows[0].SourceText=="hello world" && rows[0].IsFinal && rows[0].AudioStart==TimeSpan.FromMilliseconds(10),"no stale split rows");
        });
        await check("review unchanged provisional prefix survives suffix split",async()=> { var results=await Gather("suffix"); var stable=results.Where(x=>x.Text=="stable").ToArray(); Assert(stable.Length==2 && stable.Select(x=>x.SegmentId).Distinct().Count()==1 && results.Count(x=>x.IsDeleted)==1,"unchanged provisional prefix identity"); });
        await check("review cumulative finalized prefix dedup",async()=>
        {
            var results=await Gather("prefix"); var store=new SubtitleStore(); long session=store.BeginSession();
            foreach(var result in results) store.ApplySource(session,result);
            Assert(results.Count(x=>x.Text=="first" && !x.IsDeleted)==1,"repeated finalized prefix must not emit");
            var rows=store.Snapshot(); Assert(rows.Count==3 && rows.All(x=>x.IsFinal) && rows.Select(x=>x.SourceText).SequenceEqual(new[]{"first","second corrected","third"}),"cumulative order/final rows");
        });
        await check("review extended nibble header",async()=> { var results=await Gather("extended"); Assert(results.Count==1 && results[0].Text=="extended","extended header payload offset"); });
        await check("review actual default ASR Store to MT automatic source",async()=>
        {
            var result=(await Gather("shift")).Last(); var store=new SubtitleStore(); long session=store.BeginSession(); var row=store.ApplySource(session,result)!;
            Assert(row.Language=="auto","exercise real Core sentinel");
            var config=ProviderCatalog.DefaultSettings().Translation with {Endpoint=address+"/review-mt",ApiKey="dummy-key"};
            await new VolcengineMachineTranslationProvider(config).TranslateAsync(new(session,row.SegmentId,row.Revision,row.SourceText,row.Language,"zh",[]),CancellationToken.None);
            using var json=JsonDocument.Parse(mtBody); Assert(!json.RootElement.TryGetProperty("source_language",out _),"application auto sentinel must be omitted");
        });
                await check("review MT official language code aliases",async()=>
        {
            var config=ProviderCatalog.DefaultSettings().Translation with {Endpoint=address+"/review-mt",ApiKey="dummy-key"};
            await new VolcengineMachineTranslationProvider(config).TranslateAsync(new(1,"s",1,"hello","en-US","zh-CN",[]),CancellationToken.None);
            using var json=JsonDocument.Parse(mtBody); Assert(json.RootElement.GetProperty("source_language").GetString()=="en" && json.RootElement.GetProperty("target_language").GetString()=="zh","documented en/zh codes");
        });
await check("review disposal idempotent",async()=> { var session=new VolcengineAsrProvider().CreateSession(ProviderCatalog.DefaultSettings().Asr); await session.DisposeAsync(); await session.DisposeAsync(); });
    }
}



