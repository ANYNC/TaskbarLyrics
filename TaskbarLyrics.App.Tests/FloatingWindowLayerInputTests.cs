using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(FloatingWindowLayer.Desktop, false)]
    [InlineData(FloatingWindowLayer.Desktop, true)]
    [InlineData(FloatingWindowLayer.Window, false)]
    [InlineData(FloatingWindowLayer.Fullscreen, false)]
    public void FloatingLayersKeepControlsAndMaterialWithTheirHost(FloatingWindowLayer layer, bool showingDesktop)
    {
        RunSta(() =>
        {
            using var receiver = new ReceiverWindow();
            const uint flags = TaskbarNativeMethods.SWP_NOMOVE | TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOACTIVATE;
            Assert.True(TaskbarNativeMethods.SetWindowPos(receiver.Handle, TaskbarNativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags));
            var owner = CreateOwner(layered: true);
            var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
            var probe = new LayerTestProbe(receiver.Handle, display.Bounds, showingDesktop);
            var nativeTopmost = layer == FloatingWindowLayer.Fullscreen || showingDesktop;
            using var topmost = new SmartTopmostController(owner, probe);
            using var controls = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }),
                () => (new System.Windows.Point(-10000, -10000), IntPtr.Zero));
            try
            {
                controls.Register(owner, () => display, () => true, (_, _) => { },
                    coverBounds: () => new Rect(FloatingLyricsPanelController.GetBounds(owner).TopLeft,
                        new System.Windows.Size(34 * display.PixelsPerDip, 34 * display.PixelsPerDip)));
                controls.RegisterSizeAdjustment(owner, () => new FloatingWindowSize(420, 100), _ => { }, _ => { });
                controls.Enabled = true;
                controls.Open(display.Id, owner);
                // Opening/focusing controls may raise owned HWNDs. The next policy application
                // must place the whole card back under the application in Desktop/Window modes.
                topmost.ApplySettings(true, layer, display.Bounds);
                topmost.Reevaluate();
                PumpMessages(TimeSpan.FromMilliseconds(120));
                var panel = Assert.Single(controls.Panels);
                Assert.Equal(nativeTopmost, owner.Topmost);
                Assert.Equal(owner.Topmost, panel.Topmost);
                var hostHandle = new WindowInteropHelper(owner).Handle;
                var panelHandle = new WindowInteropHelper(panel).Handle;
                Assert.True(IsAboveLayerWindow(panelHandle, hostHandle));
                const BindingFlags binding = BindingFlags.Instance | BindingFlags.NonPublic;
                var targets = (System.Collections.IEnumerable)typeof(FloatingLyricsPanelController).GetField("_targets", binding)!.GetValue(controls)!;
                var target = Assert.Single(targets.Cast<object>());
                var material = (IFloatingPanelMaterial)target.GetType().GetField("Material", binding)!.GetValue(target)!;
                Assert.True(IsAboveLayerWindow(hostHandle, material.Handle));
                if (nativeTopmost)
                    Assert.True(IsAboveLayerWindow(hostHandle, receiver.Handle));
                else
                {
                    Assert.True(IsAboveLayerWindow(receiver.Handle, panelHandle));
                    Assert.True(IsAboveLayerWindow(receiver.Handle, hostHandle));
                    Assert.True(IsAboveLayerWindow(receiver.Handle, material.Handle));
                }
                CheckCompanions();
                var bounds = FloatingLyricsPanelController.GetBounds(owner);
                Assert.Equal(nativeTopmost ? hostHandle : receiver.Handle,
                    WindowFromPoint(Point(bounds.Left + 30, bounds.Top + 20)));
                controls.ToggleLock(owner);
                Assert.True(controls.IsLocked(owner));
                topmost.Reevaluate();
                PumpMessages(TimeSpan.FromMilliseconds(120));
                CheckCompanions();
                var before = probe.ReadCount;
                topmost.ApplySettings(false, layer, display.Bounds);
                topmost.Reevaluate();
                Assert.Equal(before, probe.ReadCount);

                void CheckCompanions()
                {
                    foreach (Window companion in owner.OwnedWindows)
                    {
                        if (!companion.IsVisible) continue;
                        Assert.Equal(owner.Topmost, companion.Topmost);
                        var handle = new WindowInteropHelper(companion).Handle;
                        Assert.Equal(nativeTopmost, (TaskbarNativeMethods.GetWindowLongPtr(handle, TaskbarNativeMethods.GWL_EXSTYLE).ToInt64() & 8) != 0);
                        if (!nativeTopmost) Assert.True(IsAboveLayerWindow(receiver.Handle, handle));
                    }
                }
            }
            finally { controls.Enabled = false; owner.Close(); }
        });
    }

    private static bool IsAboveLayerWindow(IntPtr above, IntPtr below)
    {
        for (var current = below; current != IntPtr.Zero; current = GetLayerWindow(current, 3))
            if (current == above) return true;
        return false;
    }

    private sealed class LayerTestProbe(IntPtr application, NativeRect bounds, bool showingDesktop) : ISmartTopmostWindowProbe
    {
        internal int ReadCount { get; private set; }
        public bool TryGetForegroundWindow(out SmartTopmostForegroundWindow foregroundWindow)
        {
            ReadCount++;
            foregroundWindow = new SmartTopmostForegroundWindow(application, bounds);
            return true;
        }
        public SmartTopmostDecision GetDesktopPlacement()
        {
            ReadCount++;
            return new SmartTopmostDecision(showingDesktop, showingDesktop ? IntPtr.Zero : application, Reassert: true);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindow")]
    private static extern IntPtr GetLayerWindow(IntPtr window, uint command);
}
