using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsStyleScriptFactoryTests
{
    [Theory]
    [InlineData(LyricsTextAlignment.Left, "Left")]
    [InlineData(LyricsTextAlignment.Center, "Center")]
    [InlineData(LyricsTextAlignment.Right, "Right")]
    public void CreateEmitsTheSelectedLyricsTextAlignment(LyricsTextAlignment alignment, string expected)
    {
        var script = LyricsStyleScriptFactory.Create(
            new AppSettings { LyricsTextAlignment = alignment },
            pixelsPerDip: 1);

        Assert.Contains($"\"textAlignment\":\"{expected}\"", script);
    }

    [Fact]
    public void CreateNormalizesUndefinedLyricsTextAlignmentToLeft()
    {
        var settings = new AppSettings
        {
            LyricsTextAlignment = (LyricsTextAlignment)999
        };

        var script = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains("\"textAlignment\":\"Left\"", script);
    }
}
