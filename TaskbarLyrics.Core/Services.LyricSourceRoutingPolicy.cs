using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

internal static class LyricSourceRoutingPolicy
{
    private static readonly string[] LocalProviders =
    {
        "LocalMusicFile",
        "LocalLrcFile",
        "LocalEslrcFile",
        "LocalTtmlFile"
    };

    public static IEnumerable<string> BuildRoute(TrackInfo track)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static void YieldIfNew(HashSet<string> bag, string value, ICollection<string> output)
        {
            if (!string.IsNullOrWhiteSpace(value) && bag.Add(value))
            {
                output.Add(value);
            }
        }

        var route = new List<string>();
        var source = track.SourceApp?.Trim() ?? string.Empty;

        // Always prefer exact source match first.
        YieldIfNew(yielded, source, route);

        if (IsQQFamily(source))
        {
            // QQ app: QQ source first, then common fallback sources.
            YieldIfNew(yielded, "QQMusic", route);
            YieldIfNew(yielded, "LrcLib", route);
            AddLocalFallbacks(yielded, route);
        }
        else if (IsNeteaseFamily(source))
        {
            // Netease app: Netease source first, then common fallback sources.
            YieldIfNew(yielded, "Netease", route);
            YieldIfNew(yielded, "LrcLib", route);
            AddLocalFallbacks(yielded, route);
        }
        else if (IsSpotifyFamily(source))
        {
            // Spotify app: QQ -> Kugou -> Netease -> LRCLIB.
            YieldIfNew(yielded, "QQMusic", route);
            YieldIfNew(yielded, "Kugou", route);
            YieldIfNew(yielded, "Netease", route);
            YieldIfNew(yielded, "LrcLib", route);
            AddLocalFallbacks(yielded, route);
        }
        else if (source.Contains("kugou", StringComparison.OrdinalIgnoreCase))
        {
            YieldIfNew(yielded, "Kugou", route);
            YieldIfNew(yielded, "LrcLib", route);
            AddLocalFallbacks(yielded, route);
        }
        else if (source.Contains("kuwo", StringComparison.OrdinalIgnoreCase))
        {
            YieldIfNew(yielded, "Kuwo", route);
            YieldIfNew(yielded, "LrcLib", route);
            AddLocalFallbacks(yielded, route);
        }
        else if (source.Contains("apple", StringComparison.OrdinalIgnoreCase))
        {
            YieldIfNew(yielded, "AppleMusic", route);
            YieldIfNew(yielded, "LrcLib", route);
            AddLocalFallbacks(yielded, route);
        }
        else
        {
            // Generic app route.
            YieldIfNew(yielded, "LrcLib", route);
            AddLocalFallbacks(yielded, route);
            YieldIfNew(yielded, "QQMusic", route);
            YieldIfNew(yielded, "Netease", route);
            YieldIfNew(yielded, "Kugou", route);
            YieldIfNew(yielded, "Kuwo", route);
            YieldIfNew(yielded, "AppleMusic", route);
        }

        // Fallback provider.
        YieldIfNew(yielded, "*", route);
        return route;
    }

    private static bool IsSpotifyFamily(string source)
    {
        return source.Contains("spotify", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQQFamily(string source)
    {
        return source.Contains("qqmusic", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("qq", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNeteaseFamily(string source)
    {
        return source.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("163music", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("music.163", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("wyy", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddLocalFallbacks(HashSet<string> yielded, ICollection<string> route)
    {
        static void YieldIfNew(HashSet<string> bag, string value, ICollection<string> output)
        {
            if (!string.IsNullOrWhiteSpace(value) && bag.Add(value))
            {
                output.Add(value);
            }
        }

        foreach (var provider in LocalProviders)
        {
            YieldIfNew(yielded, provider, route);
        }
    }
}
