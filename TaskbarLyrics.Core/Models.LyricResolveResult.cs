namespace TaskbarLyrics.Core.Models;

public enum LyricAcquisitionKind
{
    Unknown,
    Searching,
    MemoryCache,
    DiskCache,
    Remote,
    LocalFile,
    SongMapping,
    NotFound
}

public sealed record LyricFetchResult(
    LyricDocument? Document,
    LyricAcquisitionKind Acquisition,
    long ElapsedMilliseconds);

public sealed record LyricResolveResult(
    string SourceApp,
    LyricDocument? Document,
    LyricAcquisitionKind Acquisition = LyricAcquisitionKind.Unknown,
    long ElapsedMilliseconds = 0);
