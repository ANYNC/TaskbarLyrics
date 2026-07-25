using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsWebViewScriptFactoryTests
{
    [Fact]
    public void SetLyricsEmitsV1EnvelopeAndClampsProgress()
    {
        var script = LyricsWebViewScriptFactory.SetLyrics(
            "A \"quoted\" line",
            "next",
            2,
            3,
            "track",
            isPureMusic: false,
            isPlaying: true);

        Assert.Contains("window.taskbarLyrics?.receive", script);
        Assert.Contains("\"version\":1", script);
        Assert.Contains("\"type\":\"lyrics\"", script);
        Assert.Contains("A \\u0022quoted\\u0022 line", script);
        Assert.Contains("\"progress\":1", script);
    }

    [Fact]
    public void SetSpectrumClampsEveryBar()
    {
        var script = LyricsWebViewScriptFactory.SetSpectrum([-1, 0.5f, 2]);

        Assert.Contains("\"type\":\"spectrum\"", script);
        Assert.Contains("[0,0.5,1]", script);
    }
}
