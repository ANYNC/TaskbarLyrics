using System.Net.Http;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricifyProxyRecoveryTests
{
    [Fact]
    public async Task HttpRequestExceptionRefreshesProxyAndRetriesWithNewTask()
    {
        using var oldClient = new HttpClient();
        using var newClient = new HttpClient();
        var currentClient = oldClient;
        var refreshCount = 0;
        var attempts = 0;
        var recovery = new LyricifyProxyRecovery(
            () => Volatile.Read(ref currentClient),
            () =>
            {
                Interlocked.Increment(ref refreshCount);
                Volatile.Write(ref currentClient, newClient);
            });

        var result = await LyricifyTask.WaitWithProxyRecoveryAsync(
            () =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    return Task.FromException<int>(new HttpRequestException("proxy unavailable"));
                }

                return Task.FromResult(42);
            },
            recovery,
            CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(2, attempts);
        Assert.Equal(1, refreshCount);
        Assert.Same(newClient, currentClient);
    }

    [Fact]
    public async Task ConcurrentFailuresShareOneProxyRefresh()
    {
        const int operationCount = 8;
        using var oldClient = new HttpClient();
        using var newClient = new HttpClient();
        var currentClient = oldClient;
        var refreshCount = 0;
        var attempts = 0;
        var firstAttemptGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = new LyricifyProxyRecovery(
            () => Volatile.Read(ref currentClient),
            () =>
            {
                Interlocked.Increment(ref refreshCount);
                Volatile.Write(ref currentClient, newClient);
            });

        var operations = Enumerable.Range(0, operationCount)
            .Select(_ => LyricifyTask.WaitWithProxyRecoveryAsync(
                async () =>
                {
                    var attempt = Interlocked.Increment(ref attempts);
                    if (attempt <= operationCount)
                    {
                        await firstAttemptGate.Task;
                        throw new HttpRequestException("proxy unavailable");
                    }

                    return attempt;
                },
                recovery,
                CancellationToken.None))
            .ToArray();

        firstAttemptGate.SetResult();
        var results = await Task.WhenAll(operations);

        Assert.Equal(operationCount * 2, attempts);
        Assert.Equal(1, refreshCount);
        Assert.All(results, result => Assert.True(result > operationCount));
        Assert.Same(newClient, currentClient);
    }

    [Fact]
    public async Task NonHttpRequestExceptionDoesNotRefreshOrRetry()
    {
        using var client = new HttpClient();
        var refreshCount = 0;
        var attempts = 0;
        var expected = new InvalidOperationException("unexpected failure");
        var recovery = new LyricifyProxyRecovery(
            () => client,
            () => Interlocked.Increment(ref refreshCount));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await LyricifyTask.WaitWithProxyRecoveryAsync(
                () =>
                {
                    Interlocked.Increment(ref attempts);
                    return Task.FromException<int>(expected);
                },
                recovery,
                CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(1, attempts);
        Assert.Equal(0, refreshCount);
    }

    [Fact]
    public async Task CanceledTokenDoesNotRefreshOrRetry()
    {
        using var client = new HttpClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var refreshCount = 0;
        var attempts = 0;
        var recovery = new LyricifyProxyRecovery(
            () => client,
            () => Interlocked.Increment(ref refreshCount));

        var actual = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await LyricifyTask.WaitWithProxyRecoveryAsync(
                () =>
                {
                    Interlocked.Increment(ref attempts);
                    return Task.FromException<int>(new HttpRequestException("proxy unavailable"));
                },
                recovery,
                cancellation.Token));

        Assert.Equal("proxy unavailable", actual.Message);
        Assert.Equal(1, attempts);
        Assert.Equal(0, refreshCount);
    }

    [Fact]
    public async Task SecondHttpRequestExceptionIsPropagatedAfterOneRetry()
    {
        using var client = new HttpClient();
        var refreshCount = 0;
        var attempts = 0;
        var firstFailure = new HttpRequestException("first failure");
        var secondFailure = new HttpRequestException("second failure");
        var recovery = new LyricifyProxyRecovery(
            () => client,
            () => Interlocked.Increment(ref refreshCount));

        var actual = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await LyricifyTask.WaitWithProxyRecoveryAsync(
                () =>
                {
                    var attempt = Interlocked.Increment(ref attempts);
                    return Task.FromException<int>(attempt == 1 ? firstFailure : secondFailure);
                },
                recovery,
                CancellationToken.None));

        Assert.Same(secondFailure, actual);
        Assert.Equal(2, attempts);
        Assert.Equal(1, refreshCount);
    }
}
