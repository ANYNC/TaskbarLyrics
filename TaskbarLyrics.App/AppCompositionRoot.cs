using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.App;

internal sealed record MusicSessionServices(
    IMusicSessionProvider SessionProvider,
    IMediaPlaybackController PlaybackController,
    IPlayerRecognitionController PlayerRecognitionController);

internal interface IAppCompositionRoot : IDisposable
{
    MusicSessionServices CreateMusicSessionServices();

    LyricSyncService CreateLyricSyncService(
        AppSettings settings,
        TrackLyricOffsetStore trackLyricOffsetStore);

    LyricDiagnosticRunner CreateLyricDiagnosticRunner();

    ValueTask<bool> RememberResolvedLyricsAsync(
        TrackInfo track,
        ResolvedLyrics resolvedLyrics,
        CancellationToken cancellationToken);

    void ClearLyricCache();

    LocalMediaCoverProvider? CreateLocalMediaCoverProvider(AppSettings settings);

    IReadOnlyCollection<string> GetEnabledPlayerSources(AppSettings settings);
}

internal sealed class AppCompositionRoot : IAppCompositionRoot
{
    private readonly IResolvedLyricCache _resolvedLyricCache;
    private readonly Action _clearPipelineCache;

    public AppCompositionRoot()
        : this(JsonResolvedLyricCache.CreateDefault(), LyricPipelineCache.ClearDefault)
    {
    }

    internal AppCompositionRoot(
        IResolvedLyricCache resolvedLyricCache,
        Action? clearPipelineCache = null)
    {
        _resolvedLyricCache = resolvedLyricCache ??
            throw new ArgumentNullException(nameof(resolvedLyricCache));
        _clearPipelineCache = clearPipelineCache ?? LyricPipelineCache.ClearDefault;
    }

    public MusicSessionServices CreateMusicSessionServices()
    {
        var provider = new SmtcMusicSessionProvider();
        return new MusicSessionServices(provider, provider, provider);
    }

    public LyricSyncService CreateLyricSyncService(
        AppSettings settings,
        TrackLyricOffsetStore trackLyricOffsetStore)
    {
        var coordinator = CreateLyricResolutionCoordinator(settings, _resolvedLyricCache);
        return new LyricSyncService(
            coordinator,
            sourceApp => TimeSpan.FromMilliseconds(settings.GetPlayerLyricOffsetMilliseconds(sourceApp)),
            (track, lyricSource) => TimeSpan.FromMilliseconds(
                trackLyricOffsetStore.GetOffsetMilliseconds(track, lyricSource)));
    }

    internal static LyricResolutionCoordinator CreateLyricResolutionCoordinator(
        AppSettings settings,
        IResolvedLyricCache? resolvedLyricCache = null)
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
            localProvider: localProvider,
            resolvedLyricCache: resolvedLyricCache);
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

    public ValueTask<bool> RememberResolvedLyricsAsync(
        TrackInfo track,
        ResolvedLyrics resolvedLyrics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_resolvedLyricCache.Store(track, resolvedLyrics));
    }

    public void ClearLyricCache()
    {
        _clearPipelineCache();
        _resolvedLyricCache.Clear();
    }

    public void Dispose()
    {
        if (_resolvedLyricCache is IDisposable disposable)
        {
            disposable.Dispose();
        }
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
