using System.Diagnostics;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class PipelineLyricProviderRegistry : ILyricProviderRegistry, IDisposable
{
    private readonly ILyricResolutionCoordinator _coordinator;
    private bool _isDisposed;

    public PipelineLyricProviderRegistry(ILyricResolutionCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public async Task<List<LyricResolveResult>> ResolveLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var stopwatch = Stopwatch.StartNew();
        var resolved = await _coordinator.ResolveAsync(track, cancellationToken);
        if (resolved is null)
        {
            return [];
        }

        var document = ResolvedLyricsCompatibilityProjector.ToLyricDocument(
            resolved,
            includeInformationLines: false);
        return
        [
            new LyricResolveResult(
                resolved.ProviderId.Value,
                document,
                resolved.Acquisition,
                stopwatch.ElapsedMilliseconds)
        ];
    }

    public async Task<LyricDocument?> GetLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var results = await ResolveLyricsAsync(track, cancellationToken);
        return results.FirstOrDefault()?.Document;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _coordinator.Dispose();
    }
}
