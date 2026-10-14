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
        var source = NormalizeLanguage(request.SourceLanguage);
        var target = NormalizeLanguage(request.TargetLanguage);
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
                throw new InvalidOperationException($"翻译服务 HTTP 错误 ({(int)response.StatusCode})。");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + count > 4 * 1024 * 1024)
                    throw new InvalidOperationException("翻译服务响应过大。");
                buffer.Write(bytes, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray());
            var root = json.RootElement;
            if (!root.TryGetProperty("code", out var code) || !code.TryGetInt32(out var value) || value != 20000000)
                throw new InvalidOperationException("翻译服务返回业务错误。");
            var list = root.GetProperty("data").GetProperty("translation_list");
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != 1)
                throw new JsonException();
            return list[0].GetProperty("translation").GetString() ?? throw new JsonException();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new TimeoutException("翻译服务请求超时。"); }
        catch (HttpRequestException) { throw new InvalidOperationException("翻译服务连接失败。"); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException || e is InvalidOperationException && !e.Message.StartsWith("翻译服务")) { throw new InvalidOperationException("翻译服务响应格式无效。"); }
    }
    private static string NormalizeLanguage(string language) => language.Trim().ToLowerInvariant() switch
    {
        "" or "auto" => "",
        "en-us" or "en-gb" => "en",
        "zh-cn" or "zh-hans" or "zh-hans-cn" => "zh",
        "zh-tw" or "zh-hant" => "zh-Hant",
        _ => language.Trim()
    };
}

