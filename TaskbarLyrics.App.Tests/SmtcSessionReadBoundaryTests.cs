using System.Runtime.InteropServices;
using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SmtcSessionReadBoundaryTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("disconnected")]
    [InlineData("disposed")]
    [InlineData("invalid-state")]
    [InlineData("aborted")]
    public async Task UnavailableSessionFallsBackAndTheNextReadCanRecover(string failure)
    {
        var fallback = new PlaybackSnapshot(false, TimeSpan.Zero, null);
        var recovered = new PlaybackSnapshot(true, TimeSpan.FromSeconds(10),
            new TrackInfo("id", "Song", "Artist", "Album", "QQMusic", TimeSpan.FromMinutes(3)));
        var calls = 0;
        var reports = 0;
        Task<PlaybackSnapshot?> Read(CancellationToken token)
        {
            if (++calls > 1) return Task.FromResult<PlaybackSnapshot?>(recovered);
            return failure switch
            {
                "missing" => Task.FromResult<PlaybackSnapshot?>(null),
                "disconnected" => throw Marshal.GetExceptionForHR(unchecked((int)0x80010108))!,
                "disposed" => throw new ObjectDisposedException("session"),
                "invalid-state" => throw new InvalidOperationException("session unavailable"),
                _ => throw new OperationCanceledException("session aborted")
            };
        }
        Assert.Same(fallback, await SmtcSessionReadBoundary.ReadAsync(Read, () => fallback, _ => reports++, default));
        Assert.Same(recovered, await SmtcSessionReadBoundary.ReadAsync(Read, () => fallback, _ => reports++, default));
        Assert.Equal(1, reports);
    }

    [Fact]
    public async Task UnusableOtherSourceFallsBackToTheCurrentBrowserCandidate()
    {
        var fallback = new PlaybackSnapshot(false, TimeSpan.Zero,
            new TrackInfo("Browser|Song|Artist", "Song", "Artist", "Album", "Browser", TimeSpan.FromMinutes(3)));
        var unusable = new PlaybackSnapshot(false, TimeSpan.Zero,
            new TrackInfo("Netease|ProcessFallback", "Unknown Title", "Unknown Artist", string.Empty, "Netease", TimeSpan.Zero));
        var reports = 0;

        var result = await SmtcSessionReadBoundary.ReadAsync(
            _ => Task.FromResult<PlaybackSnapshot?>(unusable),
            () => fallback,
            _ => reports++,
            default,
            snapshot => PlaybackInputPolicy.Classify(snapshot) == PlaybackInputKind.ValidTrack);

        Assert.Same(fallback, result);
        Assert.Equal(0, reports);
    }

    [Fact]
    public async Task UsablePausedOtherSourceStillWinsOverTheBrowserFallback()
    {
        var fallback = new PlaybackSnapshot(false, TimeSpan.Zero,
            new TrackInfo("Browser|Song|Artist", "Song", "Artist", "Album", "Browser", TimeSpan.FromMinutes(3)));
        var pausedOtherSource = new PlaybackSnapshot(false, TimeSpan.Zero,
            new TrackInfo("Netease|Other|Singer", "Other", "Singer", "Album", "Netease", TimeSpan.FromMinutes(4)));

        var result = await SmtcSessionReadBoundary.ReadAsync(
            _ => Task.FromResult<PlaybackSnapshot?>(pausedOtherSource),
            () => fallback,
            _ => Assert.Fail("A usable snapshot must not be reported as unavailable."),
            default,
            snapshot => PlaybackInputPolicy.Classify(snapshot) == PlaybackInputKind.ValidTrack);

        Assert.Same(pausedOtherSource, result);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedToAnEmptyPlaybackSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var reports = 0;
        var fallbacks = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SmtcSessionReadBoundary.ReadAsync(
            token =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<PlaybackSnapshot?>(token);
            },
            () => { fallbacks++; return new PlaybackSnapshot(false, TimeSpan.Zero, null); },
            _ => reports++, cancellation.Token));
        Assert.Equal(0, reports);
        Assert.Equal(0, fallbacks);
    }

    [Fact]
    public async Task UnexpectedProgrammingFailuresAreNotSwallowed()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => SmtcSessionReadBoundary.ReadAsync(
            _ => throw new NotSupportedException("unexpected operation"),
            () => new PlaybackSnapshot(false, TimeSpan.Zero, null),
            _ => Assert.Fail("Unexpected failures must propagate"), default));
    }
}
