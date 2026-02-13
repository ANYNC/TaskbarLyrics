using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

public sealed class MockMusicSessionProvider : IMusicSessionProvider
{
    private static readonly TrackInfo DemoTrack = new(
        Id: "netease-demo-001",
        Title: "MVP Demo",
        Artist: "TaskbarLyrics",
        SourceApp: "Netease");

    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public Task<PlaybackSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var elapsed = DateTimeOffset.UtcNow - _startedAt;

        // Loop demo timeline every 25 seconds.
        var position = TimeSpan.FromSeconds(elapsed.TotalSeconds % 25);

        var snapshot = new PlaybackSnapshot(
            IsPlaying: true,
            Position: position,
            Track: DemoTrack);

        return Task.FromResult(snapshot);
    }
}
