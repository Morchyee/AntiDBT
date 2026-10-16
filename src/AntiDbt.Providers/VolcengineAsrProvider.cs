using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AntiDbt.Core;
namespace AntiDbt.Providers;

public sealed class VolcengineAsrProvider : IAsrProvider
{
    public IAsrSession CreateSession(AsrConfiguration configuration) => new Session(configuration);
    private sealed class Session(AsrConfiguration config) : IAsrSession
    {
        private const int Limit = 4 * 1024 * 1024;
        private readonly ClientWebSocket socket = new();
        private readonly SemaphoreSlim sendLock = new(1);
        private readonly CancellationTokenSource finalDrain = new();
        // Finalized transcript text is released immediately. The watermark suppresses
        // repeated finalized prefixes; only the current provisional suffix is retained.
        private readonly List<AsrResult> provisional = new();
        private readonly string sessionId = Guid.NewGuid().ToString("N");
        private long finalizedThrough = -1, nextSegment;
        private int sequence = 1, reader, disposed;
        private bool completed;
        private static ProviderException ClassifyWebSocketError(WebSocketException wse)
        {
            var msg = wse.Message;
            if (msg.Contains("401") || msg.Contains("403"))
                return ProviderHttp.Failure(ProviderErrorKind.Authentication);
            if (msg.Contains("400"))
                return ProviderHttp.Failure(ProviderErrorKind.Configuration);
            if (msg.Contains("404"))
                return ProviderHttp.Failure(ProviderErrorKind.ModelUnavailable);
            if (msg.Contains("429"))
                return new ProviderException(ProviderErrorKind.RateLimited, "请求限流，正在遵守服务冷却时间");
            if (msg.Contains("402") || msg.Contains("456"))
                return ProviderHttp.Failure(ProviderErrorKind.Quota);
            return ProviderHttp.Failure(ProviderErrorKind.Network);
        }
        private static ProviderException ClassifyAsrError(int code)
        {
            var kind = code switch
            {
                // Volcengine Big Model Streaming ASR (Seed-ASR/SAUC) official protocol error codes:
                // 45000001: invalid client request parameters
                // 45000151: unsupported business cluster or model
                1004 or 45000001 or 45000151 => ProviderErrorKind.Configuration,

                // 45000002: authentication failed (invalid token, appId, etc.)
                // 1001/1002/1003: legacy auth error codes
                1001 or 1002 or 1003 or 45000002 => ProviderErrorKind.Authentication,

                // 45000003: quota exhausted or account balance depleted
                45000003 => ProviderErrorKind.Quota,

                // 45000004/45000005/1005: rate limited / concurrency / QPS quota reached
                1005 or 45000004 or 45000005 => ProviderErrorKind.RateLimited,

                // 45000081: server recognition timeout
                45000081 => ProviderErrorKind.Timeout,

                // Standard HTTP status codes
                _ when code is 401 or 403 => ProviderErrorKind.Authentication,
                _ when code is 400 => ProviderErrorKind.Configuration,
                _ when code is 404 => ProviderErrorKind.ModelUnavailable,
                _ when code is 429 => ProviderErrorKind.RateLimited,
                _ when code is 402 or 456 => ProviderErrorKind.Quota,
                _ when code is 408 or 504 => ProviderErrorKind.Timeout,
                _ => ProviderErrorKind.Service
            };
            return ProviderHttp.Failure(kind);
        }
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            var endpoint = ProviderConfiguration.Endpoint(config.Endpoint, true);
            foreach (var header in ProviderConfiguration.Headers(config.ApiKey, config.Options, config.Secrets, "volc.seedasr.sauc.duration"))
                socket.Options.SetRequestHeader(header.Key, header.Value);
            socket.Options.SetRequestHeader("X-Api-Connect-Id", Guid.NewGuid().ToString());
            socket.Options.SetRequestHeader("X-Api-Sequence", "-1");
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken);
            }
            catch (WebSocketException wse) { throw ClassifyWebSocketError(wse); }
            var audio = new Dictionary<string, object> { ["format"] = "pcm", ["codec"] = "raw", ["rate"] = 16000, ["bits"] = 16, ["channel"] = 1 };
            if (!string.IsNullOrWhiteSpace(config.Language))
                audio["language"] = config.Language;
            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                user = new
                {
                    uid = sessionId
                },
                audio,
                request = new
                {
                    model_name = string.IsNullOrEmpty(config.Model) ? "bigmodel" : config.Model,
                    show_utterances = true,
                    enable_nonstream = true,
                    enable_itn = true,
                    enable_punc = true
                }
            });
            await SendPacket(1, body, false, cancellationToken);
        }
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
        {
            if (pcm.Length % 2 != 0 || pcm.Length > Limit)
                throw new ArgumentException("PCM 音频块格式无效。");
            return SendPacket(2, pcm, false, cancellationToken);
        }
        private async Task SendPacket(int type, ReadOnlyMemory<byte> payload, bool last, CancellationToken token)
        {
            await sendLock.WaitAsync(token);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                if (completed)
                {
                    if (last)
                        return;
                    throw new InvalidOperationException("语音会话已完成。");
                }
                if (last)
                    completed = true;
                var packet = new byte[12 + payload.Length];
                packet[0] = 0x11;
                packet[1] = (byte)((type << 4) | (last ? 3 : 1));
                packet[2] = (byte)(type == 1 ? 0x10 : 0);
                BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4), last ? -sequence++ : sequence++);
                BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), (uint)payload.Length);
                payload.CopyTo(packet.AsMemory(12));
                await socket.SendAsync(packet, WebSocketMessageType.Binary, true, token);
                if (last)
                    finalDrain.CancelAfter(TimeSpan.FromSeconds(3));
            }
            catch (WebSocketException wse) { throw ClassifyWebSocketError(wse); }
            finally { sendLock.Release(); }
        }
        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            if (completed)
                return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await SendPacket(2, ReadOnlyMemory<byte>.Empty, true, timeout.Token);
        }
        public async IAsyncEnumerable<AsrResult> GetResultsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref reader, 1) != 0)
                throw new InvalidOperationException("每个语音会话只允许一个结果读取器。");
            using var receiveToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, finalDrain.Token);
            var bytes = new byte[8192];
            while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                using var buffer = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    try
                    {
                        part = await socket.ReceiveAsync(bytes, receiveToken.Token);
                    }
                    catch (OperationCanceledException) when (finalDrain.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { yield break; }
                    catch (WebSocketException wse) { throw ClassifyWebSocketError(wse); }
                    if (part.MessageType == WebSocketMessageType.Close)
                        yield break;
                    if (part.MessageType != WebSocketMessageType.Binary || buffer.Length + part.Count > Limit)
                        throw InvalidPacket();
                    buffer.Write(bytes, 0, part.Count);
                } while (!part.EndOfMessage);
                var (payload, last) = Decode(buffer.ToArray());
                foreach (var result in Parse(payload, last))
                    yield return result;
                if (last)
                    yield break;
            }
        }
        private static InvalidOperationException InvalidPacket() => new("语音服务响应格式无效。");
        private static (byte[], bool) Decode(byte[] packet)
        {
            if (packet.Length < 4 || packet[0] >> 4 != 1 || (packet[0] & 15) == 0 || packet[3] != 0)
                throw InvalidPacket();
            int type = packet[1] >> 4, flags = packet[1] & 15, serialization = packet[2] >> 4, compression = packet[2] & 15;
            if (flags > 3 || compression > 1 || type is not (9 or 15) || serialization != 1)
                throw InvalidPacket();
            int offset = (packet[0] & 15) * 4;
            if (offset > packet.Length)
                throw InvalidPacket();

            if (type == 15)
            {
                // Official Volcengine Server Error Response (message_type == 15) layout:
                // Header (offset bytes) + error_code (4 bytes BE) + error_message_size (4 bytes BE) + error_message (error_message_size bytes)
                if (packet.Length < offset + 8)
                    throw InvalidPacket();
                int errorCode = BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(offset, 4));
                offset += 4;
                uint msgSize = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset, 4));
                offset += 4;
                if (msgSize > Limit || packet.Length - offset != (int)msgSize)
                    throw InvalidPacket();
                throw ClassifyAsrError(errorCode);
            }

            if ((flags & 1) != 0)
            {
                if (packet.Length < offset + 4)
                    throw InvalidPacket();
                int seq = BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(offset));
                if ((flags == 3 && seq >= 0) || (flags == 1 && seq <= 0))
                    throw InvalidPacket();
                offset += 4;
            }

            if (packet.Length < offset + 4)
                throw InvalidPacket();
            uint length = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset));
            offset += 4;
            if (length > Limit || length != packet.Length - offset)
                throw InvalidPacket();
            var payload = packet.AsSpan(offset).ToArray();
            if (compression == 1)
            {
                try
                {
                    using var gzip = new GZipStream(new MemoryStream(payload), CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    var chunk = new byte[8192];
                    int count;
                    while ((count = gzip.Read(chunk)) > 0)
                    {
                        if (output.Length + count > Limit)
                            throw InvalidPacket();
                        output.Write(chunk, 0, count);
                    }
                    payload = output.ToArray();
                }
                catch (InvalidDataException) { throw InvalidPacket(); }
            }
            return (payload, (flags & 2) != 0);
        }
        private List<AsrResult> Parse(byte[] payload, bool last)
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (root.TryGetProperty("code", out var code) && code.GetInt32() is not (0 or 20000000))
                    throw ClassifyAsrError(code.GetInt32());
                if (root.TryGetProperty("payload_msg", out var wrapped))
                    root = wrapped;
                if (!root.TryGetProperty("result", out var result))
                    return [];
                if (result.ValueKind != JsonValueKind.Object)
                    throw InvalidPacket();
                var snapshot = new List<Utterance>();
                if (result.TryGetProperty("utterances", out var utterances))
                {
                    foreach (var u in utterances.EnumerateArray())
                        snapshot.Add(new(u.GetProperty("start_time").GetInt64(), u.GetProperty("end_time").GetInt64(), u.GetProperty("text").GetString() ?? "", u.TryGetProperty("definite", out var definite) && definite.GetBoolean()));
                }
                else if (result.TryGetProperty("text", out var text))
                    snapshot.Add(new(0, 0, text.GetString() ?? "", last));
                else
                    return [];
                return Reconcile(snapshot);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or FormatException or OverflowException || e is InvalidOperationException && !e.Message.StartsWith("语音服务")) { throw InvalidPacket(); }
        }
        private sealed record Utterance(long Start, long End, string Text, bool Final);
        private List<AsrResult> Reconcile(List<Utterance> snapshot)
        {
            var incoming = new List<Utterance>();
            long previousEnd = -1;
            bool sawProvisional = false;
            foreach (var utterance in snapshot)
            {
                if (utterance.Start < 0 || utterance.End < utterance.Start || utterance.Start < previousEnd)
                    throw InvalidPacket();
                previousEnd = utterance.End;
                if (utterance.End <= finalizedThrough)
                    continue;
                // Definites form an immutable prefix. Refuse a response which tries to
                // rewrite across that boundary instead of duplicating finalized text.
                if (utterance.Start < finalizedThrough || sawProvisional && utterance.Final)
                    throw InvalidPacket();
                sawProvisional |= !utterance.Final;
                incoming.Add(utterance);
                if (incoming.Count > 512)
                    throw InvalidPacket();
            }
            var output = new List<AsrResult>();
            bool sameShape = incoming.Count == provisional.Count;
            int stableCount = sameShape ? incoming.Count : 0;
            if (!sameShape)
            {
                while (stableCount < Math.Min(incoming.Count, provisional.Count))
                {
                    var old = provisional[stableCount];
                    var value = incoming[stableCount];
                    if (old.Text != value.Text || old.AudioStart != TimeSpan.FromMilliseconds(value.Start) || old.AudioEnd != TimeSpan.FromMilliseconds(value.End))
                        break;
                    stableCount++;
                }
                // A split/merge changes suffix segmentation; retire superseded rows
                // explicitly so both subtitles and pending translations can remove them.
                foreach (var old in provisional.Skip(stableCount))
                    output.Add(old with
                    {
                        Revision = old.Revision + 1,
                        Text = "",
                        IsDeleted = true
                    });
            }
            var next = new List<AsrResult>();
            for (int i = 0; i < incoming.Count; i++)
            {
                var utterance = incoming[i];
                var old = i < stableCount ? provisional[i] : null;
                var start = TimeSpan.FromMilliseconds(utterance.Start);
                var end = TimeSpan.FromMilliseconds(utterance.End);
                bool changed = old == null || old.Text != utterance.Text || old.AudioStart != start || old.AudioEnd != end || old.IsFinal != utterance.Final;
                var result = changed ? new AsrResult(old?.SegmentId ?? $"{sessionId}:{++nextSegment}", (old?.Revision ?? 0) + 1, utterance.Text, utterance.Final, start, end, string.IsNullOrWhiteSpace(config.Language) ? null : config.Language) : old!;
                if (changed)
                    output.Add(result);
                if (utterance.Final)
                    finalizedThrough = Math.Max(finalizedThrough, utterance.End);
                else
                    next.Add(result);
            }
            provisional.Clear();
            provisional.AddRange(next);
            return output;
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            // Abort before waiting for a sender, so disposal cannot wait on network I/O.
            finalDrain.Cancel();
            socket.Abort();
            await sendLock.WaitAsync();
            try
            {
                socket.Dispose();
                finalDrain.Dispose();
            }
            finally { sendLock.Release(); }
        }
    }
}
