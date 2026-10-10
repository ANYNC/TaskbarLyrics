using System.Globalization;
using System.Net.Http;
using System.Text;

namespace TaskbarLyrics.Core.Services;

/// <summary>
/// QQ 音乐歌词通道的 HTTP 边界：只负责发请求、取响应文本，不做任何解析。
/// 请求超时由调用方的取消标记控制（协调器按源超时统一取消），这里不额外设置短超时，
/// 以免把源超时归类成请求超时。
/// </summary>
internal sealed class QqMusicApiClient : IDisposable
{
    private const string SearchEndpoint = "https://c.y.qq.com/soso/fcgi-bin/search_for_qq_cp";
    private const string LyricDownloadEndpoint = "https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg";
    private const string LegacyLyricEndpoint = "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg";
    private const string SongDetailEndpoint = "https://c.y.qq.com/v8/fcg-bin/fcg_play_single_song.fcg";
    private const string CallbackName = "MusicJsonCallback_lrc";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private const string Referer = "https://y.qq.com/";

    private const int SearchPageSize = 10;

    private readonly object _clientLock = new();
    private readonly Func<HttpClient> _createClient;
    private ClientState _client;
    private bool _disposed;

    public QqMusicApiClient() : this(static () => new HttpClient())
    {
    }

    internal QqMusicApiClient(Func<HttpClient> createClient)
    {
        _createClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
        _client = new ClientState(_createClient());
    }

    public Task<string?> SearchAsync(string keyword, CancellationToken cancellationToken)
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

    public Task<string?> DownloadLyricsAsync(string songId, CancellationToken cancellationToken)
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

    public Task<string?> DownloadLegacyLyricAsync(string songMid, CancellationToken cancellationToken)
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

    public Task<string?> GetSongAsync(string songIdOrMid, CancellationToken cancellationToken)
    {
        const string callback = "getOneSongInfoCallback";
        return SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Post, SongDetailEndpoint,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [QqMusicResponseMapper.IsUsableSongId(songIdOrMid) ? "songid" : "songmid"] = songIdOrMid.Trim(),
                    ["tpl"] = "yqq_song_detail",
                    ["format"] = "jsonp",
                    ["callback"] = callback,
                    ["jsonpCallback"] = callback,
                    ["g_tk"] = "5381",
                    ["loginUin"] = "0",
                    ["hostUin"] = "0",
                    ["outCharset"] = "utf8",
                    ["notice"] = "0",
                    ["platform"] = "yqq",
                    ["needNewCode"] = "0"
                }),
            cancellationToken);
    }

    private async Task<string?> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = AcquireClient();
            try
            {
                return await SendOnceAsync(client.Client, requestFactory, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (
                attempt == 0 && !cancellationToken.IsCancellationRequested &&
                LyricTransientFailurePolicy.IsTransient(exception))
            {
                RefreshIfCurrent(client);
            }
            finally
            {
                ReleaseClient(client);
            }
        }
    }

    private static async Task<string> SendOnceAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // QQ 的歌词接口返回 text/html 且不带 charset，固定按 UTF-8 读取。
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private ClientState AcquireClient()
    {
        lock (_clientLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _client.ActiveRequests++;
            return _client;
        }
    }

    private void RefreshIfCurrent(ClientState failedClient)
    {
        lock (_clientLock)
        {
            if (!_disposed && ReferenceEquals(_client, failedClient))
            {
                // 重建 handler 以重新读取系统代理；旧请求完成前保留旧客户端。
                var replacement = new ClientState(_createClient());
                failedClient.Retired = true;
                _client = replacement;
            }
        }
    }

    private void ReleaseClient(ClientState client)
    {
        bool dispose;
        lock (_clientLock)
        {
            client.ActiveRequests--;
            dispose = client.Retired && client.ActiveRequests == 0;
        }

        if (dispose)
        {
            client.Client.Dispose();
        }
    }

    public void Dispose()
    {
        ClientState client;
        bool dispose;
        lock (_clientLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            client = _client;
            client.Retired = true;
            dispose = client.ActiveRequests == 0;
        }

        if (dispose)
        {
            client.Client.Dispose();
        }
    }

    private sealed class ClientState(HttpClient client)
    {
        public HttpClient Client { get; } = client;
        public int ActiveRequests { get; set; }
        public bool Retired { get; set; }
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
