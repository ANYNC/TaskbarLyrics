namespace TaskbarLyrics.App;

internal static class LyricsWebMessageRouter
{
    public static WebViewMessage? Parse(string? messageJson)
    {
        return WebViewMessageRouter.Parse(messageJson);
    }
}
