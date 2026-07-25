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

    [Fact]
    public async Task Dispose_DefersProviderDisposalUntilActiveSearchCompletes()
    {
        var provider = new BlockingDisposableProvider();
        var registry = new LyricProviderRegistry([provider]);

        var search = registry.ResolveLyricsAsync(CreateTrack());
        await provider.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        registry.Dispose();

        Assert.False(provider.IsDisposed);
        provider.CompleteSearch();
        await search.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(provider.IsDisposed);
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

    private sealed class BlockingDisposableProvider : ILyricProvider, IDisposable
    {
        private readonly TaskCompletionSource<LyricDocument?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string SourceApp => "QQMusic";
        public bool IsDisposed { get; private set; }

        public Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
        {
            SearchStarted.TrySetResult();
            return _completion.Task;
        }

        public void CompleteSearch()
        {
            _completion.TrySetResult(null);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
