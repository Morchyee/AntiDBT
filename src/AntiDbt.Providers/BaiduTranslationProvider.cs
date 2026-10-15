using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AntiDbt.Core;

namespace AntiDbt.Providers;

public sealed class BaiduTranslationProvider(TranslationConfiguration configuration, HttpClient? testClient = null) : ITranslationProvider
{
    readonly ProviderHttp http = new(testClient);
    readonly object gate = new();
    DateTimeOffset coolingUntil;
    internal static string Sign(string appId, string text, string salt, string secret) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(appId + text + salt + secret)));

    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var c = configuration with { Provider = "BaiduTranslate" };
        if (ProviderLanguages.ValidateTranslation(c, request.SourceLanguage, request.TargetLanguage) != null)
            throw ProviderHttp.Failure(ProviderErrorKind.UnsupportedLanguage);
        string appId = c.Options.GetValueOrDefault("appId", "");
        ProviderHttp.Key(appId); ProviderHttp.Key(c.ApiKey);
        var endpoint = ProviderHttp.Endpoint(c.Endpoint, "fanyi-api.baidu.com");
        if (endpoint.IsLoopback && testClient == null || !endpoint.IsLoopback && (endpoint.AbsolutePath != "/api/trans/vip/translate" || !endpoint.IsDefaultPort))
            throw ProviderHttp.Failure(ProviderErrorKind.Configuration);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(c.TimeoutSeconds, 1, 120)));
        try
        {
            TimeSpan delay; lock (gate) delay = coolingUntil - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, deadline.Token);
            using var json = await http.JsonAsync(() =>
            {
                string salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
                return new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(new Dictionary<string,string>
                {
                    ["q"] = request.Text, ["from"] = ProviderLanguages.MtSourceCode(c, request.SourceLanguage),
                    ["to"] = ProviderLanguages.MtTargetCode(c, request.TargetLanguage), ["appid"] = appId,
                    ["salt"] = salt, ["sign"] = Sign(appId, request.Text, salt, c.ApiKey)
                }) };
            }, c.TimeoutSeconds, deadline.Token);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);
            if (root.TryGetProperty("error_code", out var error))
            {
                string code = error.ToString();
                if (code is "54003" or "54005")
                {
                    var retry = TimeSpan.FromSeconds(3); lock (gate) coolingUntil = DateTimeOffset.UtcNow + retry;
                    throw new ProviderException(ProviderErrorKind.RateLimited, "服务限流，请等待冷却后重试", retry);
                }
                throw ProviderHttp.Failure(code switch
                {
                    "52001" => ProviderErrorKind.Timeout, "52002" => ProviderErrorKind.Service,
                    "52003" or "54001" or "58000" or "58002" or "90107" => ProviderErrorKind.Authentication,
                    "54000" => ProviderErrorKind.Configuration, "54004" => ProviderErrorKind.Quota,
                    "58001" => ProviderErrorKind.UnsupportedLanguage, _ => ProviderErrorKind.Service
                });
            }
            if (!root.TryGetProperty("trans_result", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);
            var segments = new List<string>();
            foreach (var item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("dst", out var dst) || dst.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dst.GetString()))
                    throw ProviderHttp.Failure(ProviderErrorKind.InvalidResponse);
                segments.Add(dst.GetString()!);
            }
            return string.Join('\n', segments);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw ProviderHttp.Failure(ProviderErrorKind.Timeout); }
    }
}
