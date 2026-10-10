using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsControlPanelMessageRouterTests
{
    [Theory]
    [InlineData("openSettings", "{}", "OpenSettings")]
    [InlineData("toggleTranslation", "{}", "ToggleTranslation")]
    [InlineData("toggleMute", "{}", "ToggleMute")]
    [InlineData("mediaAction", "{\"action\":\"previous\"}", "Previous")]
    [InlineData("mediaAction", "{\"action\":\"toggle\"}", "TogglePlayPause")]
    [InlineData("mediaAction", "{\"action\":\"next\"}", "Next")]
    public void AcceptsKnownCommands(string type, string payload, string kind)
    {
        var message = WebViewMessageRouter.Parse($"{{\"version\":1,\"type\":\"{type}\",\"payload\":{payload}}}");

        Assert.True(LyricsControlPanelMessageRouter.TryParse(message, out var command));
        Assert.Equal(kind, command.Kind.ToString());
    }

    [Theory]
    [InlineData("seek", "{\"positionMs\":60000}", "Seek", 60000)]
    [InlineData("setVolume", "{\"level\":35}", "SetVolume", 35)]
    public void AcceptsBoundedNumericCommands(string type, string payload, string kind, long value)
    {
        var message = WebViewMessageRouter.Parse($"{{\"version\":1,\"type\":\"{type}\",\"payload\":{payload}}}");

        Assert.True(LyricsControlPanelMessageRouter.TryParse(message, out var command));
        Assert.Equal(kind, command.Kind.ToString());
        Assert.Equal(value, command.Value);
    }

    [Theory]
    [InlineData("{\"version\":2,\"type\":\"openSettings\",\"payload\":{}}")]
    [InlineData("{\"version\":1,\"type\":\"openSettings\",\"payload\":{\"extra\":true}}")]
    [InlineData("{\"version\":1,\"type\":\"seek\",\"payload\":{\"positionMs\":-1}}")]
    [InlineData("{\"version\":1,\"type\":\"setVolume\",\"payload\":{\"level\":101}}")]
    [InlineData("{\"version\":1,\"type\":\"setVolume\",\"payload\":{\"level\":42.5}}")]
    [InlineData("{\"version\":1,\"type\":\"mediaAction\",\"payload\":{\"action\":\"destroy\"}}")]
    [InlineData("{\"version\":1,\"type\":\"unknown\",\"payload\":{}}")]
    public void RejectsUnknownVersionsTypesAndInvalidPayloads(string json)
    {
        Assert.False(LyricsControlPanelMessageRouter.TryParse(WebViewMessageRouter.Parse(json), out _));
    }
}
