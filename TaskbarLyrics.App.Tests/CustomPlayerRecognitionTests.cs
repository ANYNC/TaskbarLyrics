using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class CustomPlayerRecognitionTests
{
    [Fact]
    public void BrowserCardEnablesUnconfiguredBrowsersButCustomCardControlsItsOwnBrowser()
    {
        using var provider = new SmtcMusicSessionProvider();
        Assert.False(provider.CanUseSource("chrome.exe"));

        provider.SetRecognitionOrder(["chrome.exe"], ["chrome.exe"], ["chrome.exe"]);
        Assert.True(provider.CanUseSource("chrome.exe"));

        provider.SetRecognitionOrder(["chrome.exe"], [], ["chrome.exe"]);
        Assert.False(provider.CanUseSource("chrome.exe"));
        provider.SetRecognitionOrder(["Browser", "chrome.exe"], ["Browser"], ["chrome.exe"]);
        Assert.False(provider.CanUseSource("chrome.exe"));
        Assert.True(provider.CanUseSource("msedge.exe"));
        Assert.Equal("Browser", SmtcMusicSessionProvider.NormalizeSource("msedge.exe"));
        Assert.False(provider.CanUseSource("explorer.exe"));
    }
}
