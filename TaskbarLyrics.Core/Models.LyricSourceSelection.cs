namespace TaskbarLyrics.Core.Models;

/// <summary>Enabled online sources in selection order, with an optional resolved-cache scope.</summary>
public sealed record LyricSourceSelection(
    IReadOnlyList<LyricProviderId> Order,
    string? CacheContext)
{
    public const string ManualCacheContext = "user-binding-v1";
}
