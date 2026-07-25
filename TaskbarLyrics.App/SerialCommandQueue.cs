using System.Threading.Channels;

namespace TaskbarLyrics.App;

/// <summary>
/// Serializes short-lived commands owned by a UI service. The owner decides when to stop
/// accepting work and gets a bounded wait point during shutdown.
/// </summary>
internal sealed class SerialCommandQueue<TCommand> : IDisposable
{
    private readonly Channel<TCommand> _commands = Channel.CreateUnbounded<TCommand>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Func<TCommand, CancellationToken, Task> _executeAsync;
    private readonly Action<Exception> _onUnhandledException;
    private readonly Task _processTask;
    private int _isStopping;

    public SerialCommandQueue(
        Func<TCommand, CancellationToken, Task> executeAsync,
        Action<Exception> onUnhandledException)
    {
        _executeAsync = executeAsync;
        _onUnhandledException = onUnhandledException;
        _processTask = Task.Run(ProcessAsync);
    }

    public bool TryEnqueue(TCommand command)
    {
        return Volatile.Read(ref _isStopping) == 0 && _commands.Writer.TryWrite(command);
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        RequestStop();
        await _processTask.WaitAsync(timeout).ConfigureAwait(false);
    }

    public void Dispose()
    {
        RequestStop();
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await _executeAsync(command, _cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                    // Application shutdown intentionally cancels an in-flight command.
                }
                catch (Exception exception)
                {
                    _onUnhandledException(exception);
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // The queue has been stopped by its owner.
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private void RequestStop()
    {
        if (Interlocked.Exchange(ref _isStopping, 1) != 0)
        {
            return;
        }

        _commands.Writer.TryComplete();
        _cancellation.Cancel();
    }
}
