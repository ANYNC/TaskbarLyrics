using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class CoverRefreshCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LateArtworkCorrectsTheFirstSuccessfulReadAndThenStopsRechecking()
    {
        var now = Start;
        using var cache = new CoverRefreshCache(() => now);
        byte[] oldCover = [1, 2, 3];
        byte[] correctedCover = [4, 5, 6];
        var publishedCover = oldCover;
        var reads = 0;
        Task<byte[]?> Read(CancellationToken _) { reads++; return Task.FromResult<byte[]?>(publishedCover); }

        cache.GetCover("B", "B metadata", Read);
        Assert.Same(oldCover, cache.GetCover("B", "B metadata", Read).Bytes);
        foreach (var milliseconds in new[] { 499, 500, 1499, 1500, 2999, 3000, 4999, 5000 })
        {
            now = Start.AddMilliseconds(milliseconds);
            if (milliseconds >= 1500) publishedCover = correctedCover;
            cache.GetCover("B", "B metadata", Read);
        }
        Assert.Same(correctedCover, cache.GetCover("B", "B metadata", Read).Bytes);
        Assert.Equal(5, reads);
        now = Start.AddMinutes(10);
        Assert.Same(correctedCover, cache.GetCover("B", "B metadata", Read).Bytes);
        Assert.Equal(5, reads);
    }

    [Fact]
    public async Task EqualHashKeepsTheSameBytesWithoutMarkingCachedArtworkAsLoading()
    {
        var now = Start;
        using var cache = new CoverRefreshCache(() => now);
        cache.GetCover("A", "metadata", _ => Task.FromResult<byte[]?>([1, 2, 3]));
        var original = cache.GetCover("A", "metadata", null).Bytes;
        var pending = new TaskCompletionSource<byte[]?>();
        now = Start.AddMilliseconds(500);
        var refreshing = cache.GetCover("A", "metadata", _ => pending.Task);
        Assert.Same(original, refreshing.Bytes);
        Assert.False(refreshing.IsLoading);
        await Task.Run(() => pending.SetResult([1, 2, 3]));
        Assert.Same(original, cache.GetCover("A", "metadata", null).Bytes);
    }

    [Fact]
    public void FailedRecheckKeepsArtworkAndMissingArtworkStillRetriesAfterTheConfirmationWindow()
    {
        var now = Start;
        using var cache = new CoverRefreshCache(() => now);
        byte[] cover = [1, 2];
        cache.GetCover("A", "metadata", _ => Task.FromResult<byte[]?>(cover));
        now = Start.AddSeconds(5);
        cache.GetCover("A", "metadata", _ => Task.FromResult<byte[]?>(null));
        Assert.Same(cover, cache.GetCover("A", "metadata", null).Bytes);

        cache.GetCover("B", "metadata", _ => Task.FromResult<byte[]?>(null));
        now = now.AddSeconds(5);
        cache.GetCover("B", "metadata", _ => Task.FromResult<byte[]?>(null));
        now = now.AddSeconds(5);
        cache.GetCover("B", "metadata", _ => Task.FromResult<byte[]?>(cover));
        Assert.Same(cover, cache.GetCover("B", "metadata", null).Bytes);
    }

    [Fact]
    public async Task RapidReturnToTheSameSongDiscardsTheFirstRequestEvenIfItIgnoresCancellation()
    {
        using var cache = new CoverRefreshCache(() => Start);
        var oldRead = new TaskCompletionSource<byte[]?>();
        CancellationToken oldToken = default;
        cache.GetCover("A", "A metadata", token => { oldToken = token; return oldRead.Task; });
        cache.GetCover("B", "B metadata", _ => Task.FromResult<byte[]?>([2]));
        byte[] newA = [3];
        cache.GetCover("A", "A metadata", _ => Task.FromResult<byte[]?>(newA));
        Assert.True(oldToken.IsCancellationRequested);
        await Task.Run(() => oldRead.SetResult([1]));
        Assert.Same(newA, cache.GetCover("A", "A metadata", null).Bytes);
    }

    [Fact]
    public void MetadataChangeRefreshesArtworkEvenWhenThePlayerReusesItsSongId()
    {
        using var cache = new CoverRefreshCache(() => Start);
        cache.GetCover("same song id", "Title A", _ => Task.FromResult<byte[]?>([1]));
        byte[] newCover = [2];
        cache.GetCover("same song id", "Title B", _ => Task.FromResult<byte[]?>(newCover));
        Assert.Same(newCover, cache.GetCover("same song id", "Title B", null).Bytes);
    }

    [Fact]
    public async Task PendingReadIsNotDuplicatedAndMissedCheckpointsAreCoalesced()
    {
        var now = Start;
        using var cache = new CoverRefreshCache(() => now);
        var pending = new TaskCompletionSource<byte[]?>();
        var reads = 0;
        Task<byte[]?> Read(CancellationToken _) { reads++; return pending.Task; }
        Assert.True(cache.GetCover("A", "metadata", Read).IsLoading);
        now = Start.AddSeconds(4);
        Assert.True(cache.GetCover("A", "metadata", Read).IsLoading);
        Assert.Equal(1, reads);
        await Task.Run(() => pending.SetResult([1]));
        cache.GetCover("A", "metadata", _ => { reads++; return Task.FromResult<byte[]?>([1]); });
        Assert.Equal(2, reads);
        cache.GetCover("A", "metadata", Read);
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetAndDisposeCancelPendingReadsAndRejectTheirResults(bool dispose)
    {
        using var cache = new CoverRefreshCache(() => Start);
        var pending = new TaskCompletionSource<byte[]?>();
        CancellationToken token = default;
        cache.GetCover("A", "metadata", cancellation => { token = cancellation; return pending.Task; });
        if (dispose) cache.Dispose(); else cache.Reset();
        Assert.True(token.IsCancellationRequested);
        await Task.Run(() => pending.SetResult([1]));
        Assert.Null(cache.GetCover("A", "metadata", null).Bytes);
        Assert.False(cache.GetCover("A", "metadata", null).IsLoading);
    }

    [Fact]
    public void ThumbnailCanArriveAfterTheTrackWithoutAReadStartingEarly()
    {
        var now = Start;
        using var cache = new CoverRefreshCache(() => now);
        Assert.False(cache.GetCover("A", "metadata", null).IsLoading);
        now = now.AddMilliseconds(200);
        byte[] cover = [1];
        cache.GetCover("A", "metadata", _ => Task.FromResult<byte[]?>(cover));
        Assert.Same(cover, cache.GetCover("A", "metadata", null).Bytes);
    }
}
