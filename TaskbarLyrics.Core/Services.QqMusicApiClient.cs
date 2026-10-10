using System.Globalization;
using System.Net.Http;
using System.Text;

namespace TaskbarLyrics.Core.Services;

/// <summary>
/// QQ 音乐歌词通道的 HTTP 边界：只负责发请求、取响应文本，不做任何解析。
/// 请求超时由调用方的取消标记控制（协调器按源超时统一取消），这里不额外设置短超时，
/// 以免把源超时归类成请求超时。
/// </summary>
internal static class QqMusicApiClient
{
    private const string SearchEndpoint = "https://c.y.qq.com/soso/fcgi-bin/search_for_qq_cp";
    private const string LyricDownloadEndpoint = "https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg";
    private const string LegacyLyricEndpoint = "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg";
    private const string CallbackName = "MusicJsonCallback_lrc";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private const string Referer = "https://y.qq.com/";

    private const int SearchPageSize = 10;

    private static readonly HttpClient Client = new();

    public static Task<string?> SearchAsync(string keyword, CancellationToken cancellationToken)
    {
        var query = string.Join(
            '&',
            "w=" + Uri.EscapeDataString(keyword),
            "format=json",
            "n=" + SearchPageSize.ToString(CultureInfo.InvariantCulture),
            "p=1",
            "t=0",
            "remoteplace=txt.yqq.song",
            "platform=yqq.json");
        var url = $"{SearchEndpoint}?{query}";

        return SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, url),
            cancellationToken);
    }

    public static Task<string?> DownloadLyricsAsync(string songId, CancellationToken cancellationToken)
    {
        return SendWithRetryAsync(
            () => CreateRequest(
                HttpMethod.Post,
                LyricDownloadEndpoint,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["version"] = "15",
                    ["miniversion"] = "100",
                    ["lrctype"] = "4",
                    ["musicid"] = songId
                }),
            cancellationToken);
    }

    public static Task<string?> DownloadLegacyLyricAsync(string songMid, CancellationToken cancellationToken)
    {
        return SendWithRetryAsync(
            () => CreateRequest(
                HttpMethod.Post,
                LegacyLyricEndpoint,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["callback"] = CallbackName,
                    ["pcachetime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        .ToString(CultureInfo.InvariantCulture),
                    ["songmid"] = songMid,
                    ["g_tk"] = "5381",
                    ["jsonpCallback"] = CallbackName,
                    ["loginUin"] = "0",
                    ["hostUin"] = "0",
                    ["format"] = "jsonp",
                    ["inCharset"] = "utf8",
                    ["outCharset"] = "utf8",
                    ["notice"] = "0",
                    ["platform"] = "yqq",
                    ["needNewCode"] = "0"
                }),
            cancellationToken);
    }

    private static async Task<string?> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendOnceAsync(requestFactory, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            return await SendOnceAsync(requestFactory, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> SendOnceAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // QQ 的歌词接口返回 text/html 且不带 charset，固定按 UTF-8 读取，避免平台默认编码差异。
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        Dictionary<string, string>? form = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", Referer);
        return request;
    }
}
