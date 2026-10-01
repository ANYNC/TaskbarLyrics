using TaskbarLyrics.App;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class ProcessExecutablePathResolverTests
{
    [Theory]
    [InlineData("QQMusic.exe", "QQMusic")]
    [InlineData("qqmusic.EXE", "qqmusic")]
    [InlineData(@"C:\Program Files\Soda Music\SodaMusic.exe", "SodaMusic")]
    [InlineData(" QQMusic.exe ", "QQMusic")]
    public void DerivesProcessNameFromExecutableIdentifiers(string identifier, string expected)
    {
        Assert.Equal(expected, ProcessExecutablePathResolver.GetProcessNameCandidate(identifier));
    }

    [Theory]
    [InlineData("汽水音乐")]
    [InlineData("com.soda.music")]
    [InlineData("Microsoft.VisualStudioCode")]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("")]
    [InlineData(null)]
    public void DoesNotGuessProcessNameForOtherIdentifierShapes(string? identifier)
    {
        Assert.Null(ProcessExecutablePathResolver.GetProcessNameCandidate(identifier));
    }

    [Theory]
    [InlineData("汽水音乐", "汽水音乐", true)]
    [InlineData("汽水音乐", "汽水音乐 - 又三郎", true)]
    [InlineData("汽水音乐", "网易云音乐", false)]
    [InlineData("汽水音乐", "", false)]
    [InlineData("汽水音乐", null, false)]
    [InlineData("a", "a", false)]
    [InlineData("", "QQMusic", false)]
    public void MatchesWindowTitlesWithoutSingleCharacterNoise(string? identifier, string? windowTitle, bool expected)
    {
        Assert.Equal(expected, ProcessExecutablePathResolver.WindowTitleMatches(identifier, windowTitle));
    }
}
