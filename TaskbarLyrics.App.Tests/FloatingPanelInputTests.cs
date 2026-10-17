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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledFloatingPlaybackPassesThroughCoverAndLyricsAndRestoresLockedInput(bool locked)
    {
        RunSta(() =>
        {
            using var receiver = new ReceiverWindow();
            var owner = CreateOwner(layered: true);
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }));
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                var bounds = FloatingLyricsPanelController.GetBounds(owner);
                var cover = new Rect(bounds.Left, bounds.Top, 34, 34);
                var coverPoint = Point(cover.Left + 10, cover.Top + 10);
                var lyricsPoint = Point(bounds.Right - 40, bounds.Top + 20);
                controller.Register(owner, () => display, () => true, (_, _) => { }, coverBounds: () => cover);
                controller.Enabled = true;
                if (locked) controller.ToggleLock(owner);
                controller.ShowPlaybackControls = false;
                PumpMessages();
                Assert.Equal(receiver.Handle, WindowFromPoint(coverPoint));
                Assert.Equal(receiver.Handle, WindowFromPoint(lyricsPoint));
                Assert.Equal(locked, controller.IsLocked(owner));
                Assert.Null(controller.Panel);
                controller.ShowPlaybackControls = true;
                PumpMessages();
                if (locked)
                {
                    Assert.IsType<FloatingCoverUnlockWindow>(HwndSource.FromHwnd(WindowFromPoint(coverPoint)).RootVisual);
                    Assert.Equal(receiver.Handle, WindowFromPoint(lyricsPoint));
                }
                else
                {
                    var handle = new WindowInteropHelper(owner).Handle;
                    Assert.Equal(handle, WindowFromPoint(coverPoint));
                    Assert.Equal(handle, WindowFromPoint(lyricsPoint));
                }
                controller.ShowPlaybackControls = false;
                controller.Enabled = false;
                Assert.Equal(new WindowInteropHelper(owner).Handle, WindowFromPoint(coverPoint));
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceClickDuringPanelExitDoesNotRestartItsOpeningAnimation(bool deactivateBeforeClick)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TaskbarLyrics.Core.Services.TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var panel = (LyricsControlPanelWindow)typeof(MainWindow).GetMethod("CreateControlPanelWindow", flags)!.Invoke(main, null)!;
            typeof(MainWindow).GetField("_controlPanelWindow", flags)!.SetValue(main, panel);
            try
            {
                panel.Left = 100;
                panel.Top = 100;
                panel.Show();
                typeof(LyricsControlPanelWindow).GetField("_isWebReady", flags)!.SetValue(panel, true);
                var display = DisplayMonitorService.GetDisplays()[0];
                // Deactivation starts the exit before the source WebView delivers its click.
                if (deactivateBeforeClick) panel.HidePanel();
                else main.ToggleControlPanelNear(new Rect(100, 100, 34, 34), display, _ => { });
                Assert.True(panel.IsClosing);
                var version = typeof(LyricsControlPanelWindow).GetField("_showVersion", flags)!.GetValue(panel);
                var expanded = false;
                for (var click = 0; click < 3; click++)
                    main.ToggleControlPanelNear(new Rect(100, 100, 34, 34), display, value => expanded = value);
                Assert.True(panel.IsClosing);
                Assert.Equal(version, typeof(LyricsControlPanelWindow).GetField("_showVersion", flags)!.GetValue(panel));
                Assert.False(expanded);
            }
            finally { panel.Close(); main.Close(); }
        });
    }

    [Fact]
    public void SourceClickCancelsPendingPanelPreparationAndClearsExpandedState()
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TaskbarLyrics.Core.Services.TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var panel = (LyricsControlPanelWindow)typeof(MainWindow).GetMethod("CreateControlPanelWindow", flags)!.Invoke(main, null)!;
            typeof(MainWindow).GetField("_controlPanelWindow", flags)!.SetValue(main, panel);
            try
            {
                // Cached WebView preparation can be awaiting scripts while its window is hidden.
                typeof(LyricsControlPanelWindow).GetProperty("IsOpening", flags)!.SetValue(panel, true);
                var expanded = true;
                typeof(MainWindow).GetField("_setControlPanelOpen", flags)!.SetValue(main, new Action<bool>(value => expanded = value));
                main.ToggleControlPanelNear(new Rect(100, 100, 34, 34), DisplayMonitorService.GetDisplays()[0], value => expanded = value);
                Assert.False(panel.IsOpening);
                Assert.False(panel.IsVisible);
                Assert.False(expanded);
            }
            finally { panel.Close(); main.Close(); }
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void SingleLineMainAndMirrorResizeArtworkInBothTaskbarSizeDirections(double dpi)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TaskbarLyrics.Core.Services.TrackLyricOffsetStore();
            DisplayMonitor Display(int height) => new("layout-test", "Layout", true,
                new NativeRect(0, 0, 1920, 1080), new NativeRect(0, 0, 1920, 1080 - (int)(height * dpi)), dpi);
            var compact = Display(32);
            var normal = Display(48);
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            var mirror = new LyricsMirrorWindow(compact, (_, _, _, _, _, _) => { }, composition);
            var settings = new AppSettings { SingleLineLyrics = true, LyricsLayoutScalePercent = 70 };
            var previous = TaskbarEmbeddingLayoutPolicy.FromDisplay(compact);
            try
            {
                foreach (var display in new[] { compact, normal, compact })
                {
                    var current = TaskbarEmbeddingLayoutPolicy.FromDisplay(display);
                    settings.LyricsLayoutScalePercent = TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, previous, current);
                    previous = current;
                    main.SetDisplayMonitor(display);
                    mirror.SetDisplayMonitor(display);
                    main.ApplySettings(settings);
                    mirror.ApplySettings(settings);
                    var metrics = LyricsLayoutMetrics.Create(settings, dpi,
                        new TaskbarBounds(0, 0, 1920 / dpi, current.MaxHeight));
                    foreach (var source in new Window[] { main, mirror })
                    {
                        // Hidden windows do not measure descendants; lay out their content directly.
                        var root = (FrameworkElement)source.FindName("RootBorder");
                        root.Measure(new Size(source.Width, source.Height));
                        root.Arrange(new Rect(0, 0, source.Width, source.Height));
                        root.UpdateLayout();
                        var cover = (FrameworkElement)source.FindName("CoverHitSurface");
                        Assert.Equal(metrics.CoverSize, cover.ActualWidth, 6);
                        Assert.Equal(metrics.CoverSize, cover.ActualHeight, 6);
                        Assert.Equal(metrics.DesiredWindowHeight, source.Height, 6);
                        Assert.Equal(display == normal ? 105 : 70, settings.LyricsLayoutScalePercent, 6);
                    }
                }
            }
            finally { mirror.Close(); main.Close(); }
        });
    }

    [Theory]
    [InlineData(false, false, 1, CoverPosition.Left)]
    [InlineData(false, true, 1.5, CoverPosition.Right)]
    [InlineData(true, false, 1, CoverPosition.Left)]
    [InlineData(true, true, 1.5, CoverPosition.Right)]
    public void CompactAndSidebarHostsKeepNativeCoverInsideContent(bool vertical, bool singleLine, double dpi, CoverPosition position)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TaskbarLyrics.Core.Services.TrackLyricOffsetStore();
            var display = new DisplayMonitor("layout-test", "Layout", true, new NativeRect(0, 0, 1920, 1080),
                vertical ? new NativeRect((int)(48 * dpi), 0, 1920, 1080)
                    : new NativeRect(0, 0, 1920, 1080 - (int)(32 * dpi)), dpi);
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            var mirror = new LyricsMirrorWindow(display, (_, _, _, _, _, _) => { }, composition);
            var settings = new AppSettings { SingleLineLyrics = singleLine, CoverPosition = position, ControlPanelTriggerLyrics = true };
            try
            {
                main.SetDisplayMonitor(display);
                var floating = settings.Clone();
                floating.UseFloatingWindow = true;
                main.ApplySettings(floating);
                mirror.ApplySettings(floating);
                main.ApplySettings(settings);
                mirror.ApplySettings(settings);
                foreach (var source in new Window[] { main, mirror })
                {
                    source.Measure(new Size(source.Width, source.Height));
                    source.Arrange(new Rect(0, 0, source.Width, source.Height));
                    source.UpdateLayout();
                    var content = (FrameworkElement)source.FindName("LyricsContentRoot");
                    var cover = (FrameworkElement)source.FindName("CoverHitSurface");
                    var origin = cover.TranslatePoint(new Point(), content);
                    Assert.InRange(origin.X, -0.01, content.ActualWidth);
                    Assert.InRange(origin.Y, -0.01, content.ActualHeight);
                    Assert.True(origin.X + cover.ActualWidth <= content.ActualWidth + 0.01);
                    Assert.True(origin.Y + cover.ActualHeight <= content.ActualHeight + 0.01);
                    Assert.Equal(vertical ? 48 : 32, vertical ? source.Width : source.Height);
                    var lyrics = (FrameworkElement)source.FindName("LyricsHitSurface");
                    if (vertical)
                    {
                        Assert.Equal(display.Bounds.Top / dpi, source.Top);
                        Assert.Equal(position == CoverPosition.Right ? VerticalAlignment.Bottom : VerticalAlignment.Top, cover.VerticalAlignment);
                        Assert.Equal(position == CoverPosition.Left ? cover.Height + settings.CoverGap : 0, lyrics.Margin.Top);
                    }
                }
            }
            finally { mirror.Close(); main.Close(); }
        });
    }

    [Theory]
    [InlineData(CoverPosition.Left, 100)]
    [InlineData(CoverPosition.Right, 100)]
    [InlineData(CoverPosition.Left, 150)]
    [InlineData(CoverPosition.Right, 150)]
    public void EmbeddedPanelUsesSourceCoverForEveryTriggerAndFallsBackWhenCoverIsHidden(CoverPosition position, double scale)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TaskbarLyrics.Core.Services.TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
            var mirror = new LyricsMirrorWindow(display, (_, _, _, _, _, _) => { }, composition);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            main.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("OnLoaded", flags)!.CreateDelegate(typeof(RoutedEventHandler), main);
            mirror.Loaded -= (RoutedEventHandler)typeof(LyricsMirrorWindow).GetMethod("OnLoaded", flags)!.CreateDelegate(typeof(RoutedEventHandler), mirror);
            var settings = new AppSettings
            {
                CoverPosition = position,
                LyricsLayoutScalePercent = scale,
                ShowCover = true,
                ControlPanelTriggerLyrics = true
            };
            try
            {
                main.SetDisplayMonitor(display);
                main.ApplySettings(settings);
                mirror.ApplySettings(settings);
                main.Show();
                mirror.Show();
                PumpMessages();
                foreach (var source in new Window[] { main, mirror })
                {
                    var cover = (FrameworkElement)source.FindName("CoverHitSurface");
                    var expected = NativeWindowInteraction.GetScreenBounds(cover);
                    Assert.False(expected.IsEmpty);
                    var lyricsTrigger = new Rect(expected.Left + 60, expected.Top - 5, 200, expected.Height + 10);
                    Assert.Equal(expected, main.GetControlPanelAnchor(source, expected));
                    Assert.Equal(expected, main.GetControlPanelAnchor(source, lyricsTrigger));
                    cover.Visibility = Visibility.Collapsed;
                    source.UpdateLayout();
                    Assert.Equal(lyricsTrigger, main.GetControlPanelAnchor(source, lyricsTrigger));
                }
            }
            finally { mirror.Close(); main.Close(); }
        });
    }

    [Theory]
    [InlineData(CoverPosition.Left, true, 100)]
    [InlineData(CoverPosition.Right, true, 125)]
    [InlineData(CoverPosition.Left, false, 150)]
    public void EmbeddedLyricsReceiveNativePointerInputOnlyWhileTheirTriggerIsEnabled(CoverPosition coverPosition, bool showCover, double scale)
    {
        RunSta(() =>
        {
            using var receiver = new ReceiverWindow();
            var owner = CreateOwner(layered: true);
            owner.Background = Brushes.Transparent;
            var surface = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
            };
            owner.Content = surface;
            var settings = new AppSettings
            {
                ControlPanelTriggerLyrics = true,
                CoverPosition = coverPosition,
                ShowCover = showCover,
                LyricsLayoutScalePercent = scale
            };
            var metrics = LyricsLayoutMetrics.Create(settings);
            try
            {
                void Apply() { LyricsControlPanelInputSurface.Apply(surface, settings, metrics); PumpMessages(); }
                Apply();
                var handle = new WindowInteropHelper(owner).Handle;
                var center = surface.PointToScreen(new Point(surface.ActualWidth / 2, surface.ActualHeight / 2));
                var pointer = Point(center.X, center.Y);
                Assert.Equal(handle, WindowFromPoint(pointer));
                var coverInset = metrics.CoverSize + metrics.CoverGap;
                Assert.Equal(showCover && coverPosition == CoverPosition.Left ? coverInset : 0, surface.Margin.Left);
                Assert.Equal(showCover && coverPosition == CoverPosition.Right ? coverInset : 0, surface.Margin.Right);
                if (showCover)
                {
                    var omittedCover = owner.PointToScreen(new Point(coverPosition == CoverPosition.Left ? 10 : owner.ActualWidth - 10, 20));
                    Assert.Equal(receiver.Handle, WindowFromPoint(Point(omittedCover.X, omittedCover.Y)));
                }
                settings.ControlPanelTriggerLyrics = false;
                Apply();
                Assert.Equal(receiver.Handle, WindowFromPoint(pointer));
                settings.ControlPanelTriggerLyrics = true;
                settings.EnableControlPanel = false;
                Apply();
                Assert.Equal(receiver.Handle, WindowFromPoint(pointer));
                settings.EnableControlPanel = true;
                settings.UseFloatingWindow = true;
                Apply();
                Assert.Equal(receiver.Handle, WindowFromPoint(pointer));
                settings.UseFloatingWindow = false;
                Apply();
                Assert.Equal(handle, WindowFromPoint(pointer));
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public void HidingPlaybackKeepsFloatingDragAndResizeAvailable()
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }));
            var dragged = false;
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                controller.Register(owner, () => display, () => true, (_, _) => { }, drag: () => dragged = true);
                controller.RegisterSizeAdjustment(owner, () => new FloatingWindowSize(420, 100), _ => { }, _ => { });
                controller.ShowPlaybackControls = false;
                controller.Enabled = true;
                controller.Open(display.Id, owner);
                var panel = Assert.IsType<FloatingLyricsControlWindow>(controller.Panel);
                Assert.Equal(56, panel.Height);
                Assert.Equal(Visibility.Collapsed, ((UIElement)panel.FindName("Actions")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((UIElement)panel.FindName("SeekSlider")).Visibility);
                Assert.True(((UIElement)panel.FindName("DragGrip")).IsVisible);
                panel.RequestDrag();
                Assert.True(dragged);
                var bounds = FloatingLyricsPanelController.GetBounds(owner);
                Assert.IsType<FloatingResizeGripWindow>(HwndSource.FromHwnd(WindowFromPoint(
                    Point(bounds.Right - 10, bounds.Top + 10))).RootVisual);
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingFloatingPlaybackStopsHoverAndReenablingResumesWithoutMovingPointer(bool initiallyEnabled)
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            var bounds = FloatingLyricsPanelController.GetBounds(owner);
            var pointer = new System.Windows.Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }),
                readPointer: () => (pointer, new WindowInteropHelper(owner).Handle));
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                var cover = new Rect(bounds.Left, bounds.Top, 34, 34);
                controller.Register(owner, () => display, () => true, (_, _) => { }, coverBounds: () => cover);
                controller.ShowPlaybackControls = initiallyEnabled;
                controller.Enabled = true;
                PumpMessages();
                if (initiallyEnabled) WaitForPanel();
                else
                {
                    PumpMessages(TimeSpan.FromMilliseconds(600));
                    Assert.Null(controller.Panel);
                }
                controller.ShowPlaybackControls = false;
                Assert.Null(controller.Panel);
                PumpMessages(TimeSpan.FromMilliseconds(450));
                Assert.Null(controller.Panel);
                controller.ToggleLock(owner);
                Assert.True(controller.IsLocked(owner));
                controller.ToggleLock(owner);
                PumpMessages(TimeSpan.FromMilliseconds(450));
                Assert.Null(controller.Panel);
                controller.ShowPlaybackControls = true;
                WaitForPanel();

                void WaitForPanel()
                {
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    while (controller.Panel is not { IsPanelVisible: true } && elapsed.Elapsed < TimeSpan.FromSeconds(3))
                        PumpMessages(TimeSpan.FromMilliseconds(60));
                    Assert.True(controller.Panel is { IsPanelVisible: true });
                }
            }
            finally
            {
                controller.Enabled = false;
                owner.Close();
            }
        });
    }

    [Fact]
    public void FloatingMirrorSizePreviewDoesNotRestoreItsSavedOrDefaultPosition()
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
            var mirror = new LyricsMirrorWindow(display, (_, _, _, _, _, _) => { }, composition);
            try
            {
                var settings = new AppSettings { UseFloatingWindow = true, WindowWidth = 420 };
                mirror.ApplySettings(settings);
                _ = new WindowInteropHelper(mirror).EnsureHandle();
                mirror.Left = display.WorkArea.Left / display.PixelsPerDip + 180;
                mirror.Top = display.WorkArea.Top / display.PixelsPerDip + 180;
                var original = FloatingLyricsPanelController.GetBounds(mirror);
                settings.FloatingWindowWidth = 520;
                settings.FloatingLyricsLayoutScalePercent = 125;
                mirror.ApplySettings(settings, keepControlPanelOpen: true, preserveFloatingPosition: true);
                mirror.UpdateLayout();
                var preview = FloatingLyricsPanelController.GetBounds(mirror);
                Assert.Equal(original.TopLeft, preview.TopLeft);
                Assert.True(preview.Width > original.Width);
                Assert.True(preview.Height > original.Height);
                mirror.ApplySettings(settings);
                Assert.NotEqual(preview.TopLeft, FloatingLyricsPanelController.GetBounds(mirror).TopLeft);
            }
            finally { mirror.Close(); }
        });
    }

    [Fact]
    public void LockedLyricsPassThroughOutsideTheCoverAndTheCoverCanUnlockThem()
    {
        RunSta(() =>
        {
            using var receiver = new ReceiverWindow();
            var owner = CreateOwner(layered: true);
            var changed = new List<bool>();
            var coverHovered = false;
            using var controller = new FloatingLyricsPanelController(owner.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }));
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                var bounds = FloatingLyricsPanelController.GetBounds(owner);
                var cover = new Rect(bounds.Left + 8, bounds.Top + 4, 34, 34);
                controller.Register(owner, () => display, () => true, (_, _) => { },
                    coverBounds: () => cover, lockChanged: (locked, hovered) =>
                    {
                        coverHovered = hovered;
                        if (changed.Count == 0 || changed[^1] != locked) changed.Add(locked);
                    });
                controller.RegisterSizeAdjustment(owner, () => new FloatingWindowSize(420, 100), _ => { }, _ => { });
                controller.Enabled = true;
                controller.Open(display.Id, owner);
                Assert.NotNull(controller.Panel);
                PumpMessages();
                var corner = Point(bounds.Right - 10, bounds.Top + 10);
                Assert.IsType<FloatingResizeGripWindow>(HwndSource.FromHwnd(WindowFromPoint(corner)).RootVisual);
                controller.ToggleLock(owner);
                PumpMessages();
                Assert.True(controller.IsLocked(owner));
                Assert.Null(controller.Panel);
                Assert.Equal(receiver.Handle, WindowFromPoint(corner));
                Assert.Equal(receiver.Handle, WindowFromPoint(Point(bounds.Right - 40, bounds.Top + 20)));
                var unlockHandle = WindowFromPoint(Point(cover.Left + 10, cover.Top + 10));
                Assert.NotEqual(receiver.Handle, unlockHandle);
                var unlockWindow = Assert.IsType<FloatingCoverUnlockWindow>(HwndSource.FromHwnd(unlockHandle).RootVisual);
                unlockWindow.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseEnterEvent });
                Assert.True(coverHovered);
                unlockWindow.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseLeaveEvent });
                Assert.False(coverHovered);
                Assert.Null(((System.Windows.Controls.Border)unlockWindow.Content).ToolTip);
                controller.Open(display.Id, owner);
                Assert.Null(controller.Panel);
                unlockWindow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                Assert.False(controller.IsLocked(owner));
                Assert.Collection(changed, value => Assert.True(value), value => Assert.False(value));
                controller.Open(display.Id, owner);
                Assert.NotNull(controller.Panel);
                controller.ToggleLock(owner);
                controller.Enabled = false;
                Assert.False(controller.IsLocked(owner));
                Assert.Equal(new WindowInteropHelper(owner).Handle,
                    WindowFromPoint(Point(bounds.Right - 40, bounds.Top + 20)));
            }
            finally { controller.Enabled = false; owner.Close(); }
        });
    }

    [Fact]
    public void FloatingLocksAreIndependentAndCannotLeaveAHiddenCoverAsTheOnlyUnlockEntry()
    {
        RunSta(() =>
        {
            var first = CreateOwner();
            var second = CreateOwner();
            second.Left += 450;
            var coverVisible = true;
            using var controller = new FloatingLyricsPanelController(first.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }));
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                var bounds = FloatingLyricsPanelController.GetBounds(first);
                var cover = new Rect(bounds.Left + 8, bounds.Top + 4, 34, 34);
                controller.Register(first, () => display, () => true, (_, _) => { },
                    coverBounds: () => coverVisible ? cover : Rect.Empty);
                controller.Register(second, () => display, () => true, (_, _) => { });
                controller.Enabled = true;
                controller.ToggleLock(first);
                Assert.True(controller.IsLocked(first));
                Assert.False(controller.IsLocked(second));
                coverVisible = false;
                PumpMessages(TimeSpan.FromMilliseconds(100));
                Assert.False(controller.IsLocked(first));
                controller.ToggleLock(first);
                Assert.False(controller.IsLocked(first));
            }
            finally { controller.Enabled = false; first.Close(); second.Close(); }
        });
    }

    [Fact]
    public void CoverClickTargetsItsOwnWindowEvenWhenTwoCardsShareADisplay()
    {
        RunSta(() =>
        {
            var first = CreateOwner();
            var second = CreateOwner();
            second.Left += 450;
            using var controller = new FloatingLyricsPanelController(first.Dispatcher,
                () => new FloatingLyricsControlWindow(_ => { }, _ => { }));
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                controller.Register(first, () => display, () => true, (_, _) => { });
                controller.Register(second, () => display, () => true, (_, _) => { });
                controller.Enabled = true;
                controller.Open(display.Id, second);
                Assert.Same(second, controller.Panel!.Owner);
                controller.Open(display.Id, first);
                Assert.Same(first, controller.Panel!.Owner);
            }
            finally { controller.Enabled = false; first.Close(); second.Close(); }
        });
    }

    [Fact]
    public void DragMovesTheCompleteCardWithoutClosingOrRecreatingControls()
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            FloatingLyricsPanelController? controller = null;
            var surfaces = new List<bool>();
            var created = 0;
            var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
            var scale = TaskbarNativeMethods.GetDpiForWindow(new WindowInteropHelper(owner).Handle) / 96.0;
            try
            {
                controller = new FloatingLyricsPanelController(owner.Dispatcher, () =>
                {
                    created++;
                    return new FloatingLyricsControlWindow(_ => { }, _ => { });
                });
                controller.Register(owner, () => display, () => true, (open, _) => surfaces.Add(open), () =>
                {
                    var panel = controller.Panel!;
                    surfaces.Clear();
                    var initialLyrics = FloatingLyricsPanelController.GetBounds(owner);
                    var initialControls = FloatingLyricsPanelController.GetBounds(panel);
                    var above = initialControls.Top < initialLyrics.Top;
                    var y = above ? display.WorkArea.Top + 8 : display.WorkArea.Bottom - (int)initialLyrics.Height - 8;
                    Assert.True(TaskbarNativeMethods.SetWindowPos(new WindowInteropHelper(owner).Handle, IntPtr.Zero,
                        (int)initialLyrics.Left + 80, y, 0, 0,
                        TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE));
                    PumpMessages();
                    var movedLyrics = FloatingLyricsPanelController.GetBounds(owner);
                    var movedControls = FloatingLyricsPanelController.GetBounds(panel);
                    Assert.Equal(movedLyrics.Left, movedControls.Left);
                    Assert.Equal(above ? movedLyrics.Top : movedLyrics.Bottom, above ? movedControls.Bottom : movedControls.Top);
                    Assert.True(panel.IsPanelVisible);
                    Assert.DoesNotContain(false, surfaces);
                });
                controller.Enabled = true;
                controller.Open(display.Id);
                var panel = controller.Panel!;
                PumpMessages();
                var grip = (System.Windows.Controls.Border)panel.FindName("DragGrip");
                var metadata = (FrameworkElement)panel.FindName("MetadataText");
                Assert.Equal(metadata.ActualHeight, grip.ActualHeight);
                var icon = Assert.IsType<System.Windows.Controls.Viewbox>(grip.Child);
                Assert.Equal(Stretch.Uniform, icon.Stretch);
                var glyph = Assert.IsType<System.Windows.Shapes.Path>(icon.Child);
                Assert.Equal(10, Assert.IsType<GeometryGroup>(glyph.Data).Children.Count);
                var ink = glyph.Data.GetRenderBounds(new Pen(glyph.Stroke, glyph.StrokeThickness));
                var inkLeft = glyph.TranslatePoint(ink.TopLeft, panel).X;
                var inkRight = glyph.TranslatePoint(ink.TopRight, panel).X;
                var seek = (FrameworkElement)panel.FindName("SeekSlider");
                Assert.Equal(seek.TranslatePoint(new System.Windows.Point(), panel).X, inkLeft, 1);
                Assert.Equal(8, metadata.TranslatePoint(new System.Windows.Point(), panel).X - inkRight, 1);
                var play = (FrameworkElement)panel.FindName("PlayButton");
                var playTop = play.TranslatePoint(new System.Windows.Point(), panel).Y;
                var inkTop = glyph.TranslatePoint(ink.TopLeft, panel).Y;
                var inkBottom = glyph.TranslatePoint(ink.BottomLeft, panel).Y;
                Assert.Equal(33, ink.Height);
                Assert.InRange((inkTop + inkBottom) / 2 - playTop - play.ActualHeight / 2, -1, 1);
                panel.RequestDrag();
                Assert.Same(panel, controller.Panel);
                Assert.True(panel.IsPanelVisible);
                Assert.Equal(1, created);
                var final = FloatingLyricsPanelController.GetBounds(panel);
                Assert.InRange(final.Top, display.WorkArea.Top - scale, display.WorkArea.Bottom);
                Assert.InRange(final.Bottom, display.WorkArea.Top, display.WorkArea.Bottom + scale);
            }
            finally { controller?.Dispose(); owner.Close(); }
        });
    }

    [Fact]
    public void SavedPlacementSurvivesEmbeddedOffsetsAndResetsOnlyOnExplicitRequest()
    {
        RunSta(() =>
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, $"floating-position-{Guid.NewGuid():N}.json");
            var owner = CreateOwner();
            try
            {
                var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
                var store = new FloatingWindowPositionStore(path);
                store.Remember(display.Id, new FloatingWindowPosition(display.Id, 0.7, 0.3));
                var placement = new FloatingWindowPlacement(owner, store);
                var settings = new AppSettings { UseFloatingWindow = true };
                placement.Configure(settings);
                placement.Refresh(display);
                Assert.True(placement.TryRestore());
                var restored = FloatingLyricsPanelController.GetBounds(owner);
                settings.FontFamily = "Segoe UI";
                placement.Configure(settings);
                Assert.True(placement.TryRestore());
                Assert.Equal(restored, FloatingLyricsPanelController.GetBounds(owner));
                settings.XOffset = 100;
                placement.Configure(settings);
                Assert.True(placement.TryRestore());
                Assert.Equal(restored, FloatingLyricsPanelController.GetBounds(owner));
                placement.ResetPositions();
                Assert.False(placement.TryRestore());
                Assert.Null(new FloatingWindowPositionStore(path).Get(display.Id));
            }
            finally { owner.Close(); System.IO.File.Delete(path); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MouseSeekKeepsClickedPositionWhenSliderHandlesTheMouseEvent(bool handled)
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            var clock = new SeekTestClock();
            var seeks = new List<TimeSpan>();
            var panel = CreateSeekPanel(owner, seeks.Add, clock);
            try
            {
                var slider = (System.Windows.Controls.Slider)panel.FindName("SeekSlider");
                void RaiseMouse(RoutedEvent routedEvent) => slider.RaiseEvent(
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = routedEvent, Handled = handled });
                RaiseMouse(UIElement.PreviewMouseLeftButtonDownEvent);
                slider.Value = 750;
                UpdateSeekPanel(panel, 60);
                Assert.Equal(750, slider.Value);
                RaiseMouse(UIElement.PreviewMouseLeftButtonUpEvent);
                Assert.Equal(TimeSpan.FromSeconds(150), Assert.Single(seeks));
                clock.Advance(TimeSpan.FromMilliseconds(100));
                UpdateSeekPanel(panel, 60);
                Assert.Equal(750, slider.Value);
                UpdateSeekPanel(panel, 150.1);
                Assert.Equal(750.5, slider.Value, 6);
            }
            finally { panel.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData(Key.Right, 1)]
    [InlineData(Key.Left, -1)]
    public void RepeatedKeyboardSeeksHoldTheLatestTargetUntilThePlayerCatchesUp(Key key, int direction)
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            var clock = new SeekTestClock();
            var seeks = new List<TimeSpan>();
            var panel = CreateSeekPanel(owner, seeks.Add, clock);
            try
            {
                var slider = (System.Windows.Controls.Slider)panel.FindName("SeekSlider");
                for (var step = 1; step <= 2; step++)
                {
                    RaiseSeekKey(panel, slider, key, Keyboard.PreviewKeyDownEvent);
                    slider.Value += direction * 5; // A SmallChange is one second of a 200-second track.
                    UpdateSeekPanel(panel, 60);
                    Assert.Equal((60 + direction * step) * 5, slider.Value);
                    RaiseSeekKey(panel, slider, key, Keyboard.KeyUpEvent);
                    clock.Advance(TimeSpan.FromMilliseconds(100));
                    UpdateSeekPanel(panel, 60);
                    Assert.Equal((60 + direction * step) * 5, slider.Value);
                }
                Assert.Equal(new[] { TimeSpan.FromSeconds(60 + direction), TimeSpan.FromSeconds(60 + 2 * direction) }, seeks);
                // An acknowledgment of the older command must not override the newest target.
                UpdateSeekPanel(panel, 60 + direction);
                Assert.Equal((60 + 2 * direction) * 5, slider.Value);
                UpdateSeekPanel(panel, 60 + 2 * direction + 0.1);
                Assert.Equal((60 + 2 * direction + 0.1) * 5, slider.Value, 6);
                UpdateSeekPanel(panel, 60 + 2 * direction + 1);
                Assert.Equal((60 + 2 * direction + 1) * 5, slider.Value, 6);
            }
            finally { panel.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void KeyboardSeekIgnoresOtherKeyReleasesAndRecoversIfThePlayerRejectsTheSeek()
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            var clock = new SeekTestClock();
            var seeks = new List<TimeSpan>();
            var panel = CreateSeekPanel(owner, seeks.Add, clock);
            try
            {
                var slider = (System.Windows.Controls.Slider)panel.FindName("SeekSlider");
                RaiseSeekKey(panel, slider, Key.Right, Keyboard.PreviewKeyDownEvent);
                slider.Value += 5;
                RaiseSeekKey(panel, slider, Key.LeftShift, Keyboard.KeyUpEvent);
                Assert.Empty(seeks);
                UpdateSeekPanel(panel, 60);
                Assert.Equal(305, slider.Value);
                RaiseSeekKey(panel, slider, Key.Right, Keyboard.KeyUpEvent);
                Assert.Single(seeks);
                clock.Advance(TimeSpan.FromSeconds(3));
                UpdateSeekPanel(panel, 60);
                Assert.Equal(300, slider.Value);
            }
            finally { panel.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData("track")]
    [InlineData("disabled")]
    [InlineData("hidden")]
    public void PendingSeekDoesNotLeakAcrossTrackCapabilityOrVisibilityChanges(string change)
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            var panel = CreateSeekPanel(owner, _ => { }, new SeekTestClock());
            try
            {
                var slider = (System.Windows.Controls.Slider)panel.FindName("SeekSlider");
                RaiseSeekKey(panel, slider, Key.Right, Keyboard.PreviewKeyDownEvent);
                slider.Value += 5;
                RaiseSeekKey(panel, slider, Key.Right, Keyboard.KeyUpEvent);
                if (change == "hidden")
                {
                    panel.SetPanelVisible(false, immediate: true);
                    panel.SetPanelVisible(true, immediate: true);
                }
                UpdateSeekPanel(panel, 20, canSeek: change != "disabled", trackId: change == "track" ? "other" : "seek-test");
                Assert.Equal(100, slider.Value);
            }
            finally { panel.Close(); owner.Close(); }
        });
    }

    private static FloatingLyricsControlWindow CreateSeekPanel(Window owner, Action<TimeSpan> seek, TimeProvider clock)
    {
        var panel = new FloatingLyricsControlWindow(_ => { }, seek, clock);
        var lyrics = FloatingLyricsPanelController.GetBounds(owner);
        var scale = TaskbarNativeMethods.GetDpiForWindow(new WindowInteropHelper(owner).Handle) / 96.0;
        panel.Prepare(owner, new FloatingLyricsPanelPlacement(
            new Rect(lyrics.Left, lyrics.Bottom, lyrics.Width, 96 * scale), false), scale);
        UpdateSeekPanel(panel, 60);
        panel.SetPanelVisible(true, immediate: true);
        return panel;
    }

    private static void UpdateSeekPanel(FloatingLyricsControlWindow panel, double seconds,
        bool canSeek = true, string trackId = "seek-test") => panel.Update(
        new TaskbarLyrics.Core.Models.TrackInfo(trackId, trackId, "Artist", "", "test", TimeSpan.FromSeconds(200)),
        true, TimeSpan.FromSeconds(seconds), canSeek, new PlayerSourceBadge("test", "", false), false);

    private static void RaiseSeekKey(FloatingLyricsControlWindow panel, UIElement slider, Key key, RoutedEvent routedEvent) =>
        slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(panel)!, 0, key)
        { RoutedEvent = routedEvent });

    private sealed class SeekTestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }

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
                    new Rect(lyrics.Left, lyrics.Bottom, lyrics.Width, 96 * scale), false), scale);
                panel.Update(new TaskbarLyrics.Core.Models.TrackInfo("test", "Title", "Artist", "", "test", TimeSpan.FromMinutes(3)),
                    true, TimeSpan.Zero, true, new PlayerSourceBadge("test", "", false), false);
                Assert.Null(((System.Windows.Controls.TextBlock)panel.FindName("TitleText")).ToolTip);
                Assert.Null(((System.Windows.Controls.TextBlock)panel.FindName("ArtistText")).ToolTip);
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
                var bounds = new Rect(lyrics.Left, lyrics.Bottom, lyrics.Width, 96 * scale);
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

    private static Window CreateOwner(bool layered = false)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = layered,
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
