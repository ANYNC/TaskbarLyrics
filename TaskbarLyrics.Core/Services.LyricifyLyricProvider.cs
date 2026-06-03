using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Providers.Web.Netease;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricifyLyricProvider : LyricProviderBase
{
    private const int MinimumMatchScore = 70;
    private static readonly HttpClient SharedHttpClient = new();
    private readonly Lyricify.Lyrics.Searchers.Searchers _searcherType;

    public LyricifyLyricProvider(
        string sourceApp,
        Lyricify.Lyrics.Searchers.Searchers searcherType) : base(SharedHttpClient)
    {
        SourceApp = sourceApp;
        _searcherType = searcherType;
    }

    public override string SourceApp { get; }

    protected override async Task<LyricDocument?> ResolveRemoteAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(track.Title) ||
            string.Equals(track.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Log.Info($"LyricifyLyricProvider [{SourceApp}] 开始检索歌词: {track.Title} - {track.Artist}");

        // 1. 发起在线歌曲搜索
        ISearchResult? searchResult = null;
        List<QQMusicSearchResult>? qqCandidates = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(track.SongId))
            {
                if (_searcherType == Lyricify.Lyrics.Searchers.Searchers.Netease && track.SourceApp.Equals("Netease", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info($"LyricifyLyricProvider [{SourceApp}] 命中 SMTC 网易云媒体 ID: {track.SongId}，直接跳转获取歌词");
                    searchResult = new NeteaseSearchResult(track.Title, new[] { track.Artist }, string.Empty, Array.Empty<string>(), (int)track.Duration.TotalMilliseconds, track.SongId);
                }
                else if (_searcherType == Lyricify.Lyrics.Searchers.Searchers.QQMusic && track.SourceApp.Equals("QQMusic", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info($"LyricifyLyricProvider [{SourceApp}] 命中 SMTC QQ音乐媒体 ID: {track.SongId}，直接跳转获取歌词");
                    searchResult = new QQMusicSearchResult(track.Title, new[] { track.Artist }, string.Empty, Array.Empty<string>(), (int)track.Duration.TotalMilliseconds, track.SongId, string.Empty);
                    qqCandidates = new List<QQMusicSearchResult> { (QQMusicSearchResult)searchResult };
                }
            }

            if (searchResult == null)
            {
                if (_searcherType == Lyricify.Lyrics.Searchers.Searchers.Netease)
                {
                    // 网易云音乐原生搜索接口已失效 (返回 -460 验证错误)
                    // 绕过 SearchHelper.Search 统一入口，直接使用 SearchNew 新接口
                    var response = await ProviderHelper.NeteaseApi.SearchNew(track.Title + " " + track.Artist);
                    if (response?.Result?.Songs != null && response.Result.Songs.Length > 0)
                    {
                        var candidates = response.Result.Songs.Select(s => new NeteaseSearchResult(s)).ToList();
                        NeteaseSearchResult? bestCandidate = null;
                        int bestScore = -1;
                        foreach (var c in candidates)
                        {
                            int score = LyricMatcher.Score(track, c.Title, string.Join(", ", c.Artists), (c.DurationMs ?? 0) / 1000);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestCandidate = c;
                            }
                        }
                        searchResult = bestCandidate;
                    }
                }
                else if (_searcherType == Lyricify.Lyrics.Searchers.Searchers.QQMusic)
                {
                    var response = await ProviderHelper.QQMusicApi.Search(
                        track.Title + " " + track.Artist,
                        Lyricify.Lyrics.Providers.Web.QQMusic.Api.SearchTypeEnum.SONG_ID);
                    var songs = response?.Req_1?.Data?.Body?.Song?.List;
                    if (songs != null)
                    {
                        qqCandidates = songs
                            .SelectMany(song => new[] { song }.Concat(song.Group ?? Enumerable.Empty<Lyricify.Lyrics.Providers.Web.QQMusic.Song>()))
                            .Select(song => new QQMusicSearchResult(song))
                            .Select(candidate => new
                            {
                                Candidate = candidate,
                                Score = ScoreCandidate(track, candidate)
                            })
                            .Where(x => x.Score >= MinimumMatchScore)
                            .OrderByDescending(x => x.Score)
                            .Select(x => x.Candidate)
                            .ToList();
                        searchResult = qqCandidates.FirstOrDefault();
                    }
                }
                else
                {
                    var searchMetadata = new TrackMultiArtistMetadata
                    {
                        Title = track.Title,
                        Artist = track.Artist,
                        Album = string.Empty,
                        DurationMs = (int)track.Duration.TotalMilliseconds
                    };

                    searchResult = await SearchHelper.Search(
                        searchMetadata, 
                        _searcherType, 
                        Lyricify.Lyrics.Searchers.Helpers.CompareHelper.MatchType.NoMatch);
                }
            }
            
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"LyricifyLyricProvider [{SourceApp}] 搜索候选歌曲异常: {ex.Message}");
            return null;
        }


        if (searchResult == null)
        {
            Log.Info($"LyricifyLyricProvider [{SourceApp}] 未找到任何候选歌曲");
            return null;
        }

        // 2. 对最佳候选歌曲进行 Jaro-Winkler 打分与时长比对
        int matchScore = ScoreCandidate(track, searchResult);
        Log.Debug($"LyricifyLyricProvider [{SourceApp}] 候选匹配详情: {searchResult.Title} - {searchResult.Artist} (时长: {(searchResult.DurationMs ?? 0) / 1000}s), 计算得分: {matchScore}");

        // 3. 统一低分过滤阈值，防止较弱的平台候选进入最终竞争。
        if (matchScore < MinimumMatchScore)
        {
            Log.Info($"LyricifyLyricProvider [{SourceApp}] 匹配分值 {matchScore} 低于准入线 {MinimumMatchScore} 分，放弃采用");
            return null;
        }

        // 4. 获取详细的歌词内容并解密
        string? rawLyric = null;
        string? rawTranslation = null;

        try
        {
            if (_searcherType == Lyricify.Lyrics.Searchers.Searchers.QQMusic && searchResult is QQMusicSearchResult qqResult)
            {
                foreach (var candidate in qqCandidates ?? new List<QQMusicSearchResult> { qqResult })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidateScore = ScoreCandidate(track, candidate);
                    if (candidateScore < MinimumMatchScore)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(candidate.Mid))
                    {
                        var lrcResponse = await ProviderHelper.QQMusicApi.GetLyric(candidate.Mid);
                        rawLyric = lrcResponse?.Lyric;
                        rawTranslation = lrcResponse?.Trans;
                    }

                    if (string.IsNullOrWhiteSpace(rawLyric))
                    {
                        var response = await ProviderHelper.QQMusicApi.GetLyricsAsync(candidate.Id);
                        rawLyric = response?.Lyrics;
                        rawTranslation = response?.Trans;
                    }

                    if (!string.IsNullOrWhiteSpace(rawLyric))
                    {
                        matchScore = candidateScore;
                        break;
                    }
                }
            }
            else if (_searcherType == Lyricify.Lyrics.Searchers.Searchers.Netease && searchResult is NeteaseSearchResult neteaseResult)
            {
                var response = await ProviderHelper.NeteaseApi.GetLyric(neteaseResult.Id);
                rawLyric = response?.Lrc?.Lyric;
                rawTranslation = response?.Tlyric?.Lyric;
            }
            else if (_searcherType == Lyricify.Lyrics.Searchers.Searchers.Kugou && searchResult is KugouSearchResult kugouResult)
            {
                var response = await ProviderHelper.KugouApi.GetSearchLyrics(hash: kugouResult.Hash);
                var candidates = response?.Candidates;
                if (candidates is not { Count: > 0 })
                {
                    // Kugou occasionally returns an empty candidate list for a valid hash.
                    await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
                    response = await ProviderHelper.KugouApi.GetSearchLyrics(hash: kugouResult.Hash);
                    candidates = response?.Candidates;
                }

                foreach (var candidate in candidates ?? Enumerable.Empty<Lyricify.Lyrics.Providers.Web.Kugou.SearchLyricsResponse.Candidate>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var krcLyrics = await Lyricify.Lyrics.Decrypter.Krc.Helper.GetLyricsAsync(candidate.Id, candidate.AccessKey);
                        if (string.IsNullOrWhiteSpace(krcLyrics))
                        {
                            continue;
                        }

                        var parsedList = Lyricify.Lyrics.Parsers.KrcParser.ParseLyrics(krcLyrics);
                        if (parsedList is not { Count: > 0 })
                        {
                            continue;
                        }

                        var lyricBuilder = new StringBuilder();
                        var translationBuilder = new StringBuilder();
                        foreach (var item in parsedList)
                        {
                            if (item.StartTime is not int startTime)
                            {
                                continue;
                            }

                            var startTimeStr = TimeSpan.FromMilliseconds(startTime).ToString(@"mm\:ss\.ff");
                            lyricBuilder.Append('[').Append(startTimeStr).Append(']').AppendLine(item.Text);
                            if (item is IFullLineInfo fullLineInfo &&
                                fullLineInfo.Translations.TryGetValue("zh", out var translation) &&
                                !string.IsNullOrWhiteSpace(translation))
                            {
                                translationBuilder.Append('[').Append(startTimeStr).Append(']').AppendLine(translation);
                            }
                        }

                        rawLyric = lyricBuilder.ToString();
                        rawTranslation = translationBuilder.ToString();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"LyricifyLyricProvider [{SourceApp}] 酷狗歌词候选 {candidate.Id} 拉取失败，继续尝试下一项: {ex.Message}");
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"LyricifyLyricProvider [{SourceApp}] 拉取歌词具体内容失败: {ex.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawLyric))
        {
            Log.Info($"LyricifyLyricProvider [{SourceApp}] 获取到的原始歌词文本为空");
            return null;
        }

        // 5. 解析 LRC 歌词行
        var lines = ParseLrc(rawLyric);
        if (!string.IsNullOrWhiteSpace(rawTranslation))
        {
            // 对齐翻译歌词行并合并
            var translationLines = ParseLrc(rawTranslation);
            const double epsilonMs = 60.0;
            foreach (var transLine in translationLines)
            {
                var matchedLine = lines.FirstOrDefault(l => Math.Abs((l.Timestamp - transLine.Timestamp).TotalMilliseconds) <= epsilonMs);
                if (matchedLine != null)
                {
                    int idx = lines.IndexOf(matchedLine);
                    lines[idx] = lines[idx] with { Translation = transLine.Text };
                }
            }
        }

        if (lines.Count == 0) return null;

        return new LyricDocument(lines, matchScore);
    }

    private int ScoreCandidate(TrackInfo track, ISearchResult candidate)
    {
        var trackForMatching = IsUnreliableQqDuration(track)
            ? track with { Duration = TimeSpan.Zero }
            : track;
        return LyricMatcher.Score(trackForMatching, candidate.Title, candidate.Artist, (candidate.DurationMs ?? 0) / 1000);
    }

    private bool IsUnreliableQqDuration(TrackInfo track)
    {
        return _searcherType == Lyricify.Lyrics.Searchers.Searchers.QQMusic &&
               track.SourceApp.Equals("QQMusic", StringComparison.OrdinalIgnoreCase) &&
               track.Duration > TimeSpan.Zero &&
               track.Duration <= TimeSpan.FromSeconds(61);
    }
}
