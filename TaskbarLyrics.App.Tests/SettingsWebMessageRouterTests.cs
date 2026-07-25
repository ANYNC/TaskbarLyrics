using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SettingsWebMessageRouterTests
{
    [Fact]
    public void ParseDeserializesTheV1UpdateEnvelope()
    {
        var message = SettingsWebMessageRouter.Parse("{\"version\":1,\"type\":\"update\",\"payload\":{\"key\":\"fontSize\",\"value\":18}}");

        Assert.NotNull(message);
        Assert.Equal("update", message.Type);
        Assert.Equal("fontSize", message.Key);
        Assert.Equal(18, message.Value!.Value.GetInt32());
    }

    [Fact]
    public void ParseWhenMessageIsEmptyReturnsNull()
    {
        Assert.Null(SettingsWebMessageRouter.Parse(""));
    }

    [Fact]
    public void ParseWhenVersionIsUnsupportedReturnsNull()
    {
        Assert.Null(SettingsWebMessageRouter.Parse("{\"version\":2,\"type\":\"ready\",\"payload\":{}}"));
        Assert.Null(SettingsWebMessageRouter.Parse("{\"version\":1,\"type\":\"ready\",\"payload\":"));
    }
}
