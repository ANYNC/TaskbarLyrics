using System.Net.Http;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Searchers;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class QqMusicLyricSource : ILyricSource
{
    public LyricProviderId ProviderId => KnownLyricProviders.QQMusic;

    public async Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
        LyricSearchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (ProviderSongIdPolicy.CanUseDirectSongId(plan.OriginalTrack, ProviderId))
        {
            return
            [
                CreateCandidate(
                    plan.OriginalTrack.SongId!,
                    plan.OriginalTrack.Title,
                    plan.OriginalTrack.Artists,
                    plan.OriginalTrack.Album,
                    plan.OriginalTrack.Duration,
                    "direct-song-id",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["direct"] = "true"
                    })
            ];
        }

        return await LyricSearchStageExecutor.ExecuteAsync(
            plan,
            async (variant, token) =>
            {
                var response = await LyricifyTask.WaitWithProxyRecoveryAsync(
                    () => ProviderHelper.QQMusicApi.Search(
                        BuildQuery(variant),
                        Lyricify.Lyrics.Providers.Web.QQMusic.Api.SearchTypeEnum.SONG_ID),
                    token);
                var songs = response?.Req_1?.Data?.Body?.Song?.List ?? [];
                return songs
                    .Select(song => MapSong(song, variant))
                    .Where(candidate => candidate is not null)
                    .Cast<SourceTrackCandidate>()
                    .ToArray();
            },
            cancellationToken);
    }

    public async Task<RawLyricPayload?> FetchAsync(
        SourceTrackCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var qrc = await LyricifyTask.WaitWithProxyRecoveryAsync(
            () => ProviderHelper.QQMusicApi.GetLyricsAsync(candidate.CandidateId),
            cancellationToken);
        var qrcPayload = MapQrc(candidate, qrc);
        if (qrcPayload is not null)
        {
            return qrcPayload;
        }

        if (!candidate.FetchMetadata.TryGetValue("mid", out var mid) ||
            string.IsNullOrWhiteSpace(mid))
        {
            return null;
        }

        var lrc = await LyricifyTask.WaitWithProxyRecoveryAsync(
            () => ProviderHelper.QQMusicApi.GetLyric(mid),
            cancellationToken);
        return MapLrc(candidate, lrc);
    }

    private SourceTrackCandidate CreateCandidate(
        string candidateId,
        string title,
        IReadOnlyList<string> artists,
        string album,
        TimeSpan duration,
        string variantId,
        IReadOnlyDictionary<string, string> metadata) =>
        new(ProviderId, candidateId, title, artists, album, duration, variantId, metadata);

    internal static SourceTrackCandidate? MapSong(
        Lyricify.Lyrics.Providers.Web.QQMusic.Song song,
        SearchQueryVariant variant)
    {
        if (string.IsNullOrWhiteSpace(song.Id))
        {
            return null;
        }

        return new SourceTrackCandidate(
            KnownLyricProviders.QQMusic,
            song.Id,
            string.IsNullOrWhiteSpace(song.Title) ? song.Name : song.Title,
            song.Singer?.Select(singer => singer.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? [],
            song.Album?.Name ?? string.Empty,
            TimeSpan.FromSeconds(Math.Max(0, song.Interval)),
            variant.Id,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mid"] = song.Mid ?? string.Empty
            });
    }

    internal static RawLyricPayload? MapQrc(
        SourceTrackCandidate candidate,
        Lyricify.Lyrics.Decrypter.Qrc.QqLyricsResponse? response) =>
        string.IsNullOrWhiteSpace(response?.Lyrics)
            ? null
            : CreatePayload(candidate, LyricPayloadFormat.Qrc, response.Lyrics, response.Trans);

    internal static RawLyricPayload? MapLrc(
        SourceTrackCandidate candidate,
        Lyricify.Lyrics.Providers.Web.QQMusic.LyricResult? response) =>
        string.IsNullOrWhiteSpace(response?.Lyric)
            ? null
            : CreatePayload(candidate, LyricPayloadFormat.Lrc, response.Lyric, response.Trans);

    private static RawLyricPayload CreatePayload(
        SourceTrackCandidate candidate,
        LyricPayloadFormat format,
        string original,
        string? translation) =>
        new(KnownLyricProviders.QQMusic, candidate.CandidateId, format, original, translation, false, false,
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static string BuildQuery(SearchQueryVariant variant) =>
        string.Join(' ', new[] { variant.Title }.Concat(variant.Artists));
}

