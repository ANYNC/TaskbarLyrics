using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void TaskbarObserverFiltersOtherWindowsAndNonGeometryEvents()
    {
        RunSta(() =>
        {
            var api = new FakeTaskbarEvents();
            var changes = 0;
            using var observer = new TaskbarChangeObserver(() => changes++, () => { }, api);
            Assert.True(observer.UpdateTarget(new IntPtr(10)));
            api.Raise(new IntPtr(10), 0x800B);
            api.Raise(new IntPtr(20), 0x8004);
            api.Raise(new IntPtr(30), 0x800B);
            api.Raise(new IntPtr(10), 0x800C);
            Assert.Equal(2, changes);
            for (var count = 0; count < 100; count++) Assert.True(observer.UpdateTarget(new IntPtr(10)));
            Assert.Single(api.Subscriptions);
        });
    }

    [Fact]
    public void TaskbarObserverRebindsAReusedHandleWithNewOwnerAndRejectsOldCallbacks()
    {
        RunSta(() =>
        {
            var api = new FakeTaskbarEvents();
            var changes = 0;
            var invalidations = 0;
            using var observer = new TaskbarChangeObserver(() => changes++, () => invalidations++, api);
            observer.UpdateTarget(new IntPtr(10));
            var oldHook = api.Hook;
            api.Owner++;
            observer.UpdateTarget(new IntPtr(10));
            Assert.Equal(2, api.Subscriptions.Count);
            Assert.Equal(1, invalidations);
            Assert.Equal(oldHook, Assert.Single(api.Removed));
            api.Raise(new IntPtr(10), 0x800B, oldHook);
            Assert.Equal(0, changes);
            api.Raise(new IntPtr(10), 0x800B);
            Assert.Equal(1, changes);
            observer.Detach();
            api.Raise(new IntPtr(10), 0x800B);
            Assert.Equal(1, changes);
            Assert.Equal(2, invalidations);
        });
    }

    [Fact]
    public void TaskbarObserverFailureRetriesAtLowFrequencyAndDisposesOnlyOnce()
    {
        RunSta(() =>
        {
            var api = new FakeTaskbarEvents { Fail = true };
            var clock = new TaskbarEventClock();
            using var observer = new TaskbarChangeObserver(() => { }, () => { }, api, clock);
            Assert.False(observer.UpdateTarget(new IntPtr(10)));
            for (var count = 0; count < 10; count++) Assert.False(observer.UpdateTarget(new IntPtr(10)));
            Assert.Single(api.Subscriptions);
            api.Fail = false;
            clock.Timestamp = 5 * TimeSpan.TicksPerSecond;
            Assert.True(observer.UpdateTarget(new IntPtr(10)));
            observer.Dispose();
            observer.Dispose();
            Assert.Single(api.Removed);
        });
    }

    private sealed class TaskbarEventClock : TimeProvider
    {
        public long Timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
    }

    [Fact]
    public void TaskbarObserverImmediatelyInvalidatesDestroyedRootBeforeSchedulingRefresh()
    {
        RunSta(() =>
        {
            var api = new FakeTaskbarEvents();
            var calls = new List<string>();
            using var observer = new TaskbarChangeObserver(() => calls.Add("refresh"), () => calls.Add("invalidate"), api);
            observer.UpdateTarget(new IntPtr(10));
            api.Raise(new IntPtr(10), 0x8001);
            Assert.Collection(calls,
                call => Assert.Equal("invalidate", call),
                call => Assert.Equal("refresh", call));
        });
    }

    private sealed class FakeTaskbarEvents : ITaskbarEventApi
    {
        public uint Owner = 424242;
        public bool Fail;
        public IntPtr Hook;
        public readonly List<uint> Subscriptions = [];
        public readonly List<IntPtr> Removed = [];
        private TaskbarWinEventCallback? _callback;
        public uint GetProcessId(IntPtr window) => Owner;
        public bool IsChild(IntPtr parent, IntPtr window) => window == new IntPtr(20);
        public IntPtr Subscribe(uint processId, TaskbarWinEventCallback callback)
        {
            Subscriptions.Add(processId);
            _callback = callback;
            Hook = Fail ? IntPtr.Zero : new IntPtr(Subscriptions.Count);
            return Hook;
        }
        public bool Unsubscribe(IntPtr hook) { Removed.Add(hook); return true; }
        public void Raise(IntPtr window, uint kind, IntPtr? hook = null) =>
            _callback?.Invoke(hook ?? Hook, kind, window, 0, 0, 1, 0);
    }
}
