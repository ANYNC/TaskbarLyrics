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

    [Fact]
    public void WindowDragRequestIsRecognizedInsideTheV1Envelope()
    {
        var message = LyricsWebMessageRouter.Parse("{\"version\":1,\"type\":\"windowDrag\",\"payload\":null}");

        Assert.True(LyricsWebMessageRouter.IsWindowDragRequest(message));
    }

    [Theory]
    [InlineData("{\"version\":2,\"type\":\"windowDrag\",\"payload\":null}")]
    [InlineData("{\"version\":1,\"type\":\"windowDragStart\",\"payload\":null}")]
    [InlineData("{\"version\":1,\"type\":\"coverClick\",\"payload\":null}")]
    public void WindowDragRequestRejectsUnknownEnvelopes(string json)
    {
        Assert.False(LyricsWebMessageRouter.IsWindowDragRequest(LyricsWebMessageRouter.Parse(json)));
    }

    [Fact]
    public void FloatingPanelToggleAcceptsBoundedSizing()
    {
        var message = LyricsWebMessageRouter.Parse(
            "{\"version\":1,\"type\":\"floatingPanelToggle\",\"payload\":{\"open\":true,\"height\":56,\"inset\":4,\"gap\":4,\"direction\":\"up\"}}");

        Assert.True(LyricsWebMessageRouter.TryGetFloatingPanelToggle(
            message, out var open, out var height, out var inset, out var gap, out var above));
        Assert.True(open);
        Assert.Equal(56, height);
        Assert.Equal(4, inset);
        Assert.Equal(4, gap);
        Assert.True(above);
    }

    [Theory]
    [InlineData("{\"version\":2,\"type\":\"floatingPanelToggle\",\"payload\":{\"open\":true,\"height\":56,\"inset\":4,\"gap\":4}}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelToggle\",\"payload\":{\"open\":\"yes\",\"height\":56,\"inset\":4,\"gap\":4}}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelToggle\",\"payload\":{\"open\":true,\"height\":0,\"inset\":4,\"gap\":4}}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelToggle\",\"payload\":{\"open\":true,\"height\":-20,\"inset\":4,\"gap\":4}}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelToggle\",\"payload\":{\"open\":true,\"height\":900,\"inset\":4,\"gap\":4}}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelToggle\",\"payload\":{\"open\":true,\"height\":56,\"inset\":999,\"gap\":4}}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelToggle\",\"payload\":{\"open\":true,\"height\":56}}")]
    public void FloatingPanelToggleRejectsUnusableSizing(string json)
    {
        Assert.False(LyricsWebMessageRouter.TryGetFloatingPanelToggle(
            LyricsWebMessageRouter.Parse(json), out _, out _, out _, out _, out _));
    }

    [Theory]
    [InlineData("previous")]
    [InlineData("next")]
    [InlineData("playPause")]
    [InlineData("seek")]
    public void FloatingPanelActionAcceptsKnownActions(string action)
    {
        var message = LyricsWebMessageRouter.Parse(
            $"{{\"version\":1,\"type\":\"floatingPanelAction\",\"payload\":{{\"action\":\"{action}\"}}}}");

        Assert.True(LyricsWebMessageRouter.TryGetFloatingPanelAction(message, out var parsed));
        Assert.Equal(action, parsed);
    }

    [Theory]
    [InlineData("{\"version\":2,\"type\":\"floatingPanelAction\",\"payload\":{\"action\":\"next\"}}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelAction\",\"payload\":{\"action\":\"stop\"}}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelAction\",\"payload\":null}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelSeek\",\"payload\":{\"position\":10}}")]
    public void FloatingPanelActionRejectsUnknownRequests(string json)
    {
        Assert.False(LyricsWebMessageRouter.TryGetFloatingPanelAction(LyricsWebMessageRouter.Parse(json), out _));
    }

    [Fact]
    public void FloatingPanelSeekAcceptsNonNegativePositions()
    {
        var message = LyricsWebMessageRouter.Parse(
            "{\"version\":1,\"type\":\"floatingPanelSeek\",\"payload\":{\"position\":95.5}}");

        Assert.True(LyricsWebMessageRouter.TryGetFloatingPanelSeek(message, out var position));
        Assert.Equal(TimeSpan.FromSeconds(95.5), position);
    }

    [Theory]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelSeek\",\"payload\":{\"position\":-1}}")]
    [InlineData("{\"version\":1,\"type\":\"floatingPanelSeek\",\"payload\":{}}")]
    [InlineData("{\"version\":2,\"type\":\"floatingPanelSeek\",\"payload\":{\"position\":10}}")]
    public void FloatingPanelSeekRejectsInvalidPositions(string json)
    {
        Assert.False(LyricsWebMessageRouter.TryGetFloatingPanelSeek(LyricsWebMessageRouter.Parse(json), out _));
    }
}
