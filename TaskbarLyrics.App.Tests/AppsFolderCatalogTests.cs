using TaskbarLyrics.App;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppsFolderCatalogTests
{
    private static readonly AppsFolderCatalog Catalog = new(
    [
        new RegisteredAppEntry("汽水音乐", "com.soda.music"),
        new RegisteredAppEntry("Visual Studio Code", "Microsoft.VisualStudioCode"),
        new RegisteredAppEntry("QQ", "QQ")
    ]);

    [Theory]
    [InlineData("汽水音乐", "com.soda.music")]
    [InlineData("com.soda.music", "com.soda.music")]
    [InlineData("COM.SODA.MUSIC", "com.soda.music")]
    [InlineData("Visual Studio Code", "Microsoft.VisualStudioCode")]
    [InlineData("Microsoft.VisualStudioCode", "Microsoft.VisualStudioCode")]
    [InlineData(" 汽水音乐 ", "com.soda.music")]
    public void ResolvesRegisteredUserModelIdFromIdOrDisplayName(string identifier, string expected)
    {
        Assert.Equal(expected, Catalog.TryResolveUserModelId(identifier));
    }

    [Theory]
    [InlineData("QQMusic.exe")]
    [InlineData("https://pixpin.cn")]
    [InlineData("CNEventWindowClass")]
    [InlineData("")]
    [InlineData(null)]
    public void ReturnsNullWhenNoEntryMatches(string? identifier)
    {
        Assert.Null(Catalog.TryResolveUserModelId(identifier));
    }

    [Fact]
    public void PrefersRegisteredUserModelIdOverDisplayName()
    {
        var catalog = new AppsFolderCatalog(
        [
            new RegisteredAppEntry("示例播放器", "other.app"),
            new RegisteredAppEntry("另一个名称", "示例播放器")
        ]);

        Assert.Equal("示例播放器", catalog.TryResolveUserModelId("示例播放器"));
    }
}
