namespace AntiDbt.Core;

public enum ProviderErrorKind { Configuration, Authentication, RateLimited, Quota, ModelUnavailable, UnsupportedLanguage, Timeout, Network, InvalidResponse, Service }

// Messages are authored locally; never include server bodies, URLs containing keys or SDK diagnostics.
public sealed class ProviderException(ProviderErrorKind kind, string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public ProviderErrorKind Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
