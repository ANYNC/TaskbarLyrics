using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SerialCommandQueueTests
{
    [Fact]
    public async Task TryEnqueue_ProcessesCommandsInOrderWithoutOverlap()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionOrder = new List<int>();
        var activeCommandCount = 0;
        var maximumConcurrentCommandCount = 0;

        using var queue = new SerialCommandQueue<int>(
            async (command, cancellationToken) =>
            {
                var currentCount = Interlocked.Increment(ref activeCommandCount);
                maximumConcurrentCommandCount = Math.Max(maximumConcurrentCommandCount, currentCount);
                await Task.Delay(10, cancellationToken);
                executionOrder.Add(command);
                Interlocked.Decrement(ref activeCommandCount);

                if (executionOrder.Count == 3)
                {
                    completed.TrySetResult();
                }
            },
            exception => throw exception);

        Assert.True(queue.TryEnqueue(1));
        Assert.True(queue.TryEnqueue(2));
        Assert.True(queue.TryEnqueue(3));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await queue.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
        Assert.Equal(1, maximumConcurrentCommandCount);
    }

    [Fact]
    public async Task StopAsync_CancelsInFlightCommandAndRejectsNewCommands()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var queue = new SerialCommandQueue<int>(
            async (_, cancellationToken) =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                    throw;
                }
            },
            exception => throw exception);

        Assert.True(queue.TryEnqueue(1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await queue.StopAsync(TimeSpan.FromSeconds(1));

        Assert.True(canceled.Task.IsCompletedSuccessfully);
        Assert.False(queue.TryEnqueue(2));
    }
}
