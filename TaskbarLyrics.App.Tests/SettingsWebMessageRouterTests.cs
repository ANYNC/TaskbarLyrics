using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SettingsWebMessageRouterTests
{
    [Fact]
    public void Parse_DeserializesTheExistingWebSettingsMessageShape()
    {
        var router = new SettingsWebMessageRouter();

        var message = router.Parse("{\"type\":\"update\",\"key\":\"fontSize\",\"value\":18}");

        Assert.NotNull(message);
        Assert.Equal("update", message.Type);
        Assert.Equal("fontSize", message.Key);
        Assert.Equal(18, message.Value!.Value.GetInt32());
    }

    [Fact]
    public void Parse_WhenMessageIsEmpty_ReturnsNull()
    {
        var router = new SettingsWebMessageRouter();

        Assert.Null(router.Parse(""));
    }
}
