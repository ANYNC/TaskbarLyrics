using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.Light.App;

internal sealed class FallbackLocalLyricProviderRegistry : ILyricProviderRegistry
{
    private readonly ILyricProviderRegistry _onlineRegistry;
    private readonly ILyricProvider _localProvider;

    public FallbackLocalLyricProviderRegistry(
        ILyricProviderRegistry onlineRegistry,
        ILyricProvider localProvider)
    {
        _onlineRegistry = onlineRegistry;
        _localProvider = localProvider;
    }

    public async Task<List<LyricResolveResult>> ResolveLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var onlineResults = await _onlineRegistry.ResolveLyricsAsync(track, cancellationToken);
        if (HasUsableDocument(onlineResults))
        {
            return onlineResults;
        }

        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        localCts.CancelAfter(LyricMatchingPolicy.LocalProviderTimeout);
        LyricDocument? localDocument = null;
        try
        {
            localDocument = await _localProvider.GetLyricsAsync(track, localCts.Token);
        }
        catch (OperationCanceledException) when (localCts.IsCancellationRequested)
        {
        }
        catch
        {
        }

        if (localDocument is null)
        {
            return onlineResults;
        }

        var results = onlineResults.ToList();
        results.Add(new LyricResolveResult(_localProvider.SourceApp, localDocument));
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

    private static bool HasUsableDocument(IEnumerable<LyricResolveResult> results)
    {
        return results.Any(result => result.Document is { Lines.Count: > 0 });
    }
}
