namespace TaskbarLyrics.Core.Services;

public static class LyricMatchingPolicy
{
    public const int MinimumAcceptedMatchScore = 70;
    public static readonly TimeSpan DurationConflictThreshold = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan OnlineSourceTimeout = TimeSpan.FromSeconds(5);
}
