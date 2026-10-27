using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[CollectionDefinition("TaskbarOccupiedSpaceProvider", DisableParallelization = true)]
public sealed class TaskbarOccupiedSpaceProviderTestGroup;

// These tests deliberately block worker reads to model an unresponsive native
// provider; isolate them from other collections' short async deadlines.
[Collection("TaskbarOccupiedSpaceProvider")]
public sealed partial class TaskbarOccupiedSpaceProviderTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly IntPtr TaskbarHandle = new(42);
    private static readonly Rect TaskbarBounds = new(0, 0, 1000, 48);

    [Fact]
    public async Task InitialReadRunsAsynchronouslyAndPopulatesTheCache()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var clock = new ManualTimeProvider();
        var expected = new[] { new TaskbarInterval(120, 300) };
        var notifications = 0;
        IReadOnlyList<TaskbarInterval>? snapshotSeenByHandler = null;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, token) =>
        {
            entered.TrySetResult(true);
            release.Task.Wait(token);
            return expected;
        }, clock);
        provider.SnapshotAvailable += (_, _) =>
        {
            Interlocked.Increment(ref notifications);
            snapshotSeenByHandler = provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
        };

        var pending = Task.CompletedTask;
        try
        {
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await entered.Task.WaitAsync(WaitTimeout);
            Assert.False(pending.IsCompleted);

            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);

            Assert.Equal(1, Volatile.Read(ref notifications));
            AssertIntervals(expected, snapshotSeenByHandler);
            AssertIntervals(expected, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            Assert.Equal(1, Volatile.Read(ref notifications));
        }
        finally
        {
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
        }
    }

    [Fact]
    public async Task FreshCacheAndRefreshThrottleWorkWhenTheFirstTimestampIsZero()
    {
        var clock = new ManualTimeProvider();
        var first = new[] { new TaskbarInterval(100, 200) };
        var second = new[] { new TaskbarInterval(300, 400) };
        var reads = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) =>
        {
            return Interlocked.Increment(ref reads) switch
            {
                1 => first,
                2 => second,
                _ => [new TaskbarInterval(500, 600)]
            };
        }, clock);

        Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        AssertIntervals(first, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        Assert.Equal(1, Volatile.Read(ref reads));

        clock.Advance(TimeSpan.FromMilliseconds(499));
        AssertIntervals(first, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        Assert.Equal(1, Volatile.Read(ref reads));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        AssertIntervals(first, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.Equal(2, Volatile.Read(ref reads));
        AssertIntervals(second, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));

        clock.Advance(TimeSpan.FromMilliseconds(499));
        AssertIntervals(second, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        Assert.Equal(2, Volatile.Read(ref reads));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        AssertIntervals(second, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.Equal(3, Volatile.Read(ref reads));
    }

    [Fact]
    public async Task CoalescesRequestsWhileAReadIsInFlight()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var clock = new ManualTimeProvider();
        var reads = 0;
        var expected = new[] { new TaskbarInterval(150, 250) };
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, token) =>
        {
            Interlocked.Increment(ref reads);
            entered.TrySetResult(true);
            release.Task.Wait(token);
            return expected;
        }, clock);

        var pending = Task.CompletedTask;
        try
        {
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await entered.Task.WaitAsync(WaitTimeout);

            clock.Advance(TimeSpan.FromSeconds(1));
            for (var index = 0; index < 10; index++)
            {
                Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            }

            Assert.Equal(1, Volatile.Read(ref reads));
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
            Assert.Equal(1, Volatile.Read(ref reads));
        }
        finally
        {
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("com")]
    [InlineData("element")]
    public async Task ReadFailuresKeepTheLastSnapshotOnlyUntilItsTwoSecondExpiry(string failure)
    {
        var clock = new ManualTimeProvider();
        var reads = 0;
        var notifications = 0;
        var expected = new[] { new TaskbarInterval(200, 350) };
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                return expected;
            }

            return failure switch
            {
                "null" => null,
                "com" => ThrowInteropFailure(),
                "element" => throw new ElementNotAvailableException(),
                _ => throw new InvalidOperationException("Unknown test failure.")
            };
        }, clock);
        provider.SnapshotAvailable += (_, _) => Interlocked.Increment(ref notifications);

        Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.Equal(1, Volatile.Read(ref notifications));
        AssertIntervals(expected, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));

        clock.Advance(TimeSpan.FromMilliseconds(500));
        AssertIntervals(expected, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.Equal(1, Volatile.Read(ref notifications));

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        AssertIntervals(expected, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.Equal(1, Volatile.Read(ref notifications));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        Assert.Equal(3, Volatile.Read(ref reads));
        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task CacheAgeStartsWhenTheQueryStartsRatherThanWhenItCompletes()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var clock = new ManualTimeProvider();
        var reads = 0;
        var expected = new[] { new TaskbarInterval(100, 200) };
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                entered.TrySetResult(true);
                release.Task.Wait(CancellationToken.None);
                return expected;
            }

            return null;
        }, clock);

        var pending = Task.CompletedTask;
        try
        {
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await entered.Task.WaitAsync(WaitTimeout);

            clock.Advance(TimeSpan.FromMilliseconds(2100));
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);

            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await pending.WaitAsync(WaitTimeout);
            Assert.Equal(2, Volatile.Read(ref reads));
        }
        finally
        {
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task ChangingHandleBoundsOrOrientationInvalidatesCacheAndRejectsLateResults(bool changeHandle, bool rotate)
    {
        var oldRefreshEntered = NewSignal();
        var releaseOldRefresh = NewSignal();
        var clock = new ManualTimeProvider();
        var reads = 0;
        var cached = new[] { new TaskbarInterval(100, 200) };
        var lateOld = new[] { new TaskbarInterval(300, 400) };
        var current = new[] { new TaskbarInterval(500, 600) };
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, token) =>
        {
            var read = Interlocked.Increment(ref reads);
            if (read == 2)
            {
                oldRefreshEntered.TrySetResult(true);
                releaseOldRefresh.Task.Wait(token);
                return lateOld;
            }

            return read == 1 ? cached : current;
        }, clock);

        var pending = Task.CompletedTask;
        try
        {
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await pending.WaitAsync(WaitTimeout);
            AssertIntervals(cached, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));

            clock.Advance(TimeSpan.FromMilliseconds(500));
            AssertIntervals(cached, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await oldRefreshEntered.Task.WaitAsync(WaitTimeout);

            var changedHandle = changeHandle ? new IntPtr(43) : TaskbarHandle;
            var changedBounds = rotate ? new Rect(0, 0, 48, 1000)
                : changeHandle ? TaskbarBounds : new Rect(10, 0, 1000, 48);
            Assert.Null(provider.GetOccupiedBounds(changedHandle, changedBounds));

            releaseOldRefresh.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
            Assert.Null(provider.GetOccupiedBounds(changedHandle, changedBounds));
            pending = provider.PendingRead;
            await pending.WaitAsync(WaitTimeout);

            AssertIntervals(current, provider.GetOccupiedBounds(changedHandle, changedBounds));
            Assert.Equal(3, Volatile.Read(ref reads));
        }
        finally
        {
            releaseOldRefresh.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
        }
    }

    [Fact]
    public async Task DisposeCancelsReadsIgnoresLateResultsAndPreventsFurtherWork()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var clock = new ManualTimeProvider();
        var reads = 0;
        var notifications = 0;
        var readToken = CancellationToken.None;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, token) =>
        {
            Interlocked.Increment(ref reads);
            readToken = token;
            entered.TrySetResult(true);
            release.Task.Wait(CancellationToken.None);
            return [new TaskbarInterval(100, 200)];
        }, clock);
        provider.SnapshotAvailable += (_, _) => Interlocked.Increment(ref notifications);

        var pending = Task.CompletedTask;
        try
        {
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await entered.Task.WaitAsync(WaitTimeout);

            provider.Dispose();
            provider.Dispose();
            Assert.True(readToken.IsCancellationRequested);
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            Assert.Equal(1, Volatile.Read(ref reads));
            Assert.Equal(0, Volatile.Read(ref notifications));

            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);

            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            Assert.Equal(1, Volatile.Read(ref reads));
            Assert.Equal(0, Volatile.Read(ref notifications));
        }
        finally
        {
            release.TrySetResult(true);
            provider.Dispose();
            await pending.WaitAsync(WaitTimeout);
        }
    }

    [Fact]
    public async Task ReturningToAnEarlierTargetStillRejectsItsPreSwitchRead()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var clock = new ManualTimeProvider();
        var reads = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, token) =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                entered.TrySetResult(true);
                release.Task.Wait(token);
                return [new TaskbarInterval(100, 200)];
            }
            return [new TaskbarInterval(500, 600)];
        }, clock);
        var pending = Task.CompletedTask;
        try
        {
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
            pending = provider.PendingRead;
            await entered.Task.WaitAsync(WaitTimeout);
            provider.GetOccupiedBounds(new IntPtr(43), TaskbarBounds);
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await pending.WaitAsync(WaitTimeout);
            AssertIntervals([new TaskbarInterval(500, 600)], provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        }
        finally
        {
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
        }
    }

    [Fact]
    public void InvalidNativeTargetsDoNotQueueQueries()
    {
        var reads = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) =>
        {
            Interlocked.Increment(ref reads);
            return [];
        }, new ManualTimeProvider());
        Assert.Null(provider.GetOccupiedBounds(IntPtr.Zero, TaskbarBounds));
        foreach (var bounds in new[] { Rect.Empty, new Rect(0, 0, 0, 48), new Rect(0, 0, 100, 0),
                     new Rect(double.NaN, 0, 100, 48), new Rect(0, 0, double.PositiveInfinity, 48) })
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, bounds));
        Assert.True(provider.PendingRead.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref reads));
    }

    [Fact]
    public async Task DisposeCooperativelyCancelsAWaitingReaderWithoutFaultingItsTask()
    {
        var entered = NewSignal();
        var release = NewSignal();
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, token) =>
        {
            entered.TrySetResult(true);
            release.Task.Wait(token);
            return [];
        }, new ManualTimeProvider());
        var pending = Task.CompletedTask;
        try
        {
            provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds);
            pending = provider.PendingRead;
            await entered.Task.WaitAsync(WaitTimeout);
            provider.Dispose();
            await pending.WaitAsync(WaitTimeout);
            Assert.True(pending.IsCompletedSuccessfully);
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        }
        finally
        {
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
        }
    }

    [Fact]
    public async Task DetachInvalidatesWarmCacheAndAllowsImmediateSameTargetRead()
    {
        var clock = new ManualTimeProvider();
        var reads = 0;
        var cached = new[] { new TaskbarInterval(100, 200) };
        var refreshed = new[] { new TaskbarInterval(300, 400) };
        var provider = new TaskbarOccupiedSpaceProvider((_, _, _) =>
        {
            return Interlocked.Increment(ref reads) == 1 ? cached : refreshed;
        }, clock);
        using var anchor = new EmbeddedTaskbarAnchor(provider);

        Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        AssertIntervals(cached, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));

        anchor.Detach();
        Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);

        Assert.Equal(2, Volatile.Read(ref reads));
        AssertIntervals(refreshed, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
    }

    [Fact]
    public async Task DetachDuringReadRejectsLateBoundsAndWaitsForTheSingleInFlightRead()
    {
        var entered = NewSignal();
        var release = NewSignal();
        var clock = new ManualTimeProvider();
        var reads = 0;
        var cached = new[] { new TaskbarInterval(100, 200) };
        var late = new[] { new TaskbarInterval(300, 400) };
        var refreshed = new[] { new TaskbarInterval(500, 600) };
        var notifications = 0;
        var provider = new TaskbarOccupiedSpaceProvider((_, _, _) =>
        {
            var read = Interlocked.Increment(ref reads);
            if (read == 1)
            {
                return cached;
            }

            if (read == 2)
            {
                entered.TrySetResult(true);
                release.Task.Wait(CancellationToken.None);
                return late;
            }

            return refreshed;
        }, clock);
        provider.SnapshotAvailable += (_, _) => Interlocked.Increment(ref notifications);
        using var anchor = new EmbeddedTaskbarAnchor(provider);
        var pending = Task.CompletedTask;
        try
        {
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await pending.WaitAsync(WaitTimeout);
            Assert.Equal(1, Volatile.Read(ref notifications));
            AssertIntervals(cached, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));

            clock.Advance(TimeSpan.FromMilliseconds(500));
            AssertIntervals(cached, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await entered.Task.WaitAsync(WaitTimeout);

            anchor.Detach();
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            Assert.Equal(2, Volatile.Read(ref reads));
            Assert.Equal(1, Volatile.Read(ref notifications));

            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
            Assert.Equal(1, Volatile.Read(ref notifications));

            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await pending.WaitAsync(WaitTimeout);
            Assert.Equal(3, Volatile.Read(ref reads));
            Assert.Equal(2, Volatile.Read(ref notifications));
            AssertIntervals(refreshed, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        }
        finally
        {
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
        }
    }

    [Theory]
    [InlineData("zero-hwnd")]
    [InlineData("zero-width")]
    public async Task InvalidTargetInvalidatesWarmCacheAndForcesImmediateRead(string invalidTarget)
    {
        var clock = new ManualTimeProvider();
        var reads = 0;
        var cached = new[] { new TaskbarInterval(100, 200) };
        var refreshed = new[] { new TaskbarInterval(300, 400) };
        var notifications = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) =>
        {
            return Interlocked.Increment(ref reads) == 1 ? cached : refreshed;
        }, clock);
        provider.SnapshotAvailable += (_, _) => Interlocked.Increment(ref notifications);

        Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);
        Assert.Equal(1, Volatile.Read(ref notifications));
        AssertIntervals(cached, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));

        Assert.Null(GetInvalidTarget(provider, invalidTarget));
        Assert.Equal(1, Volatile.Read(ref reads));
        Assert.Equal(1, Volatile.Read(ref notifications));
        Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        await provider.PendingRead.WaitAsync(WaitTimeout);

        Assert.Equal(2, Volatile.Read(ref reads));
        Assert.Equal(2, Volatile.Read(ref notifications));
        AssertIntervals(refreshed, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
    }

    [Theory]
    [InlineData("zero-hwnd")]
    [InlineData("zero-width")]
    public async Task InvalidTargetDuringReadRejectsLateBoundsWithoutStartingParallelWork(string invalidTarget)
    {
        var entered = NewSignal();
        var release = NewSignal();
        var clock = new ManualTimeProvider();
        var reads = 0;
        var late = new[] { new TaskbarInterval(100, 200) };
        var refreshed = new[] { new TaskbarInterval(300, 400) };
        var notifications = 0;
        using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                entered.TrySetResult(true);
                release.Task.Wait(CancellationToken.None);
                return late;
            }

            return refreshed;
        }, clock);
        provider.SnapshotAvailable += (_, _) => Interlocked.Increment(ref notifications);

        var pending = Task.CompletedTask;
        try
        {
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await entered.Task.WaitAsync(WaitTimeout);

            Assert.Null(GetInvalidTarget(provider, invalidTarget));
            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            Assert.Equal(1, Volatile.Read(ref reads));
            Assert.Equal(0, Volatile.Read(ref notifications));

            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
            Assert.Equal(0, Volatile.Read(ref notifications));

            Assert.Null(provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
            pending = provider.PendingRead;
            await pending.WaitAsync(WaitTimeout);
            Assert.Equal(2, Volatile.Read(ref reads));
            Assert.Equal(1, Volatile.Read(ref notifications));
            AssertIntervals(refreshed, provider.GetOccupiedBounds(TaskbarHandle, TaskbarBounds));
        }
        finally
        {
            release.TrySetResult(true);
            await pending.WaitAsync(WaitTimeout);
        }
    }

    private static IReadOnlyList<TaskbarInterval>? GetInvalidTarget(
        TaskbarOccupiedSpaceProvider provider,
        string invalidTarget) =>
        invalidTarget switch
        {
            "zero-hwnd" => provider.GetOccupiedBounds(IntPtr.Zero, TaskbarBounds),
            "zero-width" => provider.GetOccupiedBounds(TaskbarHandle, new Rect(0, 0, 0, TaskbarBounds.Height)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidTarget))
        };

    [Theory]
    [InlineData(1, 0, 376, 368)]
    [InlineData(1.25, 0, 376, 366)]
    [InlineData(1.5, 0, 376, 364)]
    [InlineData(1.25, -2560, -2184, 366)]
    [InlineData(2, 0, 376, 360)]
    public void PlacementKeepsPhysicalSafetyGapAtDifferentDpiAndScreenOrigins(
        double scale, int taskbarLeft, int buttonLeft, int expectedWidth)
    {
        var occupied = TaskbarOccupiedSpaceProvider.ToLogicalBounds(
            [new TaskbarInterval(buttonLeft, taskbarLeft + 2560)], taskbarLeft, scale);

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(2560 / scale, 420, 0, occupied, out var placement));
        var native = EmbeddedTaskbarLayoutCalculator.ToTaskbarClientBounds(
            placement.Start, 0, placement.Length, 40, scale);

        Assert.Equal(expectedWidth, native.Width);
        Assert.True(taskbarLeft + native.Left + native.Width <= buttonLeft - (8 * scale));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void InvalidDpiFallsBackToPhysicalUnits(double scale)
    {
        var occupied = TaskbarOccupiedSpaceProvider.ToLogicalBounds(
            [new TaskbarInterval(-1600, -1200)], -1920, scale);

        Assert.Equal(new TaskbarInterval(320, 720), Assert.Single(occupied));
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AssertIntervals(
        TaskbarInterval[] expected,
        IReadOnlyList<TaskbarInterval>? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
    }

    private static TaskbarInterval[]? ThrowInteropFailure()
    {
        Marshal.ThrowExceptionForHR(unchecked((int)0x80004005));
        throw new InvalidOperationException("Expected E_FAIL to produce an interop exception.");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
    }
}
