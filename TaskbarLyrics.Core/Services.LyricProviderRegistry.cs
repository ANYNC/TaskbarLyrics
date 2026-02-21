using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricProviderRegistry : ILyricProviderRegistry
{
    private readonly Dictionary<string, List<ILyricProvider>> _providers;

    public LyricProviderRegistry(IEnumerable<ILyricProvider> providers)
    {
        _providers = new Dictionary<string, List<ILyricProvider>>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            if (!_providers.TryGetValue(provider.SourceApp, out var list))
            {
                list = new List<ILyricProvider>();
                _providers[provider.SourceApp] = list;
            }

            list.Add(provider);
        }
    }

    public async Task<LyricResolveResult> ResolveLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var route = LyricSourceRoutingPolicy.BuildRoute(track);
        foreach (var sourceKey in route)
        {
            if (!_providers.TryGetValue(sourceKey, out var providersForSource))
            {
                continue;
            }

            foreach (var provider in providersForSource)
            {
                var effectiveTrack =
                    string.Equals(sourceKey, "*", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sourceKey, track.SourceApp, StringComparison.OrdinalIgnoreCase)
                        ? track
                        : track with { SourceApp = sourceKey };

                var result = await provider.GetLyricsAsync(effectiveTrack, cancellationToken);
                if (result is not null && result.Lines.Count > 0)
                {
                    return new LyricResolveResult(
                        Document: result,
                        SourceApp: sourceKey);
                }
            }
        }

        return new LyricResolveResult(
            Document: null,
            SourceApp: null);
    }

    public async Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveLyricsAsync(track, cancellationToken);
        return resolved.Document;
    }
}
