using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class WebViewMessageRouterTests
{
    [Fact]
    public void ParseAcceptsV1EnvelopeAndPayload()
    {
        var message = WebViewMessageRouter.Parse(
            "  {\"version\":1,\"type\":\"copy\",\"payload\":{\"text\":\"diagnostics\"}}  ");

        Assert.NotNull(message);
        Assert.Equal(1, message.Version);
        Assert.Equal("copy", message.Type);
        Assert.Equal("diagnostics", message.Payload!.Value.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("{\"version\":2,\"type\":\"ready\",\"payload\":{}}")]
    [InlineData("{\"version\":1,\"payload\":{}}")]
    [InlineData("{\"version\":1,\"type\":\"   \",\"payload\":{}}")]
    public void ParseRejectsInvalidOrUnsupportedEnvelope(string? json)
    {
        Assert.Null(WebViewMessageRouter.Parse(json));
    }
}
