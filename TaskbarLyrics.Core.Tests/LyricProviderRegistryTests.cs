using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricProviderRegistryTests
{
    [Fact]
    public async Task Dispose_DisposesOwnedProvidersAndRejectsNewSearches()
    {
        var provider = new DisposableProvider();
        var registry = new LyricProviderRegistry([provider]);

        registry.Dispose();
        registry.Dispose();

        var results = await registry.ResolveLyricsAsync(CreateTrack());

        Assert.True(provider.IsDisposed);
        var result = Assert.Single(results);
        Assert.Equal(provider.SourceApp, result.SourceApp);
        Assert.Null(result.Document);
    }

    private static TrackInfo CreateTrack() => new(
        "track-id",
        "Track",
        "Artist",
        "Album",
        "QQMusic",
        TimeSpan.FromMinutes(3));

    private sealed class DisposableProvider : ILyricProvider, IDisposable
    {
        public string SourceApp => "Test";

        public bool IsDisposed { get; private set; }

        public Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LyricDocument?>(null);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
