using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistentBackgroundRejectsMouseActivationWhileLockedOrPlaybackDisabled(bool locked)
    {
        RunSta(() =>
        {
            var owner = CreateOwner(layered: true);
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }), () => null);
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                var bounds = FloatingLyricsPanelController.GetBounds(owner);
                controller.Register(owner, () => display, () => true, (_, _) => { },
                    coverBounds: () => new Rect(bounds.Left + 8, bounds.Top + 4, 34, 34));
                controller.ShowBackground = true;
                controller.Enabled = true;
                if (locked) controller.ToggleLock(owner);
                else controller.ShowPlaybackControls = false;
                PumpMessages();
                var material = ReadFloatingBackground(controller);
                var compact = ReadFloatingBackgroundBounds(material);
                for (var click = 0; click < 3; click++)
                {
                    // USER32 asks the clicked background whether a client-area press may activate it.
                    var result = SendMaterialInputMessage(material.Handle, 0x0021, material.Handle, new IntPtr(0x02010001));
                    Assert.Equal(new IntPtr(3), result); // MA_NOACTIVATE
                    Assert.True(IsAboveLayerWindow(new WindowInteropHelper(owner).Handle, material.Handle));
                }
                Assert.Same(material, ReadFloatingBackground(controller));
                Assert.True(IsFloatingBackgroundVisible(material));
                Assert.Equal(compact, ReadFloatingBackgroundBounds(material));
                Assert.Equal(locked, controller.IsLocked(owner));
                Assert.Null(controller.Panel);
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    [Fact]
    public void PersistentBackgroundRemainsWhenThereIsNoRoomForPlaybackControls()
    {
        RunSta(() =>
        {
            var owner = CreateOwner(layered: true);
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }), () => null);
            try
            {
                var bounds = FloatingLyricsPanelController.GetBounds(owner);
                var work = new NativeRect((int)bounds.Left, (int)bounds.Top, (int)bounds.Right, (int)bounds.Bottom);
                var display = new DisplayMonitor("compact-only", "Compact", true, work, work, 1);
                controller.Register(owner, () => display, () => true, (_, _) => { });
                controller.ShowBackground = true;
                controller.Enabled = true;
                PumpMessages();
                var material = ReadFloatingBackground(controller);
                Assert.True(IsFloatingBackgroundVisible(material));
                Assert.Equal(bounds, ReadFloatingBackgroundBounds(material));
                controller.Open(display.Id, owner);
                Assert.Null(controller.Panel);
                Assert.Empty(controller.Panels);
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistentBackgroundContractsAfterClosingAndFollowsContentVisibility(bool playback)
    {
        RunSta(() =>
        {
            var owner = CreateOwner(layered: true);
            var contentVisible = true;
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }), () => null);
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                controller.Register(owner, () => display, () => contentVisible, (_, _) => { });
                controller.ShowPlaybackControls = playback;
                controller.ShowBackground = true;
                controller.Enabled = true;
                PumpMessages();
                Assert.Null(controller.Panel);
                var material = ReadFloatingBackground(controller);
                Assert.True(IsFloatingBackgroundVisible(material));
                var compact = ReadFloatingBackgroundBounds(material);
                Assert.Equal(FloatingLyricsPanelController.GetBounds(owner), compact);

                controller.Open(display.Id, owner);
                Assert.True(controller.Panel!.IsPanelVisible);
                Assert.True(ReadFloatingBackgroundBounds(material).Height > compact.Height);
                controller.Close();
                PumpMessages();
                Assert.Null(controller.Panel);
                Assert.True(IsFloatingBackgroundVisible(material));
                Assert.Equal(compact, ReadFloatingBackgroundBounds(material));
                Assert.Equal(compact, FloatingLyricsPanelController.GetBounds(owner));

                contentVisible = false;
                PumpMessages();
                Assert.Empty(controller.Panels);
                contentVisible = true;
                PumpMessages();
                material = ReadFloatingBackground(controller);
                Assert.True(IsFloatingBackgroundVisible(material));
                controller.ShowBackground = false;
                material = ReadFloatingBackground(controller);
                Assert.False(IsFloatingBackgroundVisible(material));
                controller.ShowBackground = true;
                material = ReadFloatingBackground(controller);
                Assert.True(IsFloatingBackgroundVisible(material));
                owner.Hide();
                PumpMessages();
                Assert.Empty(controller.Panels);
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistentBackgroundKeepsItsMaterialWhileLockedOrPlaybackDisabled(bool locked)
    {
        RunSta(() =>
        {
            using var receiver = new ReceiverWindow();
            var owner = CreateOwner(layered: true);
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }), () => null);
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                var bounds = FloatingLyricsPanelController.GetBounds(owner);
                var cover = new Rect(bounds.Left + 8, bounds.Top + 4, 34, 34);
                var lyricsPoint = Point(bounds.Right - 40, bounds.Top + 20);
                controller.Register(owner, () => display, () => true, (_, _) => { }, coverBounds: () => cover);
                controller.ShowBackground = true;
                controller.Enabled = true;
                PumpMessages();
                var material = ReadFloatingBackground(controller);
                if (locked) controller.ToggleLock(owner);
                else controller.ShowPlaybackControls = false;
                PumpMessages();
                Assert.Same(material, ReadFloatingBackground(controller));
                Assert.True(IsFloatingBackgroundVisible(material));
                Assert.Equal(material is FloatingMaterialWindow ? material.Handle : receiver.Handle, WindowFromPoint(lyricsPoint));
                Assert.Null(controller.Panel);

                controller.ShowBackground = false;
                PumpMessages();
                Assert.Equal(receiver.Handle, WindowFromPoint(lyricsPoint));
                controller.ShowBackground = true;
                PumpMessages();
                material = ReadFloatingBackground(controller);
                Assert.True(IsFloatingBackgroundVisible(material));
                Assert.Equal(material is FloatingMaterialWindow ? material.Handle : receiver.Handle, WindowFromPoint(lyricsPoint));
                if (locked) controller.ToggleLock(owner);
                else controller.ShowPlaybackControls = true;
                controller.Open(display.Id, owner);
                Assert.True(controller.Panel!.IsPanelVisible);
                Assert.Equal(new WindowInteropHelper(owner).Handle, WindowFromPoint(lyricsPoint));
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    private static IFloatingPanelMaterial ReadFloatingBackground(FloatingLyricsPanelController controller)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var targets = (System.Collections.IEnumerable)typeof(FloatingLyricsPanelController).GetField("_targets", flags)!.GetValue(controller)!;
        var target = Assert.Single(targets.Cast<object>());
        return Assert.IsAssignableFrom<IFloatingPanelMaterial>(target.GetType().GetField("Material", flags)!.GetValue(target));
    }

    private static bool IsFloatingBackgroundVisible(IFloatingPanelMaterial material) => material is FloatingMaterialFallbackWindow fallback
        ? ((System.Windows.Controls.Grid)fallback.Content).Children[0].Opacity > 0.9
        : IsWindowVisible(material.Handle);

    private static Rect ReadFloatingBackgroundBounds(IFloatingPanelMaterial material)
    {
        if (material is FloatingMaterialFallbackWindow fallback)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var surface = (FrameworkElement)typeof(FloatingMaterialFallbackWindow).GetField("_surface", flags)!.GetValue(fallback)!;
            return NativeWindowInteraction.GetScreenBounds(surface);
        }
        Assert.True(GetMaterialWindowRect(material.Handle, out var bounds));
        return new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMaterialInputMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
