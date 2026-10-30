using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void DisplayRecoveryCoalescesNotificationsAndStopsAfterBoundedFollowUpPasses()
    {
        RunSta(() =>
        {
            var refreshes = 0;
            using var observer = new DisplayEnvironmentObserver(() => refreshes++);
            var source = GetDisplayObserverSource(observer);
            Assert.Equal(source.Handle, GetAncestor(source.Handle, 2)); // GA_ROOT
            Assert.False(IsWindowVisible(source.Handle));
            for (var index = 0; index < 100; index++)
                SendMessage(source.Handle, 0x007E, IntPtr.Zero, IntPtr.Zero);
            Assert.Equal(0, refreshes);

            PumpDisplayEventsUntil(() => refreshes == 8, TimeSpan.FromSeconds(6));
            PumpDisplayEventsFor(TimeSpan.FromMilliseconds(600));
            Assert.Equal(8, refreshes);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayRecoveryReceivesTaskbarRecreationAndResumeWithoutLyricsWindow(bool resume)
    {
        RunSta(() =>
        {
            var refreshes = 0;
            var observer = new DisplayEnvironmentObserver(() => refreshes++);
            try
            {
                var source = GetDisplayObserverSource(observer);
                var message = resume ? 0x0218 : (int)TaskbarNativeMethods.RegisterWindowMessage("TaskbarCreated");
                SendMessage(source.Handle, message, resume ? new IntPtr(0x0012) : IntPtr.Zero, IntPtr.Zero);
                PumpDisplayEventsUntil(() => refreshes == 1, TimeSpan.FromSeconds(2));
            }
            finally { observer.Dispose(); }
            observer.RequestRefresh();
            PumpDisplayEventsFor(TimeSpan.FromMilliseconds(600));
            Assert.Equal(1, refreshes);
        });
    }

    [Fact]
    public void DisplayRecoveryOnlyRefreshesForValidSessionDisplayOnNotifications()
    {
        RunSta(() =>
        {
            var refreshes = 0;
            using var observer = new DisplayEnvironmentObserver(() => refreshes++);
            var source = GetDisplayObserverSource(observer);
            // Registration also publishes the current power state. Let that initial
            // recovery finish before testing notifications that must be ignored.
            PumpDisplayEventsFor(TimeSpan.FromMilliseconds(50));
            var timer = (DispatcherTimer)typeof(DisplayEnvironmentObserver)
                .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(observer)!;
            PumpDisplayEventsUntil(() => !timer.IsEnabled, TimeSpan.FromSeconds(5));
            refreshes = 0;
            var data = Marshal.AllocHGlobal(24);
            try
            {
                var setting = new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
                Marshal.StructureToPtr(setting, data, false);
                Marshal.WriteInt32(data, 16, 4);
                for (var state = 0; state <= 2; state += 2)
                {
                    Marshal.WriteInt32(data, 20, state);
                    SendMessage(source.Handle, 0x0218, new IntPtr(0x8013), data);
                }
                Marshal.WriteInt32(data, 16, 0);
                Marshal.WriteInt32(data, 20, 1);
                SendMessage(source.Handle, 0x0218, new IntPtr(0x8013), data);
                PumpDisplayEventsFor(TimeSpan.FromMilliseconds(350));
                Assert.Equal(0, refreshes);

                Marshal.WriteInt32(data, 16, 4);
                SendMessage(source.Handle, 0x0218, new IntPtr(0x8013), data);
                PumpDisplayEventsUntil(() => refreshes == 1, TimeSpan.FromSeconds(2));
            }
            finally { Marshal.FreeHGlobal(data); }
        });
    }

    [Fact]
    public void DisplayRecoveryRetriesAfterATransientRefreshFailure()
    {
        RunSta(() =>
        {
            var attempts = 0;
            using var observer = new DisplayEnvironmentObserver(() =>
            {
                if (++attempts == 1) throw new InvalidOperationException("Display configuration still changing.");
            });
            observer.RequestRefresh();
            PumpDisplayEventsUntil(() => attempts == 2, TimeSpan.FromSeconds(3));
        });
    }

    private static HwndSource GetDisplayObserverSource(DisplayEnvironmentObserver observer) =>
        (HwndSource)typeof(DisplayEnvironmentObserver).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(observer)!;

    private static void PumpDisplayEventsUntil(Func<bool> ready, TimeSpan timeout)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!ready() && started.Elapsed < timeout) PumpDisplayEventsFor(TimeSpan.FromMilliseconds(20));
        Assert.True(ready(), "Display recovery did not complete in time.");
    }

    private static void PumpDisplayEventsFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
