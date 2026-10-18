using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    private static readonly MediaHotkeyAction[] TaskbarPanelActions =
        [MediaHotkeyAction.PreviousTrack, MediaHotkeyAction.TogglePlayPause, MediaHotkeyAction.NextTrack];
    private static readonly string[] TaskbarPanelButtonNames = ["PreviousButton", "PlayButton", "NextButton"];

    [Fact]
    public void NativeTaskbarPanelKeepsLayoutRoutesActionsAndCachesHiddenTimeline()
    {
        RunSta(() =>
        {
            var actions = new List<MediaHotkeyAction>();
            var panel = new LyricsControlPanelWindow(actions.Add, _ => { }) { Topmost = true };
            try
            {
                var track = new TrackInfo("panel-test", "Song", "Artist", "Album", "Test", TimeSpan.FromMinutes(4));
                panel.UpdateTrack(track, true, null, null, false, new PlayerSourceBadge("Player", "", false));
                panel.UpdateTimeline(TimeSpan.FromSeconds(30), track.Duration, true, true);
                ShowNativePanel(panel);
                Assert.Equal(360, panel.ActualWidth);
                Assert.Equal(124, panel.ActualHeight);
                Assert.Equal("Song", ((TextBlock)panel.FindName("TitleText")).Text);
                Assert.Equal("Artist", ((TextBlock)panel.FindName("ArtistText")).Text);
                Assert.Equal("Player", ((TextBlock)panel.FindName("SourceText")).Text);
                var play = (Button)panel.FindName("PlayButton");
                Assert.Equal("暂停", AutomationProperties.GetName(play));
                foreach (var name in TaskbarPanelButtonNames)
                    ((Button)panel.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(TaskbarPanelActions, actions);
                var bounds = FloatingLyricsPanelController.GetBounds(panel);
                PumpMessages(TimeSpan.FromMilliseconds(250));
                Assert.Equal(bounds, FloatingLyricsPanelController.GetBounds(panel));
                panel.HidePanel(immediate: true);
                panel.UpdateTimeline(TimeSpan.FromSeconds(90), track.Duration, false, true);
                ShowNativePanel(panel);
                Assert.Equal(375, ((Slider)panel.FindName("SeekSlider")).Value, 2);
                Assert.Equal("播放", AutomationProperties.GetName(play));
            }
            finally { panel.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeTaskbarPanelPollsOnlyWhileHoverPanelIsOpen(bool hover)
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            try
            {
                if (hover) panel.TrackHoverSource(owner, FloatingLyricsPanelController.GetBounds(owner));
                ShowNativePanel(panel);
                var timer = (DispatcherTimer)typeof(LyricsControlPanelWindow).GetField("_hoverTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
                Assert.Equal(hover, timer.IsEnabled);
                panel.HidePanel();
                Assert.False(timer.IsEnabled);
                PumpMessages(TimeSpan.FromMilliseconds(250));
                Assert.False(panel.IsVisible);
            }
            finally { panel.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void NativeTaskbarPanelFadeDisablesActionsAndReleasesInputAfterCompletion()
    {
        RunSta(() =>
        {
            using var receiver = new ReceiverWindow();
            var actions = 0;
            var panel = new LyricsControlPanelWindow(_ => actions++, _ => { }) { Topmost = true };
            try
            {
                panel.UpdateTrack(new TrackInfo("id", "Song", "Artist", "", "Test", TimeSpan.FromMinutes(3)), true, null, null, false, default);
                ShowNativePanel(panel);
                PumpMessages();
                var bounds = FloatingLyricsPanelController.GetBounds(panel);
                var point = Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                Assert.Equal(new WindowInteropHelper(panel).Handle, WindowFromPoint(point));
                panel.HidePanel();
                ((Button)panel.FindName("PlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, actions);
                PumpMessages(TimeSpan.FromMilliseconds(250));
                Assert.False(panel.IsVisible);
                Assert.Equal(receiver.Handle, WindowFromPoint(point));
            }
            finally { panel.Close(); }
        });
    }

    [Fact]
    public void NativeTaskbarPanelSeekCommitsOnceAndPreservesTargetUntilConfirmed()
    {
        RunSta(() =>
        {
            var seeks = new List<TimeSpan>();
            var panel = new LyricsControlPanelWindow(_ => { }, seeks.Add);
            try
            {
                var track = new TrackInfo("id", "Song", "Artist", "", "Test", TimeSpan.FromSeconds(100));
                panel.UpdateTrack(track, false, null, null, false, default);
                panel.UpdateTimeline(TimeSpan.FromSeconds(10), track.Duration, false, true);
                ShowNativePanel(panel);
                var slider = (Slider)panel.FindName("SeekSlider");
                var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Handled = true };
                slider.RaiseEvent(down);
                slider.Value = 600;
                slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Handled = true });
                Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, seeks);
                PumpMessages(TimeSpan.FromMilliseconds(300));
                panel.UpdateTimeline(TimeSpan.FromSeconds(10), track.Duration, false, true);
                Assert.Equal(600, slider.Value);
                PumpMessages(TimeSpan.FromMilliseconds(300));
                panel.UpdateTimeline(TimeSpan.FromSeconds(60), track.Duration, false, true);
                Assert.Equal(600, slider.Value);
                panel.UpdateTrack(track with { Id = "next", Title = "Next" }, false, null, null, false, default);
                PumpMessages(TimeSpan.FromMilliseconds(300));
                panel.UpdateTimeline(TimeSpan.FromSeconds(2), track.Duration, false, true);
                Assert.Equal(20, slider.Value);
            }
            finally { panel.Close(); }
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.invalid/icon.png")]
    [InlineData("data:image/png;base64,invalid")]
    [InlineData("data:image/svg+xml,%3Cinvalid")]
    public void NativePanelInvalidImagesFailSafely(string? uri) => RunSta(() => Assert.Null(PanelImageSource.Load(uri)));

    [Fact]
    public void NativePanelDisplaysBuiltInAndColoredPresetIcons()
    {
        RunSta(() =>
        {
            var settings = new AppSettings();
            Assert.NotNull(PanelImageSource.Load(PlayerSourceBadgeResolver.Resolve("Spotify", settings).IconDataUri));
            Assert.NotNull(PanelImageSource.Load(PlayerSourceBadgeResolver.Resolve("Unknown", settings).IconDataUri));
            Assert.NotNull(PanelImageSource.Load("data:image/svg+xml," + Uri.EscapeDataString("<svg viewBox='0 0 24 24'><path fill='#ff0000' d='M0 0H24V24H0Z'/></svg>")));
        });
    }

    private static void ShowNativePanel(LyricsControlPanelWindow panel)
    {
        var task = panel.Dispatcher.InvokeAsync(() => panel.ShowNearAsync(new Rect(200, 400, 34, 34),
            DisplayMonitorService.GetDisplays().First(d => d.IsPrimary), CoverPosition.Left, activate: false)).Task.Unwrap();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) PumpMessages(TimeSpan.FromMilliseconds(10));
        Assert.True(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void NativeTaskbarPanelKeepsArtworkAcrossPlaybackUpdatesAndUsesMissingMetadataFallback()
    {
        RunSta(() =>
        {
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            try
            {
                var badge = PlayerSourceBadgeResolver.Resolve("Spotify", new AppSettings());
                var track = new TrackInfo("image", "", "", "", "Spotify", TimeSpan.FromMinutes(3));
                panel.UpdateTrack(track, true, badge.IconDataUri, null, false, badge);
                var cover = ((ImageBrush)panel.FindName("CoverBrush")).ImageSource;
                Assert.NotNull(cover);
                Assert.Equal(Visibility.Collapsed, ((TextBlock)panel.FindName("CoverFallback")).Visibility);
                panel.UpdateTrack(track, false, badge.IconDataUri, null, false, badge);
                Assert.Same(cover, ((ImageBrush)panel.FindName("CoverBrush")).ImageSource);
                Assert.Equal("未知歌曲", ((TextBlock)panel.FindName("TitleText")).Text);
                Assert.Equal("未知歌手", ((TextBlock)panel.FindName("ArtistText")).Text);
            }
            finally { panel.Close(); }
        });
    }
}
