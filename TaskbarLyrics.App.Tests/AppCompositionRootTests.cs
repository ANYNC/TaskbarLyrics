using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
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

    [Fact]
    public void CreateLyricResolutionCoordinatorRegistersOnlyNewTrustOrderedSources()
    {
        using var coordinator = AppCompositionRoot
            .CreateLyricResolutionCoordinator(new AppSettings());

        Assert.Equal(KnownLyricProviders.OnlineTrustOrder, coordinator.ProviderTrustOrder);
    }

    [Fact]
    public void CreateLyricDiagnosticRunnerRegistersEveryOnlineSourceInTrustOrder()
    {
        var runner = new AppCompositionRoot().CreateLyricDiagnosticRunner();

        Assert.Equal(KnownLyricProviders.OnlineTrustOrder, runner.ProviderTrustOrder);
    }
}
