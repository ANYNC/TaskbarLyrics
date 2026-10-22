using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void RememberedTaskbarOverlapSurvivesReloadingAndRestoringTheNativeWindow()
    {
        RunSta(() =>
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, $"floating-overlap-{Guid.NewGuid():N}.json");
            var owner = CreateOwner(layered: true);
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                var store = new FloatingWindowPositionStore(path);
                var placement = new FloatingWindowPlacement(owner, store);
                placement.Configure(new AppSettings { UseFloatingWindow = true });
                placement.Refresh(display);
                var handle = new WindowInteropHelper(owner).Handle;
                var bounds = FloatingLyricsPanelController.GetBounds(owner);
                Assert.True(TaskbarNativeMethods.SetWindowPos(handle, IntPtr.Zero, display.Bounds.Left + 100,
                    display.Bounds.Bottom - (int)bounds.Height, 0, 0,
                    TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE));
                var expected = FloatingLyricsPanelController.GetBounds(owner);
                placement.RememberCurrentPosition();
                Assert.Equal(expected, FloatingLyricsPanelController.GetBounds(owner));
                var loaded = new FloatingWindowPositionStore(path);
                Assert.True(loaded.Get(display.Id)!.UseScreenBounds);
                Assert.True(TaskbarNativeMethods.SetWindowPos(handle, IntPtr.Zero, (int)expected.Left, (int)expected.Top - 200, 0, 0,
                    TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE));
                var restored = new FloatingWindowPlacement(owner, loaded);
                restored.Configure(new AppSettings { UseFloatingWindow = true });
                restored.Refresh(display);
                Assert.True(restored.TryRestore());
                Assert.Equal(expected, FloatingLyricsPanelController.GetBounds(owner));
            }
            finally { owner.Close(); System.IO.File.Delete(path); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizePreviewAtTheScreenBottomKeepsControlsAboveAndSupportsCancelOrCommit(bool canceled)
    {
        RunSta(() =>
        {
            var owner = CreateOwner(layered: true);
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }), () => null);
            try
            {
                var handle = new WindowInteropHelper(owner).Handle;
                var scale = TaskbarNativeMethods.GetDpiForWindow(handle) / 96.0;
                // Use a virtual off-screen monitor to exercise a taskbar overlap without covering the user's taskbar.
                var display = new DisplayMonitor("overlap", "overlap", false,
                    new NativeRect(-32000, -32000, -28000, -28000), new NativeRect(-32000, -32000, -28000, -28096), scale);
                Assert.True(TaskbarNativeMethods.SetWindowPos(handle, IntPtr.Zero, -31000,
                    display.Bounds.Bottom - (int)Math.Round(44 * scale), (int)Math.Round(420 * scale), (int)Math.Round(44 * scale),
                    TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE));
                PumpMessages();
                var original = FloatingLyricsPanelController.GetBounds(owner);
                var committed = false;
                controller.Register(owner, () => display, () => true, (_, _) => { });
                controller.RegisterSizeAdjustment(owner, () => new FloatingWindowSize(420, 100), (size, _) =>
                {
                    owner.Width = size.Width;
                    owner.Height = 44 * size.ScalePercent / 100;
                }, _ => committed = true);
                controller.Enabled = true;
                controller.Open(display.Id, owner);
                var panel = Assert.IsType<FloatingLyricsControlWindow>(controller.Panel);
                Assert.Equal(original.Top, FloatingLyricsPanelController.GetBounds(panel).Bottom);
                const BindingFlags binding = BindingFlags.Instance | BindingFlags.NonPublic;
                var targets = (System.Collections.IEnumerable)typeof(FloatingLyricsPanelController).GetField("_targets", binding)!.GetValue(controller)!;
                var target = Assert.Single(targets.Cast<object>());
                typeof(FloatingLyricsPanelController).GetMethod("BeginSizeAdjustment", binding)!
                    .Invoke(controller, [target, new Point(0, 0)]);
                typeof(FloatingLyricsPanelController).GetMethod("MoveSizeAdjustment", binding)!
                    .Invoke(controller, [target, new Point(50 * scale, -20 * scale)]);
                var preview = FloatingLyricsPanelController.GetBounds(owner);
                Assert.True(preview.Height > original.Height);
                Assert.Equal(original.Bottom, preview.Bottom);
                Assert.True(preview.Bottom > display.WorkArea.Bottom);
                Assert.Equal(preview.Top, FloatingLyricsPanelController.GetBounds(panel).Bottom);
                Assert.True(panel.IsPanelVisible);
                typeof(FloatingLyricsPanelController).GetMethod("FinishSizeAdjustment", binding)!
                    .Invoke(controller, [target, canceled]);
                var final = FloatingLyricsPanelController.GetBounds(owner);
                Assert.Equal(canceled ? original : preview, final);
                Assert.Equal(!canceled, committed);
                Assert.Equal(final.Top, FloatingLyricsPanelController.GetBounds(panel).Bottom);
                Assert.True(panel.IsPanelVisible);
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }
}
