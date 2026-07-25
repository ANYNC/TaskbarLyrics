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
        var providers = new List<ILyricProvider>
        {
            new GenericSmtcLyricProvider()
        };

        if (settings.EnableLocalLyrics && settings.LocalMusicFolders.Count > 0)
        {
            providers.Add(new LocalLyricProvider(settings.LocalMusicFolders));
        }

        // Player recognition switches must not disable fallback lyric providers.
        providers.Add(new LyricifyLyricProvider("Netease", Lyricify.Lyrics.Searchers.Searchers.Netease));
        providers.Add(new LyricifyLyricProvider("QQMusic", Lyricify.Lyrics.Searchers.Searchers.QQMusic));
        providers.Add(new LyricifyLyricProvider("Kugou", Lyricify.Lyrics.Searchers.Searchers.Kugou));
        return new LyricSyncService(
            new LyricProviderRegistry(providers),
            _ => settings.ShowLyricTranslation,
            sourceApp => TimeSpan.FromMilliseconds(settings.GetPlayerLyricOffsetMilliseconds(sourceApp)),
            (track, lyricSource) => TimeSpan.FromMilliseconds(
                trackLyricOffsetStore.GetOffsetMilliseconds(track, lyricSource)));
    }

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
