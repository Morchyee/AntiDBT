using AntiDbt.Core;

namespace AntiDbt.Providers;

internal sealed class PassthroughTranslationProvider : ITranslationProvider
{
    public Task<string> TranslateAsync(TranslationRequest request,CancellationToken token)
    {token.ThrowIfCancellationRequested();return Task.FromResult(request.Text);}
}

internal sealed class ValidatedTranslationProvider(TranslationConfiguration configuration,ITranslationProvider inner):ITranslationProvider
{
    public Task<string> TranslateAsync(TranslationRequest request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(LanguageRegistry.Same(request.SourceLanguage,request.TargetLanguage))return Task.FromResult(request.Text);
        if(ProviderLanguages.ValidateTranslation(configuration,request.SourceLanguage,request.TargetLanguage) is { } reason)throw new ProviderException(ProviderErrorKind.UnsupportedLanguage,reason);
        return inner.TranslateAsync(request,token);
    }
}

// Only created by the settings factory after independent opt-in and cost consent.
// Each request gets at most one backup attempt. A short circuit prevents repeated
// primary failures on every partial while retaining normal scheduler cancellation.
internal sealed class FallbackTranslationProvider(ITranslationProvider primary,ITranslationProvider backup):ITranslationProvider
{
    readonly object gate=new();
    DateTimeOffset primaryRetryAt;
    public async Task<string> TranslateAsync(TranslationRequest request,CancellationToken token)
    {
        bool cooling;lock(gate)cooling=primaryRetryAt>DateTimeOffset.UtcNow;
        if(!cooling)
        {
            try{return await primary.TranslateAsync(request,token);}
            catch(ProviderException error) when(error.Kind is ProviderErrorKind.Network or ProviderErrorKind.Timeout or ProviderErrorKind.Service or ProviderErrorKind.RateLimited)
            {lock(gate)primaryRetryAt=DateTimeOffset.UtcNow+TimeSpan.FromSeconds(Math.Clamp(error.RetryAfter?.TotalSeconds??15,1,120));}
        }
        token.ThrowIfCancellationRequested();
        return await backup.TranslateAsync(request,token);
    }
}
