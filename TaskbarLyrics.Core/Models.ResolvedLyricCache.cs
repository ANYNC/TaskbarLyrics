namespace TaskbarLyrics.Core.Models;

/// <summary>
/// Versioned on-disk envelope for the final lyric cache.
/// </summary>
public sealed record ResolvedLyricCacheEnvelope(
    int Version,
    IReadOnlyDictionary<string, ResolvedLyricCacheEntry>? Entries);

/// <summary>
/// A persisted final resolution and the metadata observed when it was selected.
/// </summary>
public sealed record ResolvedLyricCacheEntry(
    string? TrackId,
    string? Title,
    string? Artist,
    string? Album,
    string? SourceApp,
    TimeSpan Duration,
    string? SongId,
    string? ProviderId,
    string? CandidateId,
    LyricAcquisitionKind Acquisition,
    IReadOnlyDictionary<string, string>? Diagnostics,
    ParsedLyrics? Content);
