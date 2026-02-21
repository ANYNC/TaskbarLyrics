using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.Adapters.QQMusic;

public sealed class QQMusicStandardLyricProvider : LrcLibSmtcLyricProviderBase
{
    private const string ProviderCacheFileName = "qqmusic-lyrics.json";

    public QQMusicStandardLyricProvider() : base("QQMusic", ProviderCacheFileName)
    {
    }

    public static void ClearCache()
    {
        ClearCacheFile(ProviderCacheFileName);
    }
}
