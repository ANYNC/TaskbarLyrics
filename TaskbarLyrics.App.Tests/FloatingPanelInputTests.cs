using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[CollectionDefinition("FloatingPanelInput", DisableParallelization = true)]
public sealed class FloatingPanelInputTestGroup;

[Collection("FloatingPanelInput")]
public sealed class FloatingPanelInputTests
{
    [Fact]
    public void MediaButtonFocusHintFollowsKeyboardAndMouseInputWithoutClearingFocus()
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            var panel = new FloatingLyricsControlWindow(_ => { }, _ => { });
            try
            {
                var lyrics = FloatingLyricsPanelController.GetBounds(owner);
                var scale = TaskbarNativeMethods.GetDpiForWindow(new WindowInteropHelper(owner).Handle) / 96.0;
                panel.Prepare(owner, new FloatingLyricsPanelPlacement(
                    new Rect(lyrics.Left, lyrics.Bottom, lyrics.Width, 104 * scale), false), scale);
                panel.Update(new TaskbarLyrics.Core.Models.TrackInfo("test", "Title", "Artist", "", "test", TimeSpan.FromMinutes(3)),
                    true, TimeSpan.Zero, true, new PlayerSourceBadge("test", "", false), false);
                panel.SetPanelVisible(true, immediate: true);
                var play = (System.Windows.Controls.Button)panel.FindName("PlayButton");
                panel.Activate();
                Assert.True(play.Focus());
                PumpMessages();
                var surface = (System.Windows.Controls.Border)play.Template.FindName("ButtonSurface", play);
                Assert.Equal(new Thickness(0), surface.BorderThickness);

                panel.FocusControls();
                PumpMessages();
                Assert.True(play.IsKeyboardFocused);
                Assert.Equal(new Thickness(1), surface.BorderThickness);

                play.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseDownEvent });
                PumpMessages();
                Assert.True(play.IsKeyboardFocused);
                Assert.Equal(new Thickness(0), surface.BorderThickness);

                play.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(panel)!, 0, Key.Space)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                PumpMessages();
                Assert.Equal(new Thickness(1), surface.BorderThickness);
                panel.SetPanelVisible(false, immediate: true);
                panel.SetPanelVisible(true, immediate: true);
                Assert.True(play.Focus());
                PumpMessages();
                Assert.Equal(new Thickness(0), surface.BorderThickness);
            }
            finally { panel.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenCardsAndVisibleShadowMarginsDoNotBlockAnotherThreadsWindow(bool composition)
    {
        RunSta(() =>
        {
            using var receiver = new ReceiverWindow();
            var owner = CreateOwner();
            try
            {
                var ownerHandle = new WindowInteropHelper(owner).Handle;
                var scale = TaskbarNativeMethods.GetDpiForWindow(ownerHandle) / 96.0;
                var lyrics = FloatingLyricsPanelController.GetBounds(owner);
                var card = new Rect(lyrics.Left, lyrics.Top, lyrics.Width, 148 * scale);
                using var context = composition ? new FloatingMaterialCompositor() : null;
                using IFloatingPanelMaterial material = composition
                    ? new FloatingMaterialWindow(context!) : new FloatingMaterialFallbackWindow();
                material.Configure(false, 0.45, 0.55, 24);
                material.Position(card, scale, ownerHandle);
                Assert.True(material.EnsureBelow(ownerHandle));
                var content = Point(card.Left + card.Width / 2, card.Bottom - 30 * scale);
                var margin = Point(card.Left + card.Width / 2, card.Bottom + 8 * scale);
                var corner = Point(card.Left, card.Bottom - 1);

                material.SetVisible(false, immediate: true);
                PumpMessages();
                Assert.Equal(receiver.Handle, WindowFromPoint(content));
                material.SetVisible(true, immediate: true);
                PumpMessages();
                Assert.Equal(receiver.Handle, WindowFromPoint(margin));
                Assert.Equal(receiver.Handle, WindowFromPoint(corner));
                Assert.Equal(composition ? material.Handle : receiver.Handle, WindowFromPoint(content));

                material.SetVisible(false);
                PumpMessages(TimeSpan.FromMilliseconds(240));
                Assert.Equal(receiver.Handle, WindowFromPoint(content));
                material.SetVisible(true);
                material.SetVisible(false);
                material.SetVisible(true);
                PumpMessages(TimeSpan.FromMilliseconds(240));
                Assert.True(IsWindowVisible(material.Handle));
                Assert.Equal(composition ? material.Handle : receiver.Handle, WindowFromPoint(content));
                Assert.Equal(receiver.Handle, WindowFromPoint(margin));
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public void ControlsReceiveInputOnlyWhileVisibleIncludingTheSpaceBetweenButtons()
    {
        RunSta(() =>
        {
            using var receiver = new ReceiverWindow();
            var owner = CreateOwner();
            var panel = new FloatingLyricsControlWindow(_ => { }, _ => { });
            try
            {
                var lyrics = FloatingLyricsPanelController.GetBounds(owner);
                var scale = TaskbarNativeMethods.GetDpiForWindow(new WindowInteropHelper(owner).Handle) / 96.0;
                var bounds = new Rect(lyrics.Left, lyrics.Bottom, lyrics.Width, 104 * scale);
                panel.Prepare(owner, new FloatingLyricsPanelPlacement(bounds, false), scale);
                var blank = Point(bounds.Left + 2 * scale, bounds.Top + 2 * scale);
                var button = (System.Windows.Controls.Button)panel.FindName("PlayButton");
                panel.SetPanelVisible(true, immediate: true);
                PumpMessages();
                var buttonCenter = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                var buttonPoint = Point(buttonCenter.X, buttonCenter.Y);
                var handle = new WindowInteropHelper(panel).Handle;
                Assert.Equal(handle, WindowFromPoint(blank));
                Assert.Equal(handle, WindowFromPoint(buttonPoint));

                panel.SetPanelVisible(false);
                Assert.Equal(receiver.Handle, WindowFromPoint(blank));
                Assert.Equal(receiver.Handle, WindowFromPoint(buttonPoint));
                PumpMessages(TimeSpan.FromMilliseconds(240));
                Assert.Equal(receiver.Handle, WindowFromPoint(blank));
                Assert.Equal(receiver.Handle, WindowFromPoint(buttonPoint));
                panel.SetPanelVisible(true, immediate: true);
                PumpMessages();
                Assert.Equal(handle, WindowFromPoint(blank));
                Assert.Equal(handle, WindowFromPoint(buttonPoint));
            }
            finally { panel.Close(); owner.Close(); }
        });
    }

    private static Window CreateOwner()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Left = 180,
            Top = 180,
            Width = 420,
            Height = 44,
            Background = Brushes.Black,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true
        };
        window.Show();
        return window;
    }

    private sealed class ReceiverWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly HwndSource _source;
        internal IntPtr Handle => _source.Handle;
        internal ReceiverWindow()
        {
            var ready = new TaskCompletionSource<HwndSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() =>
            {
                try
                {
                    var source = new HwndSource(new HwndSourceParameters("Input regression receiver")
                    {
                        PositionX = 100,
                        PositionY = 100,
                        Width = 1000,
                        Height = 700,
                        WindowStyle = unchecked((int)0x90000000),
                        ExtendedWindowStyle = 0x08000008
                    });
                    source.RootVisual = new System.Windows.Controls.Border { Background = Brushes.LightGray };
                    ready.SetResult(source);
                    Dispatcher.Run();
                }
                catch (Exception ex) { ready.TrySetException(ex); }
            })
            { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _source = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _source.Dispatcher.Invoke(() => { _source.Dispose(); Dispatcher.CurrentDispatcher.InvokeShutdown(); });
            Assert.True(_thread.Join(TimeSpan.FromSeconds(5)), "Receiver dispatcher did not stop.");
        }
    }

    private static TaskbarNativeMethods.NativePoint Point(double x, double y) => new((int)Math.Round(x), (int)Math.Round(y));

    private static void PumpMessages(TimeSpan? duration = null)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Input check did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(TaskbarNativeMethods.NativePoint point);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
}
