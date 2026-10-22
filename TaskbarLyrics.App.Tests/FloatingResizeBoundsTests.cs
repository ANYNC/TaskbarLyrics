using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    private static readonly double[] ResizePanelWidths = [200, 500, 180, 420];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatingMaterialResizeNeverExposesAnIntermediateBottom(bool shadowOnly)
    {
        RunSta(() =>
        {
            var owner = CreateOwner(layered: true);
            using var material = new FloatingMaterialFallbackWindow(shadowOnly);
            try
            {
                var handle = new WindowInteropHelper(owner).Handle;
                var scale = TaskbarNativeMethods.GetDpiForWindow(handle) / 96.0;
                var initial = new Rect(-30000, -30000, 500, 250);
                material.Position(initial, scale, handle);
                PumpMessages();
                using var trace = new ResizeBoundsTrace(material.Handle);
                var padding = Math.Round(36 * scale);
                foreach (var (width, height) in new[] { (460d, 210d), (420d, 170d), (550d, 290d) })
                {
                    trace.Bounds.Clear();
                    var card = new Rect(initial.Left, initial.Bottom - height, width, height);
                    material.Position(card, scale, handle);
                    PumpMessages();
                    var expected = new Rect(card.Left - padding, card.Top - padding, width + padding * 2, height + padding * 2);
                    Assert.NotEmpty(trace.Bounds);
                    Assert.All(trace.Bounds, actual => Assert.Equal(expected, actual));
                    Assert.Equal(expected, ReadResizeBounds(material.Handle));
                }
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public void FloatingBackdropAndItsShadowResizeWithoutIntermediateBounds()
    {
        RunSta(() =>
        {
            var owner = CreateOwner(layered: true);
            using var compositor = new FloatingMaterialCompositor();
            using var material = new FloatingMaterialWindow(compositor);
            try
            {
                var handle = new WindowInteropHelper(owner).Handle;
                var scale = TaskbarNativeMethods.GetDpiForWindow(handle) / 96.0;
                var initial = new Rect(-30000, -30000, 500, 250);
                material.Position(initial, scale, handle);
                PumpMessages();
                var shadow = (FloatingMaterialFallbackWindow)typeof(FloatingMaterialWindow)
                    .GetField("_shadowWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(material)!;
                using var materialTrace = new ResizeBoundsTrace(material.Handle);
                using var shadowTrace = new ResizeBoundsTrace(shadow.Handle);
                var padding = Math.Round(36 * scale);
                foreach (var (width, height) in new[] { (460d, 210d), (420d, 170d), (550d, 290d) })
                {
                    materialTrace.Bounds.Clear();
                    shadowTrace.Bounds.Clear();
                    var card = new Rect(initial.Left, initial.Bottom - height, width, height);
                    material.Position(card, scale, handle);
                    PumpMessages();
                    Assert.NotEmpty(materialTrace.Bounds);
                    Assert.NotEmpty(shadowTrace.Bounds);
                    Assert.All(materialTrace.Bounds, actual => Assert.Equal(card, actual));
                    var expectedShadow = new Rect(card.Left - padding, card.Top - padding, width + padding * 2, height + padding * 2);
                    Assert.All(shadowTrace.Bounds, actual => Assert.Equal(expectedShadow, actual));
                }
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public void FloatingPlaybackResizeKeepsItsBottomAcrossTheNarrowLayoutThreshold()
    {
        RunSta(() =>
        {
            var owner = CreateOwner(layered: true);
            var panel = new FloatingLyricsControlWindow(_ => { }, _ => { });
            try
            {
                var scale = TaskbarNativeMethods.GetDpiForWindow(new WindowInteropHelper(owner).Handle) / 96.0;
                const double bottom = -29000;
                Rect Placement(double width) => new(-30000, bottom - FloatingLyricsPanelPlacement.GetHeight(width) * scale,
                    Math.Round(width * scale), FloatingLyricsPanelPlacement.GetHeight(width) * scale);
                panel.Prepare(owner, new(Placement(420), false), scale);
                PumpMessages();
                var handle = new WindowInteropHelper(panel).Handle;
                using var trace = new ResizeBoundsTrace(handle);
                foreach (var width in ResizePanelWidths)
                {
                    trace.Bounds.Clear();
                    var placement = Placement(width);
                    panel.Prepare(owner, new(placement, false), scale);
                    PumpMessages();
                    Assert.NotEmpty(trace.Bounds);
                    Assert.All(trace.Bounds, actual => Assert.Equal(placement, actual));
                    Assert.Equal(width, panel.ActualWidth, 6);
                }
            }
            finally { panel.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FloatingLyricsResizeAppliesTheBottomAnchorBeforeChangingNativeSize(bool mirrorHost, bool onScreenBottom)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TaskbarLyrics.Core.Services.TrackLyricOffsetStore();
            var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
            Window host = mirrorHost
                ? new LyricsMirrorWindow(display, (_, _, _, _, _, _) => { }, composition)
                : new MainWindow(offsets, composition, () => { }, () => { });
            var settings = new AppSettings { UseFloatingWindow = true, FloatingWindowWidth = 420 };
            void Apply(Rect? anchor = null)
            {
                if (host is LyricsMirrorWindow mirror)
                    mirror.ApplySettings(settings, keepControlPanelOpen: true, preserveFloatingPosition: true, floatingResizeAnchor: anchor);
                else
                    ((MainWindow)host).ApplySettings(settings, keepControlPanelOpen: true, preserveFloatingPosition: true, floatingResizeAnchor: anchor);
            }
            try
            {
                if (host is MainWindow main) main.SetDisplayMonitor(display);
                Apply();
                var handle = new WindowInteropHelper(host).EnsureHandle();
                var scale = TaskbarNativeMethods.GetDpiForWindow(handle) / 96.0;
                var original = ReadResizeBounds(handle);
                var anchor = new Rect(display.WorkArea.Left + 100,
                    onScreenBottom ? display.Bounds.Bottom - original.Height : display.WorkArea.Top + 400, original.Width, original.Height);
                Assert.True(TaskbarNativeMethods.SetWindowPos(handle, IntPtr.Zero, (int)anchor.Left, (int)anchor.Top,
                    (int)anchor.Width, (int)anchor.Height, TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE));
                using var trace = new ResizeBoundsTrace(handle);
                foreach (var (width, zoom) in new[] { (520d, 150d), (360d, 75d), (600d, 200d), (420d, 100d) })
                {
                    trace.Bounds.Clear();
                    settings.FloatingWindowWidth = width;
                    settings.FloatingLyricsLayoutScalePercent = zoom;
                    Apply(anchor);
                    host.UpdateLayout();
                    var actual = ReadResizeBounds(handle);
                    var metrics = LyricsLayoutMetrics.Create(settings, scale);
                    var expected = FloatingWindowPlacement.Clamp(new Rect(anchor.Left,
                        anchor.Bottom - Math.Round(metrics.DesiredWindowHeight * scale), Math.Round(width * scale),
                        Math.Round(metrics.DesiredWindowHeight * scale)), display.Bounds);
                    Assert.NotEmpty(trace.Bounds);
                    Assert.All(trace.Bounds, observed => Assert.Equal(expected, observed));
                    Assert.Equal(expected, actual);
                }
            }
            finally { host.Close(); }
        });
    }

    private static Rect ReadResizeBounds(IntPtr handle)
    {
        Assert.True(GetMaterialWindowRect(handle, out var bounds));
        return new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    private sealed class ResizeBoundsTrace : IDisposable
    {
        private readonly HwndSource _source;
        internal List<Rect> Bounds { get; } = [];

        internal ResizeBoundsTrace(IntPtr handle)
        {
            _source = HwndSource.FromHwnd(handle);
            _source.AddHook(Observe);
        }

        private IntPtr Observe(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Final-rectangle assertions miss the transient move caused by separate WPF setters.
            if (message == 0x0047 && GetMaterialWindowRect(hwnd, out var bounds))
                Bounds.Add(new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
            return IntPtr.Zero;
        }

        public void Dispose() => _source.RemoveHook(Observe);
    }
}
