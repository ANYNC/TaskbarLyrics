using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData("unchanged", true)]
    [InlineData("width", false)]
    [InlineData("appearance", false)]
    public void FloatingSettingsReplayPreservesHoverUnlessSettingsChange(string change, bool staysOpen)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TaskbarLyrics.Core.Services.TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            var owner = CreateOwner();
            var bounds = FloatingLyricsPanelController.GetBounds(owner);
            var handle = new WindowInteropHelper(owner).Handle;
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }),
                () => (new Point(bounds.Left + 20, bounds.Top + 20), handle));
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var field = typeof(MainWindow).GetField("_floatingControls", flags)!;
            ((FloatingLyricsPanelController)field.GetValue(main)!).Dispose();
            field.SetValue(main, controller);
            var settings = new AppSettings { UseFloatingWindow = true };
            typeof(MainWindow).GetField("_currentSettings", flags)!.SetValue(main, settings);
            typeof(MainWindow).GetField("_hasAppliedSettings", flags)!.SetValue(main, true);
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                controller.Register(owner, () => display, () => true, (_, _) => { });
                controller.Enabled = true;
                WaitForPollingState(() => controller.Panel is { IsPanelVisible: true });
                var panel = controller.Panel!;
                var next = settings.Clone();
                if (change == "width") next.FloatingWindowWidth = settings.FloatingWindowWidth + 40;
                if (change == "appearance") next.FloatingPanelBrightness = settings.FloatingPanelBrightness == 0.5 ? 0.6 : 0.5;
                for (var pass = 0; pass < 3; pass++)
                {
                    main.ApplySettings(next);
                    PumpMessages(TimeSpan.FromMilliseconds(100));
                    Assert.Equal(staysOpen, controller.Panel is { IsPanelVisible: true });
                    if (staysOpen) Assert.Same(panel, controller.Panel);
                }
            }
            finally { controller.Enabled = false; owner.Close(); main.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatingPollingUnlockResumesHoverWithoutPointerEvents(bool background)
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            var bounds = FloatingLyricsPanelController.GetBounds(owner);
            var handle = new WindowInteropHelper(owner).Handle;
            var pointer = new Point(bounds.Left + 20, bounds.Top + 20);
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }), () => (pointer, handle));
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                controller.Register(owner, () => display, () => true, (_, _) => { },
                    coverBounds: () => new Rect(bounds.Left, bounds.Top, 34, 34));
                controller.ShowBackground = background;
                controller.Enabled = true;
                controller.ToggleLock(owner);
                PumpMessages(TimeSpan.FromMilliseconds(350));
                Assert.Null(controller.Panel);
                controller.ToggleLock(owner);
                // The pointer stays fixed. No MouseMove/MouseLeave or manual Tick is raised.
                WaitForPollingState(() => controller.Panel is { IsPanelVisible: true });
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatingPollingClosesAndReopensWithoutPointerEvents(bool background)
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            var bounds = FloatingLyricsPanelController.GetBounds(owner);
            var handle = new WindowInteropHelper(owner).Handle;
            var inside = new Point(bounds.Left + bounds.Width / 2, bounds.Top + 20);
            (Point Position, IntPtr Root)? pointer = (inside, handle);
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }), () => pointer);
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                controller.Register(owner, () => display, () => true, (_, _) => { });
                controller.ShowBackground = background;
                controller.Enabled = true;
                WaitForPollingState(() => controller.Panel is { IsPanelVisible: true });
                var panel = controller.Panel!;
                // Moving between native lyric and panel windows remains inside the combined surface.
                var panelBounds = FloatingLyricsPanelController.GetBounds(panel);
                pointer = (new Point(panelBounds.Left + panelBounds.Width / 2, panelBounds.Top + 20),
                    new WindowInteropHelper(panel).Handle);
                PumpMessages(TimeSpan.FromMilliseconds(400));
                Assert.Same(panel, controller.Panel);

                pointer = (new Point(bounds.Right + 500, bounds.Bottom + 500), IntPtr.Zero);
                WaitForPollingState(() => controller.Panel is null);
                pointer = (inside, handle);
                WaitForPollingState(() => controller.Panel is { IsPanelVisible: true });
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    [Fact]
    public void FloatingPollingRetriesAfterPointerReadBecomesAvailable()
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            (Point Position, IntPtr Root)? pointer = null;
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }), () => pointer);
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                controller.Register(owner, () => display, () => true, (_, _) => { });
                controller.Enabled = true;
                PumpMessages(TimeSpan.FromMilliseconds(150));
                Assert.Null(controller.Panel);
                var bounds = FloatingLyricsPanelController.GetBounds(owner);
                pointer = (new Point(bounds.Left + 20, bounds.Top + 20), new WindowInteropHelper(owner).Handle);
                WaitForPollingState(() => controller.Panel is { IsPanelVisible: true });
                controller.Enabled = false;
                PumpMessages(TimeSpan.FromMilliseconds(350));
                Assert.Null(controller.Panel);
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    private static void WaitForPollingState(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(3))
            PumpMessages(TimeSpan.FromMilliseconds(50));
        Assert.True(condition(), "Floating hover state did not converge without mouse events.");
    }
}
