using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.App;

internal sealed record MusicSessionServices(
    IMusicSessionProvider SessionProvider,
    IMediaPlaybackController PlaybackController,
    IPlayerRecognitionController PlayerRecognitionController);

internal interface IAppCompositionRoot
{
    MusicSessionServices CreateMusicSessionServices();

    LyricSyncService CreateLyricSyncService(
        AppSettings settings,
        TrackLyricOffsetStore trackLyricOffsetStore);

    LyricDiagnosticRunner CreateLyricDiagnosticRunner();

    LocalMediaCoverProvider? CreateLocalMediaCoverProvider(AppSettings settings);

    IReadOnlyCollection<string> GetEnabledPlayerSources(AppSettings settings);
}

internal sealed class AppCompositionRoot : IAppCompositionRoot
{
    public MusicSessionServices CreateMusicSessionServices()
    {
        var provider = new SmtcMusicSessionProvider();
        return new MusicSessionServices(provider, provider, provider);
    }

    public LyricSyncService CreateLyricSyncService(
        AppSettings settings,
        TrackLyricOffsetStore trackLyricOffsetStore)
    {
        var coordinator = CreateLyricResolutionCoordinator(settings);
        return new LyricSyncService(
            coordinator,
            sourceApp => TimeSpan.FromMilliseconds(settings.GetPlayerLyricOffsetMilliseconds(sourceApp)),
            (track, lyricSource) => TimeSpan.FromMilliseconds(
                trackLyricOffsetStore.GetOffsetMilliseconds(track, lyricSource)));
    }

    internal static LyricResolutionCoordinator CreateLyricResolutionCoordinator(AppSettings settings)
    {
        var sources = new ILyricSource[]
        {
            new QqMusicLyricSource(),
            new KugouLyricSource(),
            new NeteaseLyricSource(),
            new LrcLibLyricSource()
        };
        var localProvider = settings.EnableLocalLyrics && settings.LocalMusicFolders.Count > 0
            ? new LocalLyricProvider(settings.LocalMusicFolders)
            : null;
        var cache = LyricPipelineCache.CreateDefault();
        return new LyricResolutionCoordinator(
            sources,
            [new LyricifyPayloadDecoder()],
            [new LyricifyPayloadParser()],
            cache,
            localProvider: localProvider);
    }

    public LyricDiagnosticRunner CreateLyricDiagnosticRunner() =>
        new(
            [
                new QqMusicLyricSource(),
                new KugouLyricSource(),
                new NeteaseLyricSource(),
                new LrcLibLyricSource()
            ],
            [new LyricifyPayloadDecoder()],
            [new LyricifyPayloadParser()]);

    public LocalMediaCoverProvider? CreateLocalMediaCoverProvider(AppSettings settings)
    {
        return settings.EnableLocalLyrics && settings.LocalMusicFolders.Count > 0
            ? new LocalMediaCoverProvider(settings.LocalMusicFolders)
            : null;
    }

    public IReadOnlyCollection<string> GetEnabledPlayerSources(AppSettings settings)
    {
        var sources = new List<string>();
        if (settings.EnableQQMusic) sources.Add("QQMusic");
        if (settings.EnableNetease) sources.Add("Netease");
        if (settings.EnableKugou) sources.Add("Kugou");
        if (settings.EnableSpotify) sources.Add("Spotify");
        return sources;
    }
}
