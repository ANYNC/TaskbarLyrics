using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.Adapters.Netease;

public sealed class NeteaseStandardLyricProvider : LrcLibSmtcLyricProviderBase
{
    private const string ProviderCacheFileName = "netease-lyrics.json";

    public NeteaseStandardLyricProvider() : base("Netease", ProviderCacheFileName)
    {
    }

    public static void ClearCache()
    {
        ClearCacheFile(ProviderCacheFileName);
    }
}
