using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class PlayerSourceBadgeResolverTests
{
    [Fact]
    public void BuiltInSourceUsesTheSamePngAsTheSettingsPlayerCard()
    {
        var badge = PlayerSourceBadgeResolver.Resolve("Netease", new AppSettings());
        var cardIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PlayerIcons", "网易云音乐.png");

        Assert.Equal("网易云音乐", badge.DisplayName);
        Assert.StartsWith("data:image/png;base64,", badge.IconDataUri);
        Assert.Equal(File.ReadAllBytes(cardIconPath), Convert.FromBase64String(badge.IconDataUri.Split(',')[1]));
        Assert.False(badge.UsesDefaultIcon);
    }

    [Fact]
    public void CustomSourceUsesItsSettingsCardImage()
    {
        var icon = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Assets", "PlayerIcons", "网易云音乐.png")));
        var settings = new AppSettings
        {
            CustomPlayerSources = [new CustomPlayerSource("Custom.Player", "自定义播放器", true) { IconDataUrl = icon }]
        };

        var badge = PlayerSourceBadgeResolver.Resolve("Custom.Player", settings);

        Assert.Equal("自定义播放器", badge.DisplayName);
        Assert.Equal(icon, badge.IconDataUri);
        Assert.False(badge.UsesDefaultIcon);
    }

    [Fact]
    public void PresetSourceUsesTheSettingsCardShapeAndColor()
    {
        var settings = new AppSettings
        {
            CustomPlayerSources = [new CustomPlayerSource("Custom.Player", "自定义播放器", true)
            {
                PresetIconId = "disc", PresetIconColor = "#EF4444"
            }]
        };

        var badge = PlayerSourceBadgeResolver.Resolve("Custom.Player", settings);
        var svg = Uri.UnescapeDataString(badge.IconDataUri["data:image/svg+xml,".Length..]);

        Assert.Contains("fill=\"#EF4444\"", svg);
        Assert.Contains("M12 2a10 10", svg);
        Assert.False(badge.UsesDefaultIcon);
    }
}
