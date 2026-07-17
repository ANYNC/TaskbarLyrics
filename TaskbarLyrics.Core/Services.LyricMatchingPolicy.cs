using System.Collections.Generic;

namespace TaskbarLyrics.Core.Services;

public static class LyricMatchingPolicy
{
    public const int MinimumAcceptedMatchScore = 70;
    public const int OfficialImmediateAcceptScore = 90;
    public const int FallbackOverrideMargin = 8;
    public const int FallbackSoftWaitScore = 85;
    public const int FallbackImmediateExitScore = 95;

    public static readonly TimeSpan FallbackSoftWait = TimeSpan.FromMilliseconds(800);
    public static readonly TimeSpan LocalProviderTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan OfficialSourceTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FallbackProviderTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan QqMusicUnreliableDurationThreshold = TimeSpan.FromSeconds(61);

    public static readonly IReadOnlyDictionary<string, int> SourceQualityWeights =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Local"] = 10,
            ["QQMusic"] = 6,
            ["Netease"] = 3,
            ["Kugou"] = 2,
            ["LRCLIB"] = 1
        };

}
