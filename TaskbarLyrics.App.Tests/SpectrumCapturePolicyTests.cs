using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SpectrumCapturePolicyTests
{
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, true)]
    public void UnauthorizedAudioAccessNeverStartsCapture(
        bool audioAccessGranted,
        bool previewEnabled,
        bool lyricsWindowVisible,
        bool spectrumContentVisible)
    {
        Assert.False(SpectrumCapturePolicy.ShouldCapture(
            audioAccessGranted,
            previewEnabled,
            lyricsWindowVisible,
            spectrumContentVisible));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public void AuthorizedCaptureRequiresPreviewOrVisibleSpectrumContent(
        bool audioAccessGranted,
        bool previewEnabled,
        bool lyricsWindowVisible,
        bool spectrumContentVisible)
    {
        Assert.False(SpectrumCapturePolicy.ShouldCapture(
            audioAccessGranted,
            previewEnabled,
            lyricsWindowVisible,
            spectrumContentVisible));
    }

    [Fact]
    public void VisibleLyricsWithSpectrumContentStartsCapture()
    {
        Assert.True(SpectrumCapturePolicy.ShouldCapture(
            audioAccessGranted: true,
            previewEnabled: false,
            lyricsWindowVisible: true,
            spectrumContentVisible: true));
    }

    [Fact]
    public void AuthorizedPreviewAllowsCaptureWhileLyricsWindowIsHidden()
    {
        Assert.True(SpectrumCapturePolicy.ShouldCapture(
            audioAccessGranted: true,
            previewEnabled: true,
            lyricsWindowVisible: false,
            spectrumContentVisible: false));
    }
}
