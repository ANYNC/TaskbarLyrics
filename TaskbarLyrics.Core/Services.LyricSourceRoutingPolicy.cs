using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

internal static class LyricSourceRoutingPolicy
{
    private static readonly string[] AdaptedProviders = { "QQMusic", "Netease", "Kugou" };

    public static bool TryGetOfficialProvider(string? sourceApp, out string provider)
    {
        var source = sourceApp?.Trim() ?? string.Empty;
        if (source.Contains("qqmusic", StringComparison.OrdinalIgnoreCase))
        {
            provider = "QQMusic";
            return true;
        }

        if (source.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("163music", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("music.163", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("wyy", StringComparison.OrdinalIgnoreCase))
        {
            provider = "Netease";
            return true;
        }

        if (source.Contains("kugou", StringComparison.OrdinalIgnoreCase))
        {
            provider = "Kugou";
            return true;
        }

        provider = string.Empty;
        return false;
    }

    public static IReadOnlyList<IReadOnlyList<string>> BuildFallbackBatches(TrackInfo track)
    {
        if (TryGetOfficialProvider(track.SourceApp, out var officialProvider))
        {
            return new[]
            {
                BuildBatch(AdaptedProviders.Where(provider =>
                    !string.Equals(provider, officialProvider, StringComparison.OrdinalIgnoreCase))
                    .Append("LRCLIB"))
            };
        }

        return new[]
        {
            BuildBatch(new[] { "QQMusic", "Netease", "LRCLIB" }),
            BuildBatch(new[] { "Kugou" })
        };
    }

    private static IReadOnlyList<string> BuildBatch(IEnumerable<string> providers)
    {
        return providers
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
