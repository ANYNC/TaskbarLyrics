namespace TaskbarLyrics.App;

// One application-owned loop; closing a settings window never stops checks.
internal sealed class AutomaticUpdateScheduler(Func<CancellationToken, Task> check,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IDisposable
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private CancellationTokenSource? _cancellation;
    public void SetEnabled(bool enabled)
    {
        if (enabled == (_cancellation is not null)) return;
        if (!enabled) { Dispose(); return; }
        _cancellation = new CancellationTokenSource();
        TaskObserver.Observe(RunAsync(_cancellation.Token), "automatic update scheduler");
    }
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await _delay(TimeSpan.FromSeconds(5), token);
            while (!token.IsCancellationRequested)
            {
                await check(token);
                await _delay(TimeSpan.FromHours(24), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public void Dispose()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
