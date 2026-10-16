using System.Net.Http.Json;
using System.Text.Json;
using AntiDbt.Core;
namespace AntiDbt.Providers;

public sealed class VolcengineMachineTranslationProvider : ITranslationProvider
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly TranslationConfiguration config;
    private readonly HttpClient client;
    public VolcengineMachineTranslationProvider(TranslationConfiguration config, HttpClient? client = null)
    {
        // An injected client's redirect policy cannot be inspected. Restrict this test seam
        // to loopback so production credentials always use our no-redirect handler.
        if (client != null && !ProviderConfiguration.Endpoint(config.Endpoint, false).IsLoopback)
            throw new ArgumentException("自定义 HTTP 客户端仅用于本机协议测试。");
        this.config = config;
        this.client = client ?? SharedClient;
    }
    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var uri = ProviderConfiguration.Endpoint(config.Endpoint, false);
        var headers = ProviderConfiguration.Headers(config.ApiKey, config.Options, config.Secrets, "volc.speech.mt");
        if (string.IsNullOrWhiteSpace(request.TargetLanguage) || string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("翻译文本和目标语言不能为空。");
        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        foreach (var header in headers)
            message.Headers.Add(header.Key, header.Value);
        var source = ProviderLanguages.MtSourceCode(config,request.SourceLanguage);
        var target = ProviderLanguages.MtTargetCode(config,request.TargetLanguage);
        if (target.Length == 0)
            throw new ArgumentException("请选择目标语言。");
        var body = new Dictionary<string, object> { ["target_language"] = target, ["text_list"] = new[] { request.Text } };
        if (source.Length != 0)
            body["source_language"] = source;
        message.Content = JsonContent.Create(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 300)));
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var kind = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.TooManyRequests => ProviderErrorKind.RateLimited,
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => ProviderErrorKind.Authentication,
                    System.Net.HttpStatusCode.BadRequest => ProviderErrorKind.Configuration,
                    System.Net.HttpStatusCode.NotFound => ProviderErrorKind.ModelUnavailable,
                    (System.Net.HttpStatusCode)402 or (System.Net.HttpStatusCode)456 => ProviderErrorKind.Quota,
                    _ => ProviderErrorKind.Service
                };
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(1);
                    if (retry < TimeSpan.Zero) retry = TimeSpan.Zero;
                    throw new ProviderException(ProviderErrorKind.RateLimited, "请求限流，正在遵守服务冷却时间", retry);
                }
                throw ProviderHttp.Failure(kind);
            }
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + count > 4 * 1024 * 1024)
                    throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);
                buffer.Write(bytes, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray());
            var root = json.RootElement;
            if (!root.TryGetProperty("code", out var code) || !code.TryGetInt32(out var value) || value != 20000000)
            {
                int errCode = code.TryGetInt32(out var ec) ? ec : 0;
                var kind = errCode switch
                {
                    1000 or 1001 or 1002 or 1003 => ProviderErrorKind.Authentication,
                    1004 or 45000001 => ProviderErrorKind.Configuration,
                    1005 or 429 => ProviderErrorKind.RateLimited,
                    456 or 402 or 10013 => ProviderErrorKind.Quota,
                    _ => ProviderErrorKind.InvalidResponse
                };
                throw ProviderHttp.Failure(kind);
            }
            var list = root.GetProperty("data").GetProperty("translation_list");
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != 1)
                throw new JsonException();
            var translation = list[0].GetProperty("translation").GetString();
            if (string.IsNullOrWhiteSpace(translation))
                throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);
            return translation;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw ProviderHttp.Failure(ProviderErrorKind.Timeout); }
        catch (HttpRequestException) { throw ProviderHttp.Failure(ProviderErrorKind.Network); }
        catch (IOException) { throw ProviderHttp.Failure(ProviderErrorKind.Network); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse); }
    }
}

