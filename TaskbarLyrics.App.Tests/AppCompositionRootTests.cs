using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppCompositionRootTests
{
    [Fact]
    public void CreateMusicSessionServicesUsesOneProviderForAllPlaybackCapabilities()
    {
        using var root = CreateRoot();
        var services = root.CreateMusicSessionServices();

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

        using var root = CreateRoot();
        var sources = root.GetEnabledPlayerSources(settings);

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
        using var root = CreateRoot();
        using var runner = root.CreateLyricDiagnosticRunner();

        Assert.Equal(KnownLyricProviders.OnlineTrustOrder, runner.ProviderTrustOrder);
    }

    [Fact]
    public async Task RememberResolvedLyricsDelegatesToTheOwnedCache()
    {
        var cache = new RecordingResolvedLyricCache();
        using var root = new AppCompositionRoot(cache);
        var track = new TrackInfo(
            "track-1",
            "Song",
            "Artist",
            "Album",
            "QQMusic",
            TimeSpan.FromMinutes(3));
        var lyrics = new ResolvedLyrics(
            new ParsedLyrics(
                [new ParsedLyricLine(TimeSpan.Zero, null, "line")],
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                LyricPayloadFormat.Lrc),
            KnownLyricProviders.QQMusic,
            "candidate-1",
            LyricAcquisitionKind.Remote,
            new Dictionary<string, string>(StringComparer.Ordinal));

        var remembered = await root.RememberResolvedLyricsAsync(
            track,
            lyrics,
            CancellationToken.None);

        Assert.True(remembered);
        Assert.Same(track, cache.SavedTrack);
        Assert.Same(lyrics, cache.SavedLyrics);
    }

    [Fact]
    public void ClearLyricCacheClearsPipelineAndResolvedCaches()
    {
        var cache = new RecordingResolvedLyricCache();
        var pipelineClearCalls = 0;
        using var root = new AppCompositionRoot(cache, () => pipelineClearCalls++);

        root.ClearLyricCache();

        Assert.Equal(1, pipelineClearCalls);
        Assert.Equal(1, cache.ClearCalls);
    }

    private static AppCompositionRoot CreateRoot() =>
        new(new RecordingResolvedLyricCache());

    private sealed class RecordingResolvedLyricCache : IResolvedLyricCache, IDisposable
    {
        public TrackInfo? SavedTrack { get; private set; }

        public ResolvedLyrics? SavedLyrics { get; private set; }

        public int ClearCalls { get; private set; }

        public bool TryGet(TrackInfo track, out ResolvedLyrics? resolvedLyrics)
        {
            resolvedLyrics = null;
            return false;
        }

        public bool Store(TrackInfo track, ResolvedLyrics resolvedLyrics)
        {
            SavedTrack = track;
            SavedLyrics = resolvedLyrics;
            return true;
        }

        public void Clear() => ClearCalls++;

        public void Dispose()
        {
        }
    }
}
