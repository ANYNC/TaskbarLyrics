using System.Diagnostics;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Light.App;

internal sealed class DiagnosticLyricProviderRegistry : ILyricProviderRegistry
{
    private readonly ILyricProviderRegistry _inner;
    private readonly Action<TrackInfo, IReadOnlyList<LyricResolveResult>, TimeSpan> _publish;

    public DiagnosticLyricProviderRegistry(
        ILyricProviderRegistry inner,
        Action<TrackInfo, IReadOnlyList<LyricResolveResult>, TimeSpan> publish)
    {
        _inner = inner;
        _publish = publish;
    }

    public async Task<List<LyricResolveResult>> ResolveLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = await _inner.ResolveLyricsAsync(track, cancellationToken);
        stopwatch.Stop();
        _publish(track, results, stopwatch.Elapsed);
        return results;
    }

    public async Task<LyricDocument?> GetLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var results = await ResolveLyricsAsync(track, cancellationToken);
        return results
            .Where(result => result.Document is { Lines.Count: > 0 })
            .OrderByDescending(result => result.Document!.BestScore)
            .Select(result => result.Document)
            .FirstOrDefault();
    }
}
