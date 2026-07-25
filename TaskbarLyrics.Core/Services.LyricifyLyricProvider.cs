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
                    searchResult = new NeteaseSearchResult(track.Title, new[] { track.Artist }, track.Album, Array.Empty<string>(), (int)track.Duration.TotalMilliseconds, track.SongId);
                }
                else if (_searcherType == Lyricify.Lyrics.Searchers.Searchers.QQMusic && track.SourceApp.Equals("QQMusic", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info($"LyricifyLyricProvider [{SourceApp}] 命中 SMTC QQ音乐媒体 ID: {track.SongId}，直接跳转获取歌词");
                    searchResult = new QQMusicSearchResult(track.Title, new[] { track.Artist }, track.Album, Array.Empty<string>(), (int)track.Duration.TotalMilliseconds, track.SongId, string.Empty);
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
                    var searchMetadata = BuildTrackMetadata(track);
                    searchResult = await SearchHelper.Search(
                        searchMetadata,
                        _searcherType,
                        Lyricify.Lyrics.Searchers.Helpers.CompareHelper.MatchType.NoMatch);
                }
                else
                {
                    var searchMetadata = BuildTrackMetadata(track);

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
        if (matchScore < LyricMatchingPolicy.MinimumAcceptedMatchScore)
        {
            Log.Info($"LyricifyLyricProvider [{SourceApp}] 匹配分值 {matchScore} 低于准入线 {LyricMatchingPolicy.MinimumAcceptedMatchScore} 分，放弃采用");
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
                    ISearchResult candidateSearchResult = candidate;
                    var candidateScore = ScoreCandidate(track, candidate);
                    Log.Debug($"LyricifyLyricProvider [QQMusic] QQ 候选诊断: Title='{candidateSearchResult.Title}', Artist='{candidateSearchResult.Artist}', Album='{candidateSearchResult.Album}', DurationMs={candidateSearchResult.DurationMs ?? 0}, Id='{candidate.Id}', Mid='{candidate.Mid}', Score={candidateScore}");
                    if (candidateScore < LyricMatchingPolicy.MinimumAcceptedMatchScore)
                    {
                        Log.Debug($"LyricifyLyricProvider [QQMusic] QQ 候选分数低于准入线，跳过: Score={candidateScore}");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(candidate.Id))
                    {
                        var response = await ProviderHelper.QQMusicApi.GetLyricsAsync(candidate.Id);
                        rawLyric = response?.Lyrics;
                        rawTranslation = response?.Trans;
                        Log.Debug($"LyricifyLyricProvider [QQMusic] GetLyricsAsync(id) 返回: Id='{candidate.Id}', LyricLen={GetTextLength(rawLyric)}, TransLen={GetTextLength(rawTranslation)}");
                    }

                    if (string.IsNullOrWhiteSpace(rawLyric) && !string.IsNullOrWhiteSpace(candidate.Mid))
                    {
                        var lrcResponse = await ProviderHelper.QQMusicApi.GetLyric(candidate.Mid);
                        rawLyric = lrcResponse?.Lyric;
                        rawTranslation = lrcResponse?.Trans;
                        Log.Debug($"LyricifyLyricProvider [QQMusic] GetLyric(mid) 返回: Mid='{candidate.Mid}', LyricLen={GetTextLength(rawLyric)}, TransLen={GetTextLength(rawTranslation)}");
                    }

                    if (!string.IsNullOrWhiteSpace(rawLyric))
                    {
                        Log.Debug($"LyricifyLyricProvider [QQMusic] QQ 候选返回有效歌词，采用: Id='{candidate.Id}', Mid='{candidate.Mid}', Score={candidateScore}");
                        matchScore = candidateScore;
                        break;
                    }

                    Log.Debug($"LyricifyLyricProvider [QQMusic] QQ 候选未返回有效歌词: Id='{candidate.Id}', Mid='{candidate.Mid}'");
                }
            }
            else if (_searcherType == Lyricify.Lyrics.Searchers.Searchers.Netease && searchResult is NeteaseSearchResult neteaseResult)
            {
                var response = await ProviderHelper.NeteaseApi.GetLyric(neteaseResult.Id);
                rawLyric = response?.Lrc?.Lyric;
                rawTranslation = response?.Tlyric?.Lyric;
                Log.Debug($"LyricifyLyricProvider [Netease] GetLyric 返回: Id='{neteaseResult.Id}', LyricLen={GetTextLength(rawLyric)}, TransLen={GetTextLength(rawTranslation)}");
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
                        Log.Debug($"LyricifyLyricProvider [Kugou] KRC 返回有效歌词: Hash='{kugouResult.Hash}', CandidateId='{candidate.Id}', LyricLen={GetTextLength(rawLyric)}, TransLen={GetTextLength(rawTranslation)}");
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

        Log.Debug($"LyricifyLyricProvider [{SourceApp}] 准备解析原始歌词: LyricLen={GetTextLength(rawLyric)}, TransLen={GetTextLength(rawTranslation)}");

        // 5. 解析 LRC 歌词行
        var lines = ParseRawLyrics(rawLyric);
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

        if (lines.Count == 0)
        {
            Log.Info($"LyricifyLyricProvider [{SourceApp}] 原始歌词非空，但解析后没有有效时间轴行");
            return null;
        }

        return new LyricDocument(lines, matchScore);
    }

    private List<LyricLine> ParseRawLyrics(string rawLyric)
    {
        var lines = ParseLrc(rawLyric);
        if (lines.Count > 0 ||
            _searcherType != Lyricify.Lyrics.Searchers.Searchers.QQMusic)
        {
            return lines;
        }

        try
        {
            lines = ParseQrc(rawLyric);
            Log.Debug($"LyricifyLyricProvider [QQMusic] QRC 解析结果: Lines={lines.Count}");
            return lines;
        }
        catch (Exception ex)
        {
            Log.Warn($"LyricifyLyricProvider [QQMusic] QRC 解析失败: {ex.Message}");
            return new List<LyricLine>();
        }
    }

    private static List<LyricLine> ParseQrc(string rawLyric)
    {
        var parsed = Lyricify.Lyrics.Parsers.QrcParser.Parse(rawLyric);
        var parsedLines = parsed?.Lines?
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToList();
        if (parsedLines is not { Count: > 0 })
        {
            return new List<LyricLine>();
        }

        var lines = new List<LyricLine>();
        foreach (var parsedLine in parsedLines)
        {
            var startMs = Math.Max(0, parsedLine.StartTime ?? 0);
            var text = NormalizeQrcText(parsedLine.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            List<LyricSyllable>? syllables = null;
            if (parsedLine is SyllableLineInfo syllableLine &&
                syllableLine.Syllables is { Count: > 0 })
            {
                syllables = syllableLine.Syllables
                    .Where(syllable => !string.IsNullOrEmpty(syllable.Text))
                    .Select(syllable =>
                    {
                        var syllableStartMs = Math.Max(0, syllable.StartTime - startMs);
                        var syllableDurationMs = Math.Max(1, syllable.EndTime - syllable.StartTime);
                        return new LyricSyllable(
                            TimeSpan.FromMilliseconds(syllableStartMs),
                            TimeSpan.FromMilliseconds(syllableDurationMs),
                            syllable.Text);
                    })
                    .ToList();
            }

            lines.Add(new LyricLine(TimeSpan.FromMilliseconds(startMs), text, Syllables: syllables));
        }

        return lines
            .OrderBy(line => line.Timestamp)
            .ToList();
    }

    private static string NormalizeQrcText(string text)
    {
        return text
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static TrackMultiArtistMetadata BuildTrackMetadata(TrackInfo track)
    {
        return new TrackMultiArtistMetadata
        {
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            DurationMs = (int)track.Duration.TotalMilliseconds
        };
    }

    private static int GetTextLength(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? 0 : value.Length;
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
               track.Duration <= LyricMatchingPolicy.QqMusicUnreliableDurationThreshold;
    }
}
