using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.Light.App;

internal static class LyricProviderComposer
{
    public static LyricSyncService CreateSyncService(
        AppSettings settings,
        Action<TrackInfo, IReadOnlyList<LyricResolveResult>, TimeSpan>? publishDiagnostics = null,
        Func<string?, bool>? shouldShowTranslation = null)
    {
        var registry = CreateRegistry(settings);
        if (publishDiagnostics is not null)
        {
            registry = new DiagnosticLyricProviderRegistry(registry, publishDiagnostics);
        }

        return new LyricSyncService(registry, shouldShowTranslation ?? (_ => settings.ShowLyricTranslation));
    }

    private static ILyricProviderRegistry CreateRegistry(AppSettings settings)
    {
        var sources = CreateOnlineSources(settings);
        var localProvider = CreateLocalProvider(settings);
        var providerOrder = sources.Select(source => source.ProviderId).ToArray();
        var coordinator = new LyricResolutionCoordinator(
            sources,
            [new LyricifyPayloadDecoder()],
            [new LyricifyPayloadParser()],
            LyricPipelineCache.CreateDefault(),
            localProvider: settings.LocalLyricsSearchMode == LocalLyricsSearchMode.PreferLocal
                ? localProvider
                : null,
            trustPolicy: new LyricProviderTrustPolicy(providerOrder, providerOrder));
        ILyricProviderRegistry registry = new PipelineLyricProviderRegistry(coordinator);

        if (localProvider is not null && settings.LocalLyricsSearchMode == LocalLyricsSearchMode.OnlineFallback)
        {
            registry = new FallbackLocalLyricProviderRegistry(registry, localProvider);
        }

        return registry;
    }

    private static List<ILyricSource> CreateOnlineSources(AppSettings settings)
    {
        var sources = new List<ILyricSource>();

        if (settings.EnableQQMusic)
        {
            sources.Add(new QqMusicLyricSource());
        }

        if (settings.EnableKugou)
        {
            sources.Add(new KugouLyricSource());
        }

        if (settings.EnableNetease)
        {
            sources.Add(new NeteaseLyricSource());
        }

        sources.Add(new LrcLibLyricSource());
        return sources;
    }

    private static ILyricProvider? CreateLocalProvider(AppSettings settings)
    {
        if (settings.EnableLocalLyrics && settings.LocalMusicFolders.Count > 0)
        {
            return new LazyLyricProvider(
                "Local",
                () => new LocalLyricProvider(settings.LocalMusicFolders));
        }

        return null;
    }
}
