using AntiDbt.Core;
sealed class CountingTranslator : ITranslationProvider
{
    public int Calls;
    public Task<string> TranslateAsync(TranslationRequest request,CancellationToken token) { Calls++; return Task.FromResult("译:"+request.Text); }
}
sealed class ControlledTranslator : ITranslationProvider
{
    private readonly List<TaskCompletionSource<string>> _requests=[];
    public Task<string> TranslateAsync(TranslationRequest request,CancellationToken token)
    { lock(_requests) { var t=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);_requests.Add(t);return t.Task; } }
    public void Complete(int index,string value) { lock(_requests)_requests[index].TrySetResult(value); }
    public void Fail(int index) { lock(_requests)_requests[index].TrySetException(new IOException("local failure")); }
    public async Task WaitForCountAsync(int count)
    { using var c=new CancellationTokenSource(TimeSpan.FromSeconds(3));for(;;){lock(_requests)if(_requests.Count>=count)return;await Task.Delay(5,c.Token);} }
}
