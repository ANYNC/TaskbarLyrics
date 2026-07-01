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
        var onlineProviders = CreateOnlineProviders(settings);
        var localProvider = CreateLocalProvider(settings);
        if (localProvider is null)
        {
            return new LyricProviderRegistry(onlineProviders);
        }

        if (settings.LocalLyricsSearchMode == LocalLyricsSearchMode.OnlineFallback)
        {
            return new FallbackLocalLyricProviderRegistry(
                new LyricProviderRegistry(onlineProviders),
                localProvider);
        }

        onlineProviders.Add(localProvider);
        return new LyricProviderRegistry(onlineProviders);
    }

    private static List<ILyricProvider> CreateOnlineProviders(AppSettings settings)
    {
        var providers = new List<ILyricProvider>
        {
            new LazyLyricProvider("LRCLIB", () => new GenericSmtcLyricProvider())
        };

        if (settings.EnableNetease)
        {
            providers.Add(new LazyLyricProvider(
                "Netease",
                () => new LyricifyLyricProvider("Netease", Lyricify.Lyrics.Searchers.Searchers.Netease)));
        }

        if (settings.EnableQQMusic)
        {
            providers.Add(new LazyLyricProvider(
                "QQMusic",
                () => new LyricifyLyricProvider("QQMusic", Lyricify.Lyrics.Searchers.Searchers.QQMusic)));
        }

        if (settings.EnableKugou)
        {
            providers.Add(new LazyLyricProvider(
                "Kugou",
                () => new LyricifyLyricProvider("Kugou", Lyricify.Lyrics.Searchers.Searchers.Kugou)));
        }

        return providers;
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