public sealed class KugouLyricSource : ILyricSource
{
    public LyricProviderId ProviderId => KnownLyricProviders.Kugou;

    public async Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
        LyricSearchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (ProviderSongIdPolicy.CanUseDirectSongId(plan.OriginalTrack, ProviderId))
        {
            var directSong = new Lyricify.Lyrics.Providers.Web.Kugou.SearchSongResponse.DataItem.InfoItem
            {
                Hash = plan.OriginalTrack.SongId!,
                SongName = plan.OriginalTrack.Title,
                SingerName = string.Join(", ", plan.OriginalTrack.Artists),
                AlbumName = plan.OriginalTrack.Album,
                Duration = (int)Math.Clamp(
                    plan.OriginalTrack.Duration.TotalMilliseconds,
                    0,
                    int.MaxValue)
            };
            var directVariant = plan.Variants[0];
            var directLyrics = await SearchLyricCandidatesAsync(directSong.Hash, cancellationToken);
            return directLyrics
                .Select(lyric => MapCandidate(directSong, lyric, directVariant))
                .Where(candidate => candidate is not null)
                .Cast<SourceTrackCandidate>()
                .ToArray();
        }

        return await LyricSearchStageExecutor.ExecuteAsync(
            plan,
            async (variant, token) =>
            {
                var response = await LyricifyTask.WaitWithProxyRecoveryAsync(
                    () => ProviderHelper.KugouApi.GetSearchSong(BuildQuery(variant)),
                    token);
                var candidates = new List<SourceTrackCandidate>();
                foreach (var song in response?.Data?.Info ?? [])
                {
                    if (string.IsNullOrWhiteSpace(song.Hash))
                    {
                        continue;
                    }

                    var lyricCandidates = await SearchLyricCandidatesAsync(song.Hash, token);
                    foreach (var lyric in lyricCandidates)
                    {
                        var candidate = MapCandidate(song, lyric, variant);
                        if (candidate is not null)
                        {
                            candidates.Add(candidate);
                        }
                    }
                }

                return candidates;
            },
            cancellationToken);
    }

    public async Task<RawLyricPayload?> FetchAsync(
        SourceTrackCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.FetchMetadata.TryGetValue("access-key", out var accessKey) ||
            string.IsNullOrWhiteSpace(accessKey))
        {
            return null;
        }

        var krc = await LyricifyTask.WaitAsync(
            Lyricify.Lyrics.Decrypter.Krc.Helper.GetLyricsAsync(candidate.CandidateId, accessKey),
            cancellationToken);
        return MapKrc(candidate, krc);
    }

    internal static SourceTrackCandidate? MapCandidate(
        Lyricify.Lyrics.Providers.Web.Kugou.SearchSongResponse.DataItem.InfoItem song,
        Lyricify.Lyrics.Providers.Web.Kugou.SearchLyricsResponse.Candidate lyric,
        SearchQueryVariant variant)
    {
        if (string.IsNullOrWhiteSpace(lyric.Id) || string.IsNullOrWhiteSpace(lyric.AccessKey))
        {
            return null;
        }

        return new SourceTrackCandidate(
            KnownLyricProviders.Kugou,
            lyric.Id,
            string.IsNullOrWhiteSpace(lyric.Song) ? song.SongName : lyric.Song,
            SplitArtists(string.IsNullOrWhiteSpace(lyric.Singer) ? song.SingerName : lyric.Singer),
            song.AlbumName ?? string.Empty,
            TimeSpan.FromMilliseconds(Math.Max(0, lyric.Duration)),
            variant.Id,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["access-key"] = lyric.AccessKey,
                ["song-hash"] = song.Hash
            });
    }

    internal static RawLyricPayload? MapKrc(SourceTrackCandidate candidate, string? krc) =>
        string.IsNullOrWhiteSpace(krc)
            ? null
            : new RawLyricPayload(
                KnownLyricProviders.Kugou,
                candidate.CandidateId,
                LyricPayloadFormat.Krc,
                krc,
                null,
                false,
                false,
                new Dictionary<string, string>(StringComparer.Ordinal));

    private static async Task<IReadOnlyList<Lyricify.Lyrics.Providers.Web.Kugou.SearchLyricsResponse.Candidate>>
        SearchLyricCandidatesAsync(string hash, CancellationToken cancellationToken)
    {
        var response = await LyricifyTask.WaitWithProxyRecoveryAsync(
            () => ProviderHelper.KugouApi.GetSearchLyrics(hash: hash),
            cancellationToken);
        if (response?.Candidates is { Count: > 0 })
        {
            return response.Candidates;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        response = await LyricifyTask.WaitWithProxyRecoveryAsync(
            () => ProviderHelper.KugouApi.GetSearchLyrics(hash: hash),
            cancellationToken);
        return response?.Candidates ?? [];
    }

    private static string[] SplitArtists(string? artists) =>
        string.IsNullOrWhiteSpace(artists)
            ? []
            : artists.Split(
                ['&', '/', '、', '，', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string BuildQuery(SearchQueryVariant variant) =>
        string.Join(' ', new[] { variant.Title }.Concat(variant.Artists));
}

public sealed class NeteaseLyricSource : ILyricSource
{
    public LyricProviderId ProviderId => KnownLyricProviders.Netease;

    public async Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
        LyricSearchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (ProviderSongIdPolicy.CanUseDirectSongId(plan.OriginalTrack, ProviderId))
        {
            return
            [
                new SourceTrackCandidate(
                    ProviderId,
                    plan.OriginalTrack.SongId!,
                    plan.OriginalTrack.Title,
                    plan.OriginalTrack.Artists,
                    plan.OriginalTrack.Album,
                    plan.OriginalTrack.Duration,
                    "direct-song-id",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["direct"] = "true"
                    })
            ];
        }

        return await LyricSearchStageExecutor.ExecuteAsync(
            plan,
            async (variant, token) =>
            {
                var response = await LyricifyTask.WaitWithProxyRecoveryAsync(
                    () => ProviderHelper.NeteaseApi.SearchNew(BuildQuery(variant)),
                    token);
                return (response?.Result?.Songs ?? [])
                    .Select(song => MapSong(song, variant))
                    .Where(candidate => candidate is not null)
                    .Cast<SourceTrackCandidate>()
                    .ToArray();
            },
            cancellationToken);
    }

    public async Task<RawLyricPayload?> FetchAsync(
        SourceTrackCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var response = await LyricifyTask.WaitWithProxyRecoveryAsync(
            () => ProviderHelper.NeteaseApi.GetLyricNew(candidate.CandidateId),
            cancellationToken);
        return MapLyrics(candidate, response);
    }

    internal static SourceTrackCandidate? MapSong(
        Lyricify.Lyrics.Providers.Web.Netease.Song song,
        SearchQueryVariant variant)
    {
        var result = new NeteaseSearchResult(song);
        if (string.IsNullOrWhiteSpace(result.Id))
        {
            return null;
        }

        return new SourceTrackCandidate(
            KnownLyricProviders.Netease,
            result.Id,
            result.Title,
            result.Artists,
            result.Album,
            TimeSpan.FromMilliseconds(Math.Max(0, result.DurationMs ?? 0)),
            variant.Id,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    internal static RawLyricPayload? MapLyrics(
        SourceTrackCandidate candidate,
        Lyricify.Lyrics.Providers.Web.Netease.LyricResult? response)
    {
        if (response is null || response.Nolyric || response.Uncollected)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(response.Yrc?.Lyric))
        {
            return new RawLyricPayload(
                KnownLyricProviders.Netease,
                candidate.CandidateId,
                LyricPayloadFormat.Yrc,
                response.Yrc.Lyric,
                response.Ytlrc?.Lyric ?? response.Tlyric?.Lyric,
                false,
                false,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        return string.IsNullOrWhiteSpace(response.Lrc?.Lyric)
            ? null
            : new RawLyricPayload(
                KnownLyricProviders.Netease,
                candidate.CandidateId,
                LyricPayloadFormat.Lrc,
                response.Lrc.Lyric,
                response.Tlyric?.Lyric,
                false,
                false,
                new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static string BuildQuery(SearchQueryVariant variant) =>
        string.Join(' ', new[] { variant.Title }.Concat(variant.Artists));
}

public sealed class LrcLibLyricSource : ILyricSource
{
    public LyricProviderId ProviderId => KnownLyricProviders.LrcLib;

    public async Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
        LyricSearchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return await LyricSearchStageExecutor.ExecuteAsync(
            plan,
            async (variant, token) =>
            {
                var response = await LyricifyTask.WaitWithProxyRecoveryAsync(
                    () => ProviderHelper.LRCLIBApi.Search(
                        variant.Title,
                        string.Join(", ", variant.Artists),
                        variant.Album,
                        variant.Duration > TimeSpan.Zero ? variant.Duration.TotalSeconds : null),
                    token);
                return (response ?? [])
                    .Select(item => MapSearchResult(item, variant))
                    .Where(candidate => candidate is not null)
                    .Cast<SourceTrackCandidate>()
                    .ToArray();
            },
            cancellationToken);
    }

    public async Task<RawLyricPayload?> FetchAsync(
        SourceTrackCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!int.TryParse(
                candidate.CandidateId,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var candidateId))
        {
            return null;
        }

        var response = await LyricifyTask.WaitWithProxyRecoveryAsync(
            () => ProviderHelper.LRCLIBApi.GetById(candidateId),
            cancellationToken);
        return MapLyrics(candidate, response);
    }

    internal static SourceTrackCandidate? MapSearchResult(
        Lyricify.Lyrics.Providers.Web.LRCLIB.SearchResultItem item,
        SearchQueryVariant variant)
    {
        if (item.Id <= 0)
        {
            return null;
        }

        var id = item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new SourceTrackCandidate(
            KnownLyricProviders.LrcLib,
            id,
            item.TrackName,
            string.IsNullOrWhiteSpace(item.ArtistName) ? [] : [item.ArtistName],
            item.AlbumName ?? string.Empty,
            TimeSpan.FromSeconds(Math.Max(0, item.Duration)),
            variant.Id,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    internal static RawLyricPayload? MapLyrics(
        SourceTrackCandidate candidate,
        Lyricify.Lyrics.Providers.Web.LRCLIB.GetLyricResult? response)
    {
        if (response is null)
        {
            return null;
        }

        var format = !string.IsNullOrWhiteSpace(response.SyncedLyrics)
            ? LyricPayloadFormat.Lrc
            : LyricPayloadFormat.PlainText;
        var lyrics = format == LyricPayloadFormat.Lrc
            ? response.SyncedLyrics
            : response.PlainLyrics;
        if (!response.Instrumental && string.IsNullOrWhiteSpace(lyrics))
        {
            return null;
        }

        return new RawLyricPayload(
            KnownLyricProviders.LrcLib,
            candidate.CandidateId,
            format,
            lyrics,
            null,
            false,
            response.Instrumental,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }
}

internal static class LyricifyTask
{
    private static readonly LyricifyProxyRecovery ProxyRecovery = LyricifyProxyRecovery.CreateDefault();

    public static async Task<T> WaitAsync<T>(Task<T> helperTask, CancellationToken cancellationToken)
    {
        try
        {
            return await helperTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = helperTask.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    public static Task<T> WaitWithProxyRecoveryAsync<T>(
        Func<Task<T>> taskFactory,
        CancellationToken cancellationToken) =>
        WaitWithProxyRecoveryAsync(taskFactory, ProxyRecovery, cancellationToken);

    internal static async Task<T> WaitWithProxyRecoveryAsync<T>(
        Func<Task<T>> taskFactory,
        LyricifyProxyRecovery proxyRecovery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(taskFactory);
        ArgumentNullException.ThrowIfNull(proxyRecovery);

        var failedClient = proxyRecovery.GetCurrentClient();
        try
        {
            return await WaitAsync(taskFactory(), cancellationToken);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            proxyRecovery.RefreshIfCurrent(failedClient);
            return await WaitAsync(taskFactory(), cancellationToken);
        }
    }
}

internal sealed class LyricifyProxyRecovery
{
    private readonly object _refreshLock = new();
    private readonly Func<HttpClient> _getCurrentClient;
    private readonly Action _clearProxy;

    public LyricifyProxyRecovery(Func<HttpClient> getCurrentClient, Action clearProxy)
    {
        ArgumentNullException.ThrowIfNull(getCurrentClient);
        ArgumentNullException.ThrowIfNull(clearProxy);
        _getCurrentClient = getCurrentClient;
        _clearProxy = clearProxy;
    }

    public static LyricifyProxyRecovery CreateDefault() =>
        new(
            static () => Lyricify.Lyrics.Providers.Web.BaseApi.HttpClient,
            static () => Lyricify.Lyrics.Providers.Web.Proxy.ClearProxy());

    public HttpClient GetCurrentClient() => _getCurrentClient();

    public void RefreshIfCurrent(HttpClient failedClient)
    {
        ArgumentNullException.ThrowIfNull(failedClient);
        lock (_refreshLock)
        {
            if (ReferenceEquals(_getCurrentClient(), failedClient))
            {
                _clearProxy();
            }
        }
    }
}
