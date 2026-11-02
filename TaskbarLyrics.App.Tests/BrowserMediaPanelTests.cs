using System.Windows.Controls;
using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void BrowserCapabilitiesAndUnknownPositionReachBothControlPanels()
    {
        RunSta(() =>
        {
            var track = new TrackInfo("browser-test", "Song", "Artist", "Album", "Browser", TimeSpan.FromSeconds(200));
            var capabilities = new MediaControlCapabilities(false, false, true);
            var floating = new FloatingLyricsControlWindow(_ => { }, _ => { });
            var taskbar = new LyricsControlPanelWindow(_ => { }, _ => { });
            try
            {
                floating.Update(track, true, TimeSpan.Zero, false, default, true, false, capabilities);
                taskbar.UpdateTrack(track, true, null, null, false, default, capabilities);
                taskbar.UpdateTimeline(TimeSpan.Zero, track.Duration, true, false, false);
                ShowNativePanel(taskbar);
                foreach (var panel in new System.Windows.Window[] { floating, taskbar })
                {
                    Assert.False(((Button)panel.FindName("PlayButton")).IsEnabled);
                    Assert.False(((Button)panel.FindName("PreviousButton")).IsEnabled);
                    Assert.True(((Button)panel.FindName("NextButton")).IsEnabled);
                    Assert.False(((Slider)panel.FindName("SeekSlider")).IsEnabled);
                    Assert.Equal("--:--", ((TextBlock)panel.FindName("ElapsedText")).Text);
                }
                // Seeking is independent of whether a previous-track action exists.
                taskbar.UpdateTrack(track, true, null, null, false, default, capabilities);
                taskbar.UpdateTimeline(TimeSpan.FromSeconds(30), track.Duration, true, true, true);
                PumpMessages(TimeSpan.FromMilliseconds(300));
                taskbar.UpdateTimeline(TimeSpan.FromSeconds(30), track.Duration, true, true, true);
                Assert.True(((Slider)taskbar.FindName("SeekSlider")).IsEnabled);
            }
            finally {floating.Close();taskbar.Close();}
        });
    }
}
