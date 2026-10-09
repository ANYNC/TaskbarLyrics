using System.Windows;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsWebMessageRouterTests
{
    [Fact]
    public void CoverClickAcceptsBoundedViewportCoordinates()
    {
        var message = LyricsWebMessageRouter.Parse(
            "{\"version\":1,\"type\":\"coverClick\",\"payload\":{\"x\":4,\"y\":3,\"width\":34,\"height\":34,\"viewportWidth\":500,\"viewportHeight\":50}}");

        Assert.True(LyricsWebMessageRouter.TryGetCoverClick(message, out var bounds));
        Assert.Equal(new Rect(8, 6, 68, 68), bounds.InHost(new Size(1000, 100)));
    }

    [Theory]
    [InlineData("{\"version\":2,\"type\":\"coverClick\",\"payload\":{\"x\":0,\"y\":0,\"width\":34,\"height\":34,\"viewportWidth\":500,\"viewportHeight\":50}}")]
    [InlineData("{\"version\":1,\"type\":\"coverClick\",\"payload\":{\"x\":490,\"y\":0,\"width\":34,\"height\":34,\"viewportWidth\":500,\"viewportHeight\":50}}")]
    [InlineData("{\"version\":1,\"type\":\"coverClick\",\"payload\":{\"x\":-1,\"y\":0,\"width\":34,\"height\":34,\"viewportWidth\":500,\"viewportHeight\":50}}")]
    [InlineData("{\"version\":1,\"type\":\"coverClick\",\"payload\":{\"x\":0,\"y\":0,\"width\":34,\"height\":34,\"viewportWidth\":0,\"viewportHeight\":50}}")]
    [InlineData("{\"version\":1,\"type\":\"other\",\"payload\":{\"x\":0,\"y\":0,\"width\":34,\"height\":34,\"viewportWidth\":500,\"viewportHeight\":50}}")]
    public void CoverClickRejectsInvalidCoordinatesOrEnvelope(string json)
    {
        Assert.False(LyricsWebMessageRouter.TryGetCoverClick(LyricsWebMessageRouter.Parse(json), out _));
    }
}
