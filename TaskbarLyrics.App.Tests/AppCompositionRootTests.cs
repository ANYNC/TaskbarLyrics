using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppCompositionRootTests
{
    [Fact]
    public void CreateMusicSessionServicesUsesOneProviderForAllPlaybackCapabilities()
    {
        var services = new AppCompositionRoot().CreateMusicSessionServices();

        Assert.Same(services.SessionProvider, services.PlaybackController);
        Assert.Same(services.SessionProvider, services.PlayerRecognitionController);
    }

    [Fact]
    public void GetEnabledPlayerSourcesOnlyIncludesEnabledRecognizers()
    {
        var settings = new AppSettings
        {
            EnableQQMusic = false,
            EnableNetease = true,
            EnableKugou = false,
            EnableSpotify = true
        };

        var sources = new AppCompositionRoot().GetEnabledPlayerSources(settings);

        Assert.Equal(["Netease", "Spotify"], sources);
    }
}
