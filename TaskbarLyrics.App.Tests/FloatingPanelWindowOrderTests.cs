using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingPanelWindowOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepairsAnInterposedWindowWithoutMovingResizingOrActivatingTheMaterial(bool topmost)
    {
        RunSta(() =>
        {
            using var lyrics = new NativeTestWindow("lyrics", 100, topmost);
            using var material = new NativeTestWindow("material", 200, false);
            using var interposed = new NativeTestWindow("interposed", 300, topmost);
            Assert.True(TaskbarNativeMethods.SetWindowPos(lyrics.Handle,
                topmost ? TaskbarNativeMethods.HWND_TOPMOST : TaskbarNativeMethods.HWND_NOTOPMOST,
                0, 0, 0, 0, TaskbarNativeMethods.SWP_NOMOVE | TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOACTIVATE));
            PumpMessages();
            Assert.True(FloatingPanelWindowOrder.EnsureBelow(material.Handle, lyrics.Handle));
            Assert.True(TaskbarNativeMethods.SetWindowPos(interposed.Handle,
                topmost ? TaskbarNativeMethods.HWND_TOPMOST : TaskbarNativeMethods.HWND_NOTOPMOST,
                0, 0, 0, 0, TaskbarNativeMethods.SWP_NOMOVE | TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOACTIVATE));
            Assert.True(TaskbarNativeMethods.SetWindowPos(interposed.Handle, lyrics.Handle, 0, 0, 0, 0,
                TaskbarNativeMethods.SWP_NOMOVE | TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOACTIVATE));
            PumpMessages();
            Assert.Equal(interposed.Handle, GetWindow(lyrics.Handle, 2));
            Assert.True(GetWindowRect(material.Handle, out var before));
            var foreground = GetForegroundWindow();

            Assert.True(FloatingPanelWindowOrder.EnsureBelow(material.Handle, lyrics.Handle));
            PumpMessages();

            Assert.Equal(material.Handle, GetWindow(lyrics.Handle, 2));
            Assert.True(GetWindowRect(material.Handle, out var after));
            Assert.Equal((before.Left, before.Top, before.Right, before.Bottom), (after.Left, after.Top, after.Right, after.Bottom));
            Assert.Equal(foreground, GetForegroundWindow());
            var lyricsTopmost = (TaskbarNativeMethods.GetWindowLongPtr(lyrics.Handle, TaskbarNativeMethods.GWL_EXSTYLE).ToInt64() & 8) != 0;
            var materialTopmost = (TaskbarNativeMethods.GetWindowLongPtr(material.Handle, TaskbarNativeMethods.GWL_EXSTYLE).ToInt64() & 8) != 0;
            Assert.Equal(topmost, lyricsTopmost);
            Assert.Equal(lyricsTopmost, materialTopmost);
            Assert.True(FloatingPanelWindowOrder.EnsureBelow(material.Handle, lyrics.Handle));
            Assert.Equal(material.Handle, GetWindow(lyrics.Handle, 2));
        });
    }

    [Fact]
    public void RejectsMissingAndIdenticalHandles()
    {
        Assert.False(FloatingPanelWindowOrder.EnsureBelow(IntPtr.Zero, new IntPtr(1)));
        Assert.False(FloatingPanelWindowOrder.EnsureBelow(new IntPtr(1), IntPtr.Zero));
        Assert.False(FloatingPanelWindowOrder.EnsureBelow(new IntPtr(1), new IntPtr(1)));
    }

    private sealed class NativeTestWindow : IDisposable
    {
        private readonly Window _window;
        internal IntPtr Handle { get; }
        internal NativeTestWindow(string name, int size, bool topmost)
        {
            _window = new Window
            {
                Title = name,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Left = 40,
                Top = 40,
                Width = size,
                Height = size,
                Topmost = topmost
            };
            _window.Show();
            Handle = new WindowInteropHelper(_window).Handle;
        }
        public void Dispose() => _window.Close();
    }

    private static void PumpMessages()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { check(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The native window check did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out TaskbarNativeMethods.NativeRect bounds);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
