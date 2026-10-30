using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(-1400, -900, 1)]
    [InlineData(1900, 100, 1.25)]
    [InlineData(-1400, -900, 1.5)]
    [InlineData(1900, -900, 2)]
    public void SidebarAvoidsAndFallsBackWithoutChangingSettings(int left, int top, double scale)
    {
        RunSta(() =>
        {
            var parent = CreateTaskbarSpaceWindow();
            var child = CreateTaskbarSpaceWindow();
            try
            {
                var parentHandle = new WindowInteropHelper(parent).Handle;
                var childHandle = new WindowInteropHelper(child).Handle;
                Assert.True(TaskbarNativeMethods.SetWindowPos(parentHandle, IntPtr.Zero, left, top,
                    (int)(60 * scale), (int)(1000 * scale),
                    TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE));
                SetTaskbarSpaceParent(childHandle, parentHandle);
                var style = TaskbarNativeMethods.GetWindowLongPtr(childHandle, -16).ToInt64();
                TaskbarNativeMethods.SetWindowLongPtr(childHandle, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
                var clock = new TaskbarSpaceClock();
                TaskbarInterval[] occupied = [new(300, 1000)];
                using var provider = new TaskbarOccupiedSpaceProvider((_, _, _) => occupied.Select(interval =>
                    new TaskbarInterval(top + interval.Start * scale, top + interval.End * scale)).ToArray(), clock);
                using var anchor = new EmbeddedTaskbarAnchor(provider);
                var display = new DisplayMonitor("side", "side", false,
                    new NativeRect(left, top, left + 2000, top + 2000),
                    new NativeRect(left + 60, top, left + 2000, top + 2000), scale);
                var settings = new AppSettings
                {
                    UseFloatingWindow = false,
                    WindowWidth = 420,
                    YOffset = 40,
                    HorizontalAnchor = LyricsHorizontalAnchor.Right,
                    XOffset = 19
                };
                Assert.True(anchor.Position(childHandle, parentHandle, child, settings, display));
                AssertSidebarBounds(40, 420);
                Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(anchor.Position(childHandle, parentHandle, child, settings, display));
                AssertSidebarBounds(0, 292);

                Refresh([new(90, 300)]);
                AssertSidebarBounds(308, 420);

                Refresh([new(0, 1000)]);
                AssertSidebarBounds(40, 420);
                Assert.True(anchor.Position(childHandle, parentHandle, child, settings, display));
                AssertSidebarBounds(40, 420);

                Refresh([new(700, 1000)]);
                AssertSidebarBounds(40, 420);
                Assert.Equal(420, settings.WindowWidth);
                Assert.Equal(40, settings.YOffset);
                Assert.Equal(19, settings.XOffset);

                settings.YOffset = 500;
                Assert.True(anchor.Position(childHandle, parentHandle, child, settings, display));
                AssertSidebarBounds(272, 420);

                void Refresh(TaskbarInterval[] next)
                {
                    occupied = next;
                    clock.Advance();
                    provider.GetOccupiedBounds(parentHandle, FloatingLyricsPanelController.GetBounds(parent));
                    Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                    Assert.True(anchor.Position(childHandle, parentHandle, child, settings, display));
                }

                void AssertSidebarBounds(double expectedTop, double expectedHeight)
                {
                    var bounds = FloatingLyricsPanelController.GetBounds(child);
                    Assert.Equal(left, bounds.Left);
                    Assert.Equal(Math.Round(top + expectedTop * scale, MidpointRounding.AwayFromZero), bounds.Top);
                    Assert.Equal(60 * scale, bounds.Width);
                    Assert.Equal(Math.Round(expectedHeight * scale, MidpointRounding.AwayFromZero), bounds.Height);
                }
            }
            finally { child.Close(); parent.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeTaskbarReaderProjectsControlsOntoTheLongAxis(bool vertical)
    {
        RunSta(() =>
        {
            var window = CreateTaskbarSpaceWindow();
            try
            {
                window.Left = 40;
                window.Top = 40;
                window.Width = vertical ? 60 : 900;
                window.Height = vertical ? 900 : 60;
                var canvas = new System.Windows.Controls.Canvas();
                var button = new System.Windows.Controls.Button
                {
                    Content = "Space probe",
                    Width = vertical ? 60 : 80,
                    Height = vertical ? 80 : 60
                };
                System.Windows.Controls.Canvas.SetLeft(button, vertical ? 0 : 100);
                System.Windows.Controls.Canvas.SetTop(button, vertical ? 100 : 0);
                canvas.Children.Add(button);
                window.Content = canvas;
                window.UpdateLayout();
                PumpMessages();
                var handle = new WindowInteropHelper(window).Handle;
                var bounds = FloatingLyricsPanelController.GetBounds(window);
                var origin = button.PointToScreen(new Point());
                var end = button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight));
                var read = Task.Run(() => TaskbarOccupiedSpaceProvider.ReadOccupiedBounds(handle, bounds, CancellationToken.None));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!read.IsCompleted && DateTime.UtcNow < deadline) PumpMessages();
                Assert.True(read.IsCompleted, "Native UIA query did not complete.");
                var intervals = read.GetAwaiter().GetResult();
                Assert.NotNull(intervals);
                Assert.Contains(intervals, interval =>
                    Math.Abs(interval.Start - (vertical ? origin.Y : origin.X)) < 1 &&
                    Math.Abs(interval.End - (vertical ? end.Y : end.X)) < 1);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmbeddedSpaceFallbackKeepsPanelPreparationAndNativeArea(bool useMirror)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TaskbarLyrics.Core.Services.TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
            var mirror = useMirror ? new LyricsMirrorWindow(display, (_, _, _, _, _, _) => { },
                composition) : null;
            var parent = new Window { Width = 1000, Height = 48, ShowInTaskbar = false, ShowActivated = false };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var panel = (LyricsControlPanelWindow)typeof(MainWindow).GetMethod("CreateControlPanelWindow", flags)!.Invoke(main, null)!;
            typeof(MainWindow).GetField("_controlPanelWindow", flags)!.SetValue(main, panel);
            try
            {
                parent.Show();
                var source = (Window?)mirror ?? main;
                var sourceHandle = new WindowInteropHelper(source).EnsureHandle();
                var parentHandle = new WindowInteropHelper(parent).Handle;
                var anchor = (EmbeddedTaskbarAnchor)source.GetType().GetField("_embeddedTaskbarAnchor", flags)!.GetValue(source)!;
                anchor.Detach();
                SetTaskbarSpaceParent(sourceHandle, parentHandle);
                var style = TaskbarNativeMethods.GetWindowLongPtr(sourceHandle, -16).ToInt64();
                TaskbarNativeMethods.SetWindowLongPtr(sourceHandle, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
                var expanded = true;
                typeof(MainWindow).GetField("_setControlPanelOpen", flags)!.SetValue(main, new Action<bool>(value => expanded = value));
                typeof(LyricsControlPanelWindow).GetProperty("IsOpening", flags)!.SetValue(panel, true);

                Assert.True(anchor.Position(sourceHandle, parentHandle, source,
                    new AppSettings { UseFloatingWindow = false }, display));
                Assert.True(panel.IsOpening);
                Assert.True(expanded);
                Assert.True(FloatingLyricsPanelController.GetBounds(source).Width > 0);
                anchor.Detach();
            }
            finally
            {
                mirror?.Close();
                panel.Close();
                main.Close();
                parent.Close();
            }
        });
    }

    [DllImport("user32.dll", EntryPoint = "SetParent")]
    private static extern IntPtr SetTaskbarSpaceParent(IntPtr child, IntPtr parent);

    private static Window CreateTaskbarSpaceWindow()
    {
        var window = new Window
        {
            Width = 420,
            Height = 44,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -10000,
            Top = -10000
        };
        window.Show();
        return window;
    }

    private sealed class TaskbarSpaceClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance() => _ticks += TimeSpan.FromMilliseconds(500).Ticks;
    }
}
