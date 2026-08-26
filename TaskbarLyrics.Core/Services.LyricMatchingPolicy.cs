namespace TaskbarLyrics.Core.Services;

public static class LyricMatchingPolicy
{
    public const int MinimumAcceptedMatchScore = 80;
    public const int ImmediateAcceptanceScore = 90;

    // 跨语言歌手候选只是未验证的别名假设：保持可准入（>= MinimumAcceptedMatchScore），
    // 但封顶在立即接受线之下，避免其压过歌手证据确凿的同语言候选。
    public const int CrossScriptArtistMaxScore = ImmediateAcceptanceScore - 1;

    public static readonly TimeSpan DurationConflictThreshold = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan OnlineSourceTimeout = TimeSpan.FromSeconds(5);
}
