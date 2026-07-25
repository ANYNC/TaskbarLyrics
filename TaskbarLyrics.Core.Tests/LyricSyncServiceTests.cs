using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricSyncServiceTests
{
    [Fact]
    public async Task GetDisplayFrameAsyncAppliesPlayerAndTrackOffsetsBeforeSelectingTheLine()
    {
        var document = new LyricDocument(
        [
            new LyricLine(TimeSpan.Zero, "Intro"),
            new LyricLine(TimeSpan.FromSeconds(10), "Verse", "主歌"),
            new LyricLine(TimeSpan.FromSeconds(20), "Outro")
        ]);
        var registry = new ImmediateRegistry(document);
        using var service = new LyricSyncService(
            registry,
            shouldShowTranslation: _ => true,
            getPlayerLeadTime: _ => TimeSpan.FromMilliseconds(500),
            getTrackLeadTime: (_, _) => TimeSpan.FromMilliseconds(500));
        var snapshot = new PlaybackSnapshot(
            IsPlaying: true,
            Position: TimeSpan.FromSeconds(9),
            Track: CreateTrack());

        var frame = await service.GetDisplayFrameAsync(snapshot);

        Assert.Equal("Verse (主歌)", frame.CurrentLine);
        Assert.Equal("Outro", frame.NextLine);
        Assert.Equal(1, frame.CurrentLineIndex);
        Assert.Equal(0, frame.LineProgress);
    }

    [Fact]
    public async Task GetDisplayFrameAsyncWhenPreviousSearchCompletesLateKeepsNewerTrackLyrics()
    {
        var registry = new OutOfOrderRegistry();
        using var service = new LyricSyncService(registry);
        var firstSnapshot = new PlaybackSnapshot(true, TimeSpan.Zero, CreateTrack("First track"));
        var secondSnapshot = new PlaybackSnapshot(true, TimeSpan.Zero, CreateTrack("Second track"));

        await service.GetDisplayFrameAsync(firstSnapshot);
        await registry.FirstSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondFrame = await service.GetDisplayFrameAsync(secondSnapshot);
        Assert.Equal("Second lyric", secondFrame.CurrentLine);

        registry.CompleteFirstSearch();
        await registry.FirstSearchReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);

        var latestFrame = await service.GetDisplayFrameAsync(secondSnapshot);
        Assert.True(registry.FirstRequestWasCanceled);
        Assert.Equal("Second lyric", latestFrame.CurrentLine);
    }

    [Fact]
    public async Task DisposeCancelsActiveSearchAndDisposesRegistry()
    {
        var registry = new BlockingRegistry();
        using var service = new LyricSyncService(registry);
        var snapshot = new PlaybackSnapshot(
            IsPlaying: true,
            Position: TimeSpan.Zero,
            Track: CreateTrack());

        await service.GetDisplayFrameAsync(snapshot);
        await registry.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        service.Dispose();

        await registry.SearchCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(registry.IsDisposed);
    }

    private static TrackInfo CreateTrack(string title = "Midnight City") => new(
        "track-id",
        title,
        "M83",
        "Hurry Up, We're Dreaming",
        "Spotify",
        TimeSpan.FromSeconds(244));

    private sealed class BlockingRegistry : ILyricProviderRegistry
    {
        public TaskCompletionSource SearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SearchCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsDisposed { get; private set; }

        public async Task<List<LyricResolveResult>> ResolveLyricsAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default)
        {
            SearchStarted.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SearchCancelled.TrySetResult();
                throw;
            }

            return [];
        }

        public Task<LyricDocument?> GetLyricsAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default) => Task.FromResult<LyricDocument?>(null);

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class ImmediateRegistry(LyricDocument document) : ILyricProviderRegistry
    {
        public Task<List<LyricResolveResult>> ResolveLyricsAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default) => Task.FromResult(
            new List<LyricResolveResult>
            {
                new("Netease", document, LyricAcquisitionKind.Remote, 10)
            });

        public Task<LyricDocument?> GetLyricsAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default) => Task.FromResult<LyricDocument?>(document);

        public void Dispose()
        {
        }
    }

    private sealed class OutOfOrderRegistry : ILyricProviderRegistry
    {
        private readonly TaskCompletionSource<List<LyricResolveResult>> _firstSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstSearchReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FirstRequestWasCanceled { get; private set; }

        public Task<List<LyricResolveResult>> ResolveLyricsAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default)
        {
            return track.Title == "First track"
                ? ResolveFirstAsync(cancellationToken)
                : Task.FromResult(CreateResult("Second lyric"));
        }

        public Task<LyricDocument?> GetLyricsAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default) => Task.FromResult<LyricDocument?>(null);

        public void CompleteFirstSearch()
        {
            _firstSearch.TrySetResult(CreateResult("First lyric"));
        }

        public void Dispose()
        {
        }

        private async Task<List<LyricResolveResult>> ResolveFirstAsync(CancellationToken cancellationToken)
        {
            FirstSearchStarted.TrySetResult();
            var result = await _firstSearch.Task;
            FirstRequestWasCanceled = cancellationToken.IsCancellationRequested;
            FirstSearchReturned.TrySetResult();
            return result;
        }

        private static List<LyricResolveResult> CreateResult(string line) =>
        [
            new LyricResolveResult(
                "Test",
                new LyricDocument([new LyricLine(TimeSpan.Zero, line)]),
                LyricAcquisitionKind.Remote,
                0)
        ];
    }
}
