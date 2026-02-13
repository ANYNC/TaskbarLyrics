using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricProviderRegistry : ILyricProviderRegistry
{
    private readonly Dictionary<string, ILyricProvider> _providers;

    public LyricProviderRegistry(IEnumerable<ILyricProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.SourceApp, StringComparer.OrdinalIgnoreCase);
    }

    public Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(track.SourceApp, out var provider))
        {
            return Task.FromResult<LyricDocument?>(null);
        }

        return provider.GetLyricsAsync(track, cancellationToken);
    }
}
