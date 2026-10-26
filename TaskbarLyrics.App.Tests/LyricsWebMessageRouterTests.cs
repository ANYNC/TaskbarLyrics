using System.Windows;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsWebMessageRouterTests
{
    [Theory]
    [InlineData(-1, 0, 34, 34, 500, 50, "NegativePosition")]
    [InlineData(0, -1, 34, 34, 500, 50, "NegativePosition")]
    [InlineData(0, 0, 0, 34, 500, 50, "NonPositiveSize")]
    [InlineData(0, 0, 34, 34, 500, 0, "InvalidViewport")]
    [InlineData(0, 0, 34, 34, 500, 12, "OutsideViewport")]
    [InlineData(0, 0, 10, 10, 500, 12, "None")]
    [InlineData(0, 0, 34, 34, 500, 33, "None")]
    public void BoundsDiagnosticsExplainRejectionWithoutRelaxingValidation(
        double x, double y, double width, double height, double viewportWidth, double viewportHeight, string expected)
    {
        var json = FormattableString.Invariant($"{{\"version\":1,\"type\":\"coverClick\",\"payload\":{{\"x\":{x},\"y\":{y},\"width\":{width},\"height\":{height},\"viewportWidth\":{viewportWidth},\"viewportHeight\":{viewportHeight}}}}}");
        var message = LyricsWebMessageRouter.Parse(json)!;
        Assert.Equal(expected, LyricsWebMessageRouter.ReadBounds(message.Payload!.Value, out _));
        Assert.Equal(expected == "None", LyricsWebMessageRouter.TryGetCoverClick(message, out _));
    }

    [Fact]
    public void BoundsDiagnosticsRejectMissingOrNonNumericFields()
    {
        using var payload = System.Text.Json.JsonDocument.Parse("{\"x\":\"34\"}");
        Assert.Equal("InvalidNumber", LyricsWebMessageRouter.ReadBounds(payload.RootElement, out _));
    }

    [Theory]
    [InlineData("Cover", "Hover", true)]
    [InlineData("Lyrics", "Click", true)]
    [InlineData("Lyrics", "Keyboard", true)]
    [InlineData("Cover", "Unknown", false)]
    [InlineData("Unknown", "Hover", false)]
    [InlineData("1", "Hover", false)]
    [InlineData("Cover, Lyrics", "Hover", false)]
    public void ControlPanelTriggerValidatesAreaAndActivation(string area, string activation, bool expected)
    {
        var message = LyricsWebMessageRouter.Parse(
            $"{{\"version\":1,\"type\":\"controlPanelTrigger\",\"payload\":{{\"area\":\"{area}\",\"activation\":\"{activation}\",\"x\":4,\"y\":3,\"width\":34,\"height\":34,\"viewportWidth\":500,\"viewportHeight\":50}}}}");
        Assert.Equal(expected, LyricsWebMessageRouter.TryGetControlPanelTrigger(message, out var bounds, out var actualArea, out var actualActivation));
        if (!expected) return;
        Assert.Equal(area, actualArea.ToString());
        Assert.Equal(activation, actualActivation.ToString());
        Assert.Equal(new Rect(8, 6, 68, 68), bounds.InHost(new Size(1000, 100)));
    }

    [Theory]
    [InlineData(1, 490, 500)]
    [InlineData(1, -1, 500)]
    [InlineData(1, 0, 0)]
    [InlineData(2, 0, 500)]
    public void ControlPanelTriggerRejectsInvalidBoundsAndVersions(int version, double x, double viewportWidth)
    {
        var json = FormattableString.Invariant($"{{\"version\":{version},\"type\":\"controlPanelTrigger\",\"payload\":{{\"area\":\"Lyrics\",\"activation\":\"Hover\",\"x\":{x},\"y\":0,\"width\":34,\"height\":34,\"viewportWidth\":{viewportWidth},\"viewportHeight\":50}}}}");
        Assert.False(LyricsWebMessageRouter.TryGetControlPanelTrigger(LyricsWebMessageRouter.Parse(json), out _, out _, out _));
    }
    [Fact]
    public void CoverClickAcceptsBoundedViewportCoordinates()
    {
        var message = LyricsWebMessageRouter.Parse(
            "{\"version\":1,\"type\":\"coverClick\",\"payload\":{\"x\":4,\"y\":3,\"width\":34,\"height\":34,\"viewportWidth\":500,\"viewportHeight\":50}}");

        Assert.True(LyricsWebMessageRouter.TryGetCoverClick(message, out var bounds));
        Assert.True(LyricsWebMessageRouter.TryGetControlPanelTrigger(message, out _, out var area, out var activation));
        Assert.Equal(ControlPanelTriggerArea.Cover, area);
        Assert.Equal(ControlPanelActivation.Click, activation);
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
