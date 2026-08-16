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
        Assert.Contains("\"animateTransition\":true", script);
    }

    [Fact]
    public void SetLyricsCanDisableTransitionAnimation()
    {
        var script = LyricsWebViewScriptFactory.SetLyrics(
            "current",
            "next",
            0.5,
            2,
            "track",
            isPureMusic: false,
            isPlaying: false,
            animateTransition: false);

        Assert.Contains("\"animateTransition\":false", script);
    }

    [Fact]
    public void SetLyricsEmitsStablePresentationScene()
    {
        var script = LyricsWebViewScriptFactory.SetLyrics(
            "正在检索歌词...",
            string.Empty,
            0,
            -1,
            "track",
            isPureMusic: false,
            isPlaying: true,
            presentationScene: LyricsPresentationScene.Searching);

        Assert.Contains("\"scene\":\"searching\"", script);
    }

    [Fact]
    public void SetLyricsClampsWordScanProgressAndEmitsNullWhenUnavailable()
    {
        var clampedHigh = LyricsWebViewScriptFactory.SetLyrics(
            "current",
            "next",
            0,
            0,
            "track",
            isPureMusic: false,
            isPlaying: true,
            wordScanProgress: 1.5);
        var clampedLow = LyricsWebViewScriptFactory.SetLyrics(
            "current",
            "next",
            0,
            0,
            "track",
            isPureMusic: false,
            isPlaying: true,
            wordScanProgress: -0.5);
        var unavailable = LyricsWebViewScriptFactory.SetLyrics(
            "current",
            "next",
            0,
            0,
            "track",
            isPureMusic: false,
            isPlaying: true);

        Assert.Contains("\"wordScanProgress\":1", clampedHigh);
        Assert.Contains("\"wordScanProgress\":0", clampedLow);
        Assert.Contains("\"wordScanProgress\":null", unavailable);
    }

    [Fact]
    public void SetLyricsEmitsStructuredTranslationPayloadFields()
    {
        var script = LyricsWebViewScriptFactory.SetLyrics(
            "current",
            "next",
            0.5,
            2,
            "track",
            isPureMusic: false,
            isPlaying: true,
            currentTranslation: "translated current",
            nextTranslation: "translated next",
            translationMode: true);

        Assert.Contains("\"currentTranslation\":\"translated current\"", script);
        Assert.Contains("\"nextTranslation\":\"translated next\"", script);
        Assert.Contains("\"translationMode\":true", script);
    }

    [Fact]
    public void SetSpectrumClampsEveryBar()
    {
        var script = LyricsWebViewScriptFactory.SetSpectrum([-1, 0.5f, 2]);

        Assert.Contains("\"type\":\"spectrum\"", script);
        Assert.Contains("[0,0.5,1]", script);
    }
}
