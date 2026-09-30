using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface IContextualResolvedLyricCache : IResolvedLyricCache
{
    bool TryGet(TrackInfo track, string context, out ResolvedLyrics? resolvedLyrics);

    bool Store(TrackInfo track, ResolvedLyrics resolvedLyrics, string context);

    ResolvedLyricCacheStoreResult StoreWithResult(
        TrackInfo track,
        ResolvedLyrics resolvedLyrics,
        string context) => new(Store(track, resolvedLyrics, context), null);
}
