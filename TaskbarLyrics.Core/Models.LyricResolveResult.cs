namespace TaskbarLyrics.Core.Models;

public sealed record LyricResolveResult(
    string SourceApp,
    LyricDocument? Document);
