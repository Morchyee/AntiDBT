using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using AntiDbt.Core;

namespace AntiDbt.Providers;

// File transcription, final-only. The VAD/chunker is local; this is not native streaming ASR.
public sealed class GroqWhisperProvider(HttpClient? testClient = null) : IAsrProvider
{
    readonly ProviderHttp http = new(testClient);
    public IAsrSession CreateSession(AsrConfiguration configuration) => new Session(configuration,http,testClient is not null);
    sealed record Job(byte[] Pcm,double Start,double End,bool Overlap,long Id);
    sealed class Session : IAsrSession
    {
        readonly AsrConfiguration config;readonly ProviderHttp http;readonly Uri endpoint;
        readonly Channel<Job> jobs=Channel.CreateBounded<Job>(new BoundedChannelOptions(4){FullMode=BoundedChannelFullMode.Wait,SingleReader=true});
        readonly Channel<AsrResult> results=Channel.CreateBounded<AsrResult>(new BoundedChannelOptions(256){FullMode=BoundedChannelFullMode.Wait,SingleReader=true});
        readonly CancellationTokenSource stop=new();readonly object gate=new();
        readonly List<byte[]> active=[];readonly Queue<byte[]> pre=[];byte[] remainder=[];
        Task worker=Task.CompletedTask;long frame,id;int silence,voiced,newFrames;double activeStart;bool overlap,started,completed,disposed;
        long droppedJobs;
        public long DroppedJobs => Interlocked.Read(ref droppedJobs);
        readonly int minimum,maximum,silenceLimit,overlapFrames;readonly double threshold;
        string previousText="";long previousJob;
        public Session(AsrConfiguration config,ProviderHttp http,bool testClient)
        {
            this.config=config;endpoint=ProviderHttp.Endpoint(config.Endpoint,"api.groq.com");ProviderHttp.Key(config.ApiKey);
            if(testClient&&!endpoint.IsLoopback)throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
            if(config.Model is not ("whisper-large-v3-turbo" or "whisper-large-v3"))throw ProviderHttp.Failure(ProviderErrorKind.ModelUnavailable);
            this.http=http;
            minimum=Number("minSegmentMs",1000,300,10000)/10;maximum=Number("maxSegmentMs",4000,Math.Max(500,minimum*10),30000)/10;
            silenceLimit=Number("silenceMs",600,200,3000)/10;overlapFrames=Number("overlapMs",200,0,500)/10;
            threshold=double.TryParse(config.Options.GetValueOrDefault("vadThreshold","0.008"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var level)?Math.Clamp(level,.0001,.5):.008;
        }
        int Number(string key,int fallback,int min,int max)=>int.TryParse(config.Options.GetValueOrDefault(key),out var value)?Math.Clamp(value,min,max):fallback;
        public Task ConnectAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();lock(gate){ObjectDisposedException.ThrowIf(disposed,this);if(started)throw ProviderHttp.Failure(ProviderErrorKind.Configuration);started=true;worker=PumpAsync(stop.Token);}return Task.CompletedTask;
        }
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();lock(gate)
            {
                if(!started||completed||disposed||pcm.Length%2!=0)throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
                var input=new byte[remainder.Length+pcm.Length];remainder.CopyTo(input,0);pcm.Span.CopyTo(input.AsSpan(remainder.Length));int offset=0;
                while(offset+320<=input.Length){Accept(input.AsSpan(offset,320).ToArray());offset+=320;}remainder=input[offset..];
            }
            return Task.CompletedTask;
        }
        void Accept(byte[] bytes)
        {
            double sum=0;for(int i=0;i<bytes.Length;i+=2){double value=BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i))/32768d;sum+=value*value;}
            bool voice=Math.Sqrt(sum/(bytes.Length/2))>=threshold;
            if(active.Count==0)
            {
                if(!voice){pre.Enqueue(bytes);while(pre.Count>20)pre.Dequeue();frame++;return;}
                activeStart=(frame-pre.Count)*.01;active.AddRange(pre);pre.Clear();voiced=0;silence=0;newFrames=0;overlap=false;
            }
            active.Add(bytes);newFrames++;if(voice){voiced++;silence=0;}else silence++;
            frame++;
            if(active.Count>=maximum)Emit(true);
            else if(silence>=silenceLimit&&active.Count>=minimum)Emit(false);
        }
        void Emit(bool continuation)
        {
            if (newFrames > 0 && voiced >= 15)
            {
                var job = new Job(active.SelectMany(x => x).ToArray(), activeStart, frame * .01, overlap, ++id);
                while (!jobs.Writer.TryWrite(job))
                {
                    if (jobs.Reader.TryRead(out _))
                        Interlocked.Increment(ref droppedJobs);
                    else
                    {
                        Interlocked.Increment(ref droppedJobs);
                        break;
                    }
                }
            }
            var tail = continuation && overlapFrames > 0 ? active.TakeLast(overlapFrames).ToArray() : [];
            active.Clear();active.AddRange(tail);activeStart=(frame-tail.Length)*.01;overlap=tail.Length>0;newFrames=0;voiced=0;silence=0;
        }
        async Task PumpAsync(CancellationToken token)
        {
            try
            {
                await foreach(var job in jobs.Reader.ReadAllAsync(token))
                {
                    using var doc=await http.JsonAsync(()=>Request(job.Pcm),Number("timeoutSeconds",30,1,120),token,retryServerErrors:false);
                    string text;
                    try{text=doc.RootElement.GetProperty("text").GetString()??throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);}
                    catch(Exception e)when(e is KeyNotFoundException or InvalidOperationException){throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);}
                    string original=text.Trim();text=job.Overlap&&job.Id==previousJob+1?RemoveOverlap(previousText,original):original;previousText=original;previousJob=job.Id;
                    if(text.Length>0)await results.Writer.WriteAsync(new("groq-"+job.Id,1,text,true,TimeSpan.FromSeconds(job.Start),TimeSpan.FromSeconds(job.End),ProviderHttp.Language(config.Language)),token);
                }
                results.Writer.TryComplete();
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){results.Writer.TryComplete();}
            catch(Exception error){results.Writer.TryComplete(error is ProviderException?error:ProviderHttp.Failure(ProviderErrorKind.Service));}
        }
        HttpRequestMessage Request(byte[] pcm)
        {
            var message=new HttpRequestMessage(HttpMethod.Post,endpoint);message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",config.ApiKey);
            var form=new MultipartFormDataContent();var audio=new ByteArrayContent(Wav(pcm));audio.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");form.Add(audio,"file","segment.wav");
            form.Add(new StringContent(config.Model),"model");form.Add(new StringContent("json"),"response_format");form.Add(new StringContent("0"),"temperature");
            var lang=ProviderHttp.Language(config.Language);if(lang.Length>0)form.Add(new StringContent(lang),"language");message.Content=form;return message;
        }
        public IAsyncEnumerable<AsrResult> GetResultsAsync(CancellationToken token)=>results.Reader.ReadAllAsync(token);
        public Task CompleteAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();lock(gate){if(!completed){completed=true;if(remainder.Length>0){var tail=new byte[320];remainder.CopyTo(tail,0);Accept(tail);remainder=[];}Emit(false);jobs.Writer.TryComplete();}}
            return Task.CompletedTask;
        }
        public async ValueTask DisposeAsync()
        {
            lock(gate){if(disposed)return;disposed=true;completed=true;jobs.Writer.TryComplete();stop.Cancel();}
            try{await worker.WaitAsync(TimeSpan.FromSeconds(2));}catch(OperationCanceledException){}catch(TimeoutException){}
            results.Writer.TryComplete();stop.Dispose();
        }
    }
    internal static byte[] Wav(byte[] pcm)
    {
        using var output=new MemoryStream();using var writer=new BinaryWriter(output,Encoding.ASCII,true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(pcm.Length);writer.Write(pcm);return output.ToArray();
    }
    internal static string RemoveOverlap(string previous,string current)
    {
        var prior=previous.TrimEnd(' ','.',',','!','?','，','。','！','？');var next=current.TrimStart();
        for(int length=Math.Min(200,Math.Min(prior.Length,next.Length));length>=2;length--)
        {
            if(!prior.AsSpan(prior.Length-length).Equals(next.AsSpan(0,length),StringComparison.OrdinalIgnoreCase))continue;
            bool cjk=next.Take(length).Any(c=>c>=0x3400&&c<=0x9fff);
            bool boundary=prior.Length==length||!char.IsLetterOrDigit(prior[prior.Length-length-1]);
            bool endBoundary=length==next.Length||!char.IsLetterOrDigit(next[length]);
            if(cjk||boundary&&endBoundary&&length>=3)return next[length..].TrimStart(' ','.',',','，','。','!','?');
        }
        return current;
    }
}
