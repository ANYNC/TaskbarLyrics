namespace TaskbarLyrics.Core.Services;

public static class LyricMatchingPolicy
{
    public const int MinimumAcceptedMatchScore = 80;
    public const int ImmediateAcceptanceScore = 90;
    public static readonly TimeSpan DurationConflictThreshold = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan OnlineSourceTimeout = TimeSpan.FromSeconds(5);
}
