using TaskbarLyrics.App;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class PlayerIconResolverTests
{
    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)]
    [InlineData("汽水音乐", false)]
    [InlineData("QQMusic.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void DetectsPackagedUserModelIds(string? identifier, bool expected)
    {
        Assert.Equal(expected, PlayerIconResolver.IsPackagedUserModelId(identifier));
    }

    [Fact]
    public void BuildsShellAppsFolderPath()
    {
        Assert.Equal("shell:AppsFolder\\com.soda.music", PlayerIconResolver.ToShellItemPath(" com.soda.music "));
    }

    [Fact]
    public void ReturnsNoIconForBlankIdentifiers()
    {
        Assert.Equal(string.Empty, PlayerIconResolver.ResolveDataUrl(null));
        Assert.Equal(string.Empty, PlayerIconResolver.ResolveDataUrl("   "));
    }
}
