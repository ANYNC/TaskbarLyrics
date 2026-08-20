namespace TaskbarLyrics.Core.Models;

public sealed record RawLyricCacheEnvelope(
    int CacheVersion,
    string ProviderId,
    string CandidateId,
    LyricPayloadFormat Format,
    string PayloadHash,
    DateTimeOffset FetchedAtUtc,
    string? OriginalLyrics,
    string? TranslationLyrics,
    bool IsEncrypted,
    bool IsPureMusic,
    IReadOnlyDictionary<string, string> Diagnostics);

public sealed record ParsedLyricCacheEnvelope(
    int CacheVersion,
    string ProviderId,
    string CandidateId,
    string RawPayloadHash,
    string ParserId,
    string ParserVersion,
    string NormalizationVersion,
    ParsedLyrics Content);
