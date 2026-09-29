using TaskbarLyrics.App;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class PlayerMediaSessionDiscoveryTests
{
    [Theory]
    [InlineData("cn.MoeKoe.Music", "MoeKoe Music", true)]
    [InlineData("chrome.exe", "chrome", true)]
    [InlineData("cn.MoeKoe.Music", "Other Music", false)]
    [InlineData("Music", "Music", false)]
    public void MatchesOnlyAConfidentRunningProcessName(string appUserModelId, string processName, bool expected)
    {
        Assert.Equal(expected, PlayerMediaSessionDiscovery.MatchesProcessName(appUserModelId, processName));
    }
}
