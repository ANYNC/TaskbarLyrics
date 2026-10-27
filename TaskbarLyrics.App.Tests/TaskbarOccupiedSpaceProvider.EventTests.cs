using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class TaskbarOccupiedSpaceProviderTests
{
    [Fact]
    public async Task HealthyEventMonitoringKeepsCacheBetweenFiveSecondChecks()
    {
        var clock = new ManualTimeProvider();
        var reads = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) => { reads++; return [new(200, 400)]; }, clock);
        await WarmEventCache(provider, clock);
        var warmReads = reads;
        clock.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.NotNull(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        Assert.Equal(warmReads, reads);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.NotNull(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.Equal(warmReads + 1, reads);
    }

    [Fact]
    public async Task EventDoesNotRetroactivelyExpireAnIdleSnapshotAndStartsShortPolling()
    {
        var clock = new ManualTimeProvider();
        var reads = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) => { reads++; return [new(200, 400)]; }, clock);
        await WarmEventCache(provider, clock);
        clock.Advance(TimeSpan.FromSeconds(4));
        var beforeEvent = reads;
        provider.NotifyChanged();
        Assert.NotNull(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        Assert.Equal(beforeEvent, reads);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        for (var index = 0; index < 4; index++)
        {
            Assert.NotNull(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            await provider.PendingRead.WaitAsync(WaitTimeout);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(beforeEvent + 4, reads);
        Assert.NotNull(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        Assert.Equal(beforeEvent + 4, reads);
    }

    [Fact]
    public async Task FailedIdleCheckRetriesQuicklyAndExpiresWithinTwoSecondsWithoutRenewingGrace()
    {
        var clock = new ManualTimeProvider();
        var fail = false;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) => fail ? null : [new(200, 400)], clock);
        await WarmEventCache(provider, clock);
        fail = true;
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.NotNull(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.NotNull(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        clock.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
    }

    [Fact]
    public async Task NewOccupancyShrinksImmediatelyButExpansionRequiresMatchingReads()
    {
        var clock = new ManualTimeProvider();
        TaskbarInterval[] raw = [new(300, 1000)];
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) => raw, clock);
        await WarmEventCache(provider, clock);
        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1000, 200, 0,
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds)!, out _));
        raw = [new(100, 1000)];
        provider.NotifyChanged();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1000, 200, 0,
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds)!, out _));
        raw = [new(300, 1000)];
        provider.NotifyChanged();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1000, 200, 0,
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds)!, out _));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1000, 200, 0,
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds)!, out _));
    }

    [Fact]
    public async Task MissedEventDetectedByIdleCheckEntersConfirmationPolling()
    {
        var clock = new ManualTimeProvider();
        TaskbarInterval[] raw = [new(100, 1000)];
        var reads = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) => { reads++; return raw; }, clock);
        await WarmEventCache(provider, clock);
        raw = [new(300, 1000)];
        clock.Advance(TimeSpan.FromSeconds(5));
        provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
        await provider.PendingRead.WaitAsync(WaitTimeout);
        var firstCheck = reads;
        clock.Advance(TimeSpan.FromMilliseconds(500));
        provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.Equal(firstCheck + 1, reads);
        Assert.Equal(raw, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
    }

    [Fact]
    public async Task EventsDuringAReadRejectItsPreChangeResultAndDoNotQueueParallelQueries()
    {
        var clock = new ManualTimeProvider();
        var entered = NewSignal();
        var release = NewSignal();
        var reads = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, token) =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                entered.TrySetResult(true);
                release.Task.Wait(token);
                return [new(100, 200)];
            }
            return [new(300, 400)];
        }, clock);
        provider.SetEventMonitoring(true);
        try
        {
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
            await entered.Task.WaitAsync(WaitTimeout);
            for (var count = 0; count < 100; count++)
            {
                provider.NotifyChanged();
                Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            }
            Assert.Equal(1, reads);
            release.TrySetResult(true);
            await provider.PendingRead.WaitAsync(WaitTimeout);
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            clock.Advance(TimeSpan.FromMilliseconds(500));
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
            await provider.PendingRead.WaitAsync(WaitTimeout);
            Assert.Equal(2, reads);
            Assert.Equal(new[] { new TaskbarInterval(300, 400) }, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        }
        finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task LosingEventMonitoringRestoresPollingAndInvalidatesPreviousData()
    {
        var clock = new ManualTimeProvider();
        var reads = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) => { reads++; return [new(200, 400)]; }, clock);
        await WarmEventCache(provider, clock);
        var warmReads = reads;
        provider.SetEventMonitoring(false);
        Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.Equal(warmReads + 2, reads);
    }

    private static async Task WarmEventCache(TaskbarOccupiedSpaceProvider provider, ManualTimeProvider clock)
    {
        provider.SetEventMonitoring(true);
        provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
        await provider.PendingRead.WaitAsync(WaitTimeout);
        for (var count = 0; count < 3; count++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
            await provider.PendingRead.WaitAsync(WaitTimeout);
        }
    }

    [Fact]
    public async Task RacingEventCanShrinkKnownSpaceButCannotRenewItsExpiry()
    {
        var clock = new ManualTimeProvider();
        var entered = NewSignal();
        var release = NewSignal();
        var block = false;
        var fail = false;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, token) =>
        {
            if (fail) return null;
            if (block) { entered.TrySetResult(true); release.Task.Wait(token); return [new(100, 1000)]; }
            return [new(300, 1000)];
        }, clock);
        await WarmEventCache(provider, clock);
        clock.Advance(TimeSpan.FromSeconds(4));
        block = true;
        provider.NotifyChanged();
        clock.Advance(TimeSpan.FromMilliseconds(100));
        provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
        try
        {
            await entered.Task.WaitAsync(WaitTimeout);
            provider.NotifyChanged();
            release.TrySetResult(true);
            await provider.PendingRead.WaitAsync(WaitTimeout);
            Assert.False(TaskbarFreeSpaceLayout.TryPlace(1000, 200, 0,
                provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds)!, out _));
            fail = true;
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            await provider.PendingRead.WaitAsync(WaitTimeout);
        }
        finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task EventModeRejectsSlowResultsEvenThoughHealthyIdleCacheHasLongerLifetime()
    {
        var clock = new ManualTimeProvider();
        var entered = NewSignal();
        var release = NewSignal();
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, token) =>
        {
            entered.TrySetResult(true);
            release.Task.Wait(token);
            return [new(200, 400)];
        }, clock);
        provider.SetEventMonitoring(true);
        try
        {
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
            await entered.Task.WaitAsync(WaitTimeout);
            clock.Advance(TimeSpan.FromMilliseconds(2100));
            release.TrySetResult(true);
            await provider.PendingRead.WaitAsync(WaitTimeout);
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            await provider.PendingRead.WaitAsync(WaitTimeout);
            Assert.NotNull(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        }
        finally { release.TrySetResult(true); }
    }
}
