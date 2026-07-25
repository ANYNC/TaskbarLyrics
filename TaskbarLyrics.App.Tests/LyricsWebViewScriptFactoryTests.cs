using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsWebViewScriptFactoryTests
{
    [Fact]
    public void SetLyrics_EscapesTextAndClampsProgress()
    {
        var script = LyricsWebViewScriptFactory.SetLyrics(
            "A \"quoted\" line",
            "next",
            2,
            3,
            "track",
            isPureMusic: false,
            isPlaying: true);

        Assert.Contains("window.taskbarLyrics?.setLyrics", script);
        Assert.Contains("A \\u0022quoted\\u0022 line", script);
        Assert.Contains(", 1, 3,", script);
    }

    [Fact]
    public void SetSpectrum_ClampsEveryBar()
    {
        var script = LyricsWebViewScriptFactory.SetSpectrum([-1, 0.5f, 2]);

        Assert.Equal("window.taskbarLyrics?.setSpectrum([0,0.5,1]);", script);
    }
}
