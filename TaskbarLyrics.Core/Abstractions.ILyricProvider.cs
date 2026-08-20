using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricProvider
{
    string SourceApp { get; }

    Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);

    async Task<LyricFetchResult> GetLyricsWithDiagnosticsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var document = await GetLyricsAsync(track, cancellationToken);
        return new LyricFetchResult(
            document,
            document is null ? LyricAcquisitionKind.NotFound : LyricAcquisitionKind.Unknown,
            stopwatch.ElapsedMilliseconds);
    }
}
