using System.Globalization;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("zh-CN", "Zh-CN")]
    [InlineData("zh-TW", "Zh-Hant")]
    [InlineData("zh-HK", "Zh-Hant")]
    [InlineData("zh-MO", "Zh-Hant")]
    [InlineData("zh-Hant", "Zh-Hant")]
    [InlineData("zh-Hant-HK", "Zh-Hant")]
    [InlineData("en-US", "EN")]
    public void UsesLauncherLanguagePolicy(string culture, string expected)
    {
        Assert.Equal(expected, Text.DetectSystemLanguage(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void ResolvesEmbeddedResourcesAndReturnsMissingKeys()
    {
        Assert.Equal("暂停", Text.Translate("UI_Pause", "Zh-CN"));
        Assert.Equal("Pause", Text.Translate("UI_Pause", "EN"));
        Assert.Equal("暫停", Text.Translate("UI_Pause", "Zh-Hant"));
        Assert.Equal("Missing_Key", Text.Translate("Missing_Key", "EN"));
    }

    [Fact]
    public void BuildsWebBootstrapFromEmbeddedRuntime()
    {
        var bootstrap = Text.CreateWebBootstrap("EN");
        var traditionalBootstrap = Text.CreateWebBootstrap("Zh-Hant");

        Assert.Contains("\"language\":\"EN\"", bootstrap);
        Assert.Contains("window.taskbarLyricsI18n", bootstrap);
        Assert.Contains("\"language\":\"Zh-Hant\"", traditionalBootstrap);
    }
}
