namespace TaskbarLyrics.Core.Services;

public sealed class LrcLibLyricProvider : LrcLibSmtcLyricProviderBase
{
    private const string ProviderCacheFileName = "lrclib-lyrics.json";

    public LrcLibLyricProvider() : base("LrcLib", ProviderCacheFileName)
    {
    }

    public static void ClearCache()
    {
        ClearCacheFile(ProviderCacheFileName);
    }
}
