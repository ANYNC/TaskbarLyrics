namespace TaskbarLyrics.Core.Models;

public sealed record LyricResolveResult(
    LyricDocument? Document,
    string? SourceApp);
