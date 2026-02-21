namespace TaskbarLyrics.Core.Services;

public sealed class GenericSmtcLyricProvider : LrcLibSmtcLyricProviderBase
{
    private const string GenericCacheFileName = "smtc-generic-lyrics.json";

    public GenericSmtcLyricProvider() : base("*", GenericCacheFileName, strictSourceMatch: false)
    {
    }

    public static void ClearCache()
    {
        ClearCacheFile(GenericCacheFileName);
    }
}
