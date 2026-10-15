using System.Net;
using System.Text.Json;
using AntiDbt.Core;

namespace AntiDbt.Providers;

internal sealed class ProviderHttp
{
    static readonly HttpClient Shared = new(new HttpClientHandler { AllowAutoRedirect = false });
    readonly HttpClient client;
    readonly object gate = new();
    DateTimeOffset coolingUntil;
    public ProviderHttp(HttpClient? testClient = null) => client = testClient ?? Shared;

    public async Task<JsonDocument> JsonAsync(Func<HttpRequestMessage> create, int timeoutSeconds, CancellationToken token, bool retryServerErrors = true, bool retryRateLimits = true,Func<HttpStatusCode,byte[],ProviderErrorKind>? errorClassifier=null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 120)));
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                for(;;)
                {
                    TimeSpan delay;lock(gate)delay=coolingUntil-DateTimeOffset.UtcNow;
                    if(delay<=TimeSpan.Zero)break;await Task.Delay(delay,deadline.Token);
                }
                using var request = create();
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                byte[] payload;
                using (var stream = await response.Content.ReadAsStreamAsync(deadline.Token))
                using (var buffer = new MemoryStream())
                {
                    var bytes = new byte[8192]; int count;
                    while ((count = await stream.ReadAsync(bytes, deadline.Token)) > 0)
                    {
                        if (buffer.Length + count > 4 * 1024 * 1024) throw Failure(ProviderErrorKind.InvalidResponse);
                        buffer.Write(bytes, 0, count);
                    }
                    payload = buffer.ToArray();
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if((errorClassifier??Classify)(response.StatusCode,payload)==ProviderErrorKind.Quota)throw Failure(ProviderErrorKind.Quota);
                    var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(1);
                    if (retry < TimeSpan.Zero) retry = TimeSpan.Zero;
                    lock (gate) {var candidate=DateTimeOffset.UtcNow+retry;if(candidate>coolingUntil)coolingUntil=candidate;}
                    if (attempt == 0 && retryRateLimits) continue;
                    throw new ProviderException(ProviderErrorKind.RateLimited, "请求限流，正在遵守服务冷却时间", retry);
                }
                if ((int)response.StatusCode >= 500 && retryServerErrors && attempt == 0)
                { await Task.Delay(300, deadline.Token); continue; }
                if (!response.IsSuccessStatusCode) throw Failure((errorClassifier??Classify)(response.StatusCode, payload));
                try { return JsonDocument.Parse(payload); }
                catch (JsonException) { throw Failure(ProviderErrorKind.InvalidResponse); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw Failure(ProviderErrorKind.Timeout); }
        catch (HttpRequestException) { throw Failure(ProviderErrorKind.Network); }
        catch (IOException) { throw Failure(ProviderErrorKind.Network); }
    }
    internal static ProviderErrorKind Classify(HttpStatusCode status, byte[] payload)
    {
        string code = "";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString() ?? "";
                if (error.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String) code = s.GetString() ?? code;
            }
        }
        catch (JsonException) { }
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return ProviderErrorKind.Authentication;
        if (code is "insufficient_quota" or "RESOURCE_EXHAUSTED" || (int)status is 456 or 402) return ProviderErrorKind.Quota;
        if (code is "model_not_found" or "model_decommissioned" || status == HttpStatusCode.NotFound) return ProviderErrorKind.ModelUnavailable;
        if (code is "language_not_supported" or "unsupported_language") return ProviderErrorKind.UnsupportedLanguage;
        if (status == HttpStatusCode.BadRequest) return ProviderErrorKind.Configuration;
        return ProviderErrorKind.Service;
    }
    public static ProviderException Failure(ProviderErrorKind kind) => new(kind, kind switch
    {
        ProviderErrorKind.Configuration => "服务配置无效，请检查字段和语言",
        ProviderErrorKind.Authentication => "认证失败，请检查密钥、区域与服务权限",
        ProviderErrorKind.Quota => "服务配额不足，请检查账单或额度",
        ProviderErrorKind.ModelUnavailable => "模型不可用，请刷新模型或检查权限",
        ProviderErrorKind.UnsupportedLanguage => "所选服务不支持当前语言",
        ProviderErrorKind.Timeout => "服务请求超时",
        ProviderErrorKind.Network => "网络连接失败",
        ProviderErrorKind.InvalidResponse => "服务返回空内容或响应格式错误",
        _ => "服务暂时不可用，请稍后重试"
    });
    public static Uri Endpoint(string value, params string[] hosts)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            (uri.Scheme != "https" && !(uri.IsLoopback && uri.Scheme == "http")) ||
            hosts.Length > 0 && !uri.IsLoopback && !hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw Failure(ProviderErrorKind.Configuration);
        return uri;
    }
    public static void Key(string value)
    { if (string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n')) throw Failure(ProviderErrorKind.Configuration); }
    public static string Language(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "";
        return language.Split('-','_')[0].ToLowerInvariant();
    }
}
