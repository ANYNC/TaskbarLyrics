using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Database;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricProviderRegistry : ILyricProviderRegistry
{
    private const int SoftTimeoutScore = 85;
    private const int ImmediateExitScore = 95;
    private static readonly TimeSpan SoftTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan OfficialSourceTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FallbackProviderTimeout = TimeSpan.FromSeconds(5);
    private static readonly IReadOnlyDictionary<string, int> SourceQualityWeights =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["QQMusic"] = 6,
            ["Netease"] = 3,
            ["Kugou"] = 2,
            ["LRCLIB"] = 1,
            ["LrcLib"] = 1
        };

    private readonly IReadOnlyList<ILyricProvider> _providers;
    private readonly IReadOnlyDictionary<ILyricProvider, SemaphoreSlim> _providerGates;

    public LyricProviderRegistry(IEnumerable<ILyricProvider> providers)
    {
        _providers = providers.ToList();
        _providerGates = _providers.ToDictionary(provider => provider, _ => new SemaphoreSlim(1, 1));
    }

    public async Task<List<LyricResolveResult>> ResolveLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Log.Info($"ResolveLyricsAsync 开始处理轨道: {track.Title} - {track.Artist} (App: {track.SourceApp}, 时长: {track.Duration.TotalSeconds}s)");

        if (IsUnknownTitle(track.Title))
        {
            Log.Info("轨道标题未知，跳过在线歌词检索。");
            return BuildResults();
        }

        var mapping = ResolveMapping(track);
        if (mapping.PureMusicDocument is not null)
        {
            return BuildResults(_providers.ToDictionary<ILyricProvider, ILyricProvider, LyricDocument?>(
                provider => provider,
                _ => mapping.PureMusicDocument));
        }

        var overriddenTrack = track with
        {
            Title = mapping.Title,
            Artist = mapping.Artist
        };

        if (!string.IsNullOrWhiteSpace(mapping.PreferredProvider))
        {
            Log.Info($"人工映射强制绑定歌词源: {mapping.PreferredProvider}");
            var preferred = FindProviders(mapping.PreferredProvider).FirstOrDefault();
            if (preferred is null)
            {
                Log.Warn($"人工映射指定的歌词源 [{mapping.PreferredProvider}] 未注册。");
                return BuildResults();
            }

            var preferredResult = await RunProviderAsync(
                preferred,
                overriddenTrack,
                OfficialSourceTimeout,
                cancellationToken);
            return preferredResult.Document is null
                ? BuildResults()
                : BuildResults(new Dictionary<ILyricProvider, LyricDocument?>
                {
                    [preferred] = preferredResult.Document
                });
        }

        if (LyricSourceRoutingPolicy.TryGetOfficialProvider(track.SourceApp, out var officialSource))
        {
            var officialProvider = FindProviders(officialSource).FirstOrDefault();
            if (officialProvider is not null)
            {
                Log.Info($"播放器 [{track.SourceApp}] 已适配，歌词源 [{officialSource}] 进入最长 {OfficialSourceTimeout.TotalSeconds:F0} 秒独占检索阶段。");
                var officialResult = await RunProviderAsync(
                    officialProvider,
                    overriddenTrack,
                    OfficialSourceTimeout,
                    cancellationToken);
                if (officialResult.Document is not null)
                {
                    stopwatch.Stop();
                    Log.Info($"官方歌词源 [{officialSource}] 返回有效歌词，独占采用。总耗时: {stopwatch.ElapsedMilliseconds} ms");
                    return BuildResults(new Dictionary<ILyricProvider, LyricDocument?>
                    {
                        [officialProvider] = officialResult.Document
                    });
                }

                Log.Info($"官方歌词源 [{officialSource}] 未返回有效歌词，立即启用跨平台回退。");
            }
        }

        var fallbackResults = await ResolveFallbackAsync(overriddenTrack, cancellationToken);
        stopwatch.Stop();
        var best = fallbackResults
            .Where(pair => pair.Value is not null)
            .OrderByDescending(pair => pair.Value!.BestScore)
            .FirstOrDefault();
        Log.Info($"ResolveLyricsAsync 回退检索结束，总耗时: {stopwatch.ElapsedMilliseconds} ms，最佳歌词源: [{best.Key?.SourceApp ?? "None"}]，最终分: {best.Value?.BestScore ?? 0}");
        return BuildResults(fallbackResults);
    }

    private async Task<Dictionary<ILyricProvider, LyricDocument?>> ResolveFallbackAsync(
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        var resolvedResults = new Dictionary<ILyricProvider, LyricDocument?>();
        foreach (var batchSources in LyricSourceRoutingPolicy.BuildFallbackBatches(track))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchProviders = batchSources
                .SelectMany(FindProviders)
                .Distinct()
                .ToList();
            if (batchProviders.Count == 0)
            {
                continue;
            }

            Log.Info($"启动跨平台回退批次: {string.Join(", ", batchProviders.Select(provider => provider.SourceApp))}");
            var batchResults = await ResolveFallbackBatchAsync(batchProviders, track, cancellationToken);
            foreach (var (provider, document) in batchResults.Documents)
            {
                resolvedResults[provider] = document;
            }

            if (batchResults.HasUsableDocument)
            {
                break;
            }
        }

        return resolvedResults;
    }

    private async Task<BatchResolveResult> ResolveFallbackBatchAsync(
        IReadOnlyList<ILyricProvider> providers,
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pendingTasks = providers
            .Select(provider => RunProviderAsync(provider, track, FallbackProviderTimeout, batchCts.Token))
            .ToList();
        var documents = new Dictionary<ILyricProvider, LyricDocument?>();
        var bestScore = 0;

        while (pendingTasks.Count > 0)
        {
            Task completedTask;
            if (bestScore >= SoftTimeoutScore)
            {
                var softTimeoutTask = Task.Delay(SoftTimeout, batchCts.Token);
                completedTask = await Task.WhenAny(pendingTasks.Cast<Task>().Append(softTimeoutTask));
                if (completedTask == softTimeoutTask)
                {
                    Log.Info($"回退批次已有 {bestScore} 分候选，{SoftTimeout.TotalMilliseconds:F0} ms 等待窗口结束。");
                    batchCts.Cancel();
                    break;
                }
            }
            else
            {
                completedTask = await Task.WhenAny(pendingTasks);
            }

            var providerTask = (Task<(ILyricProvider Provider, LyricDocument? Document)>)completedTask;
            pendingTasks.Remove(providerTask);
            var (provider, document) = await providerTask;
            if (document is null)
            {
                continue;
            }

            var weightedDocument = ApplyQualityWeight(provider, document);
            documents[provider] = weightedDocument;
            bestScore = Math.Max(bestScore, weightedDocument.BestScore);
            if (bestScore >= ImmediateExitScore)
            {
                Log.Info($"回退歌词源 [{provider.SourceApp}] 达到 {bestScore} 分，触发高置信快速返回。");
                batchCts.Cancel();
                break;
            }
        }

        return new BatchResolveResult(documents, documents.Values.Any(document => document is not null));
    }

    private static LyricDocument ApplyQualityWeight(ILyricProvider provider, LyricDocument document)
    {
        var qualityWeight = SourceQualityWeights.TryGetValue(provider.SourceApp, out var configuredWeight)
            ? configuredWeight
            : 0;
        var weightedScore = Math.Min(100, document.BestScore + qualityWeight);
        Log.Info($"回退歌词源 [{provider.SourceApp}] 基础分: {document.BestScore}，质量权重: +{qualityWeight}，最终分: {weightedScore}");
        return new LyricDocument(document.Lines, weightedScore);
    }

    private MappingResult ResolveMapping(TrackInfo track)
    {
        var targetTitle = track.Title;
        var targetArtist = track.Artist;
        try
        {
            using var db = new SongSearchMapDbContext();
            var map = db.SongSearchMaps.FirstOrDefault(candidate =>
                candidate.OriginalTitle == track.Title &&
                candidate.OriginalArtist == track.Artist);
            if (map is null)
            {
                return new MappingResult(targetTitle, targetArtist, null, null);
            }

            Log.Info($"SQLite 别名映射命中: {track.Title} - {track.Artist}");
            if (map.IsMarkedAsPureMusic)
            {
                var pureMusic = new LyricDocument(
                    new[] { new LyricLine(TimeSpan.Zero, "🎶🎶🎶") },
                    100);
                return new MappingResult(targetTitle, targetArtist, null, pureMusic);
            }

            if (!string.IsNullOrWhiteSpace(map.MappedTitle))
            {
                targetTitle = map.MappedTitle;
            }

            if (!string.IsNullOrWhiteSpace(map.MappedArtist))
            {
                targetArtist = map.MappedArtist;
            }

            return new MappingResult(targetTitle, targetArtist, map.PreferredProvider, null);
        }
        catch (Exception ex)
        {
            Log.Error($"查询 SQLite 映射库失败: {ex.Message}");
            return new MappingResult(targetTitle, targetArtist, null, null);
        }
    }

    private IEnumerable<ILyricProvider> FindProviders(string sourceApp)
    {
        return _providers.Where(provider =>
            string.Equals(provider.SourceApp, sourceApp, StringComparison.OrdinalIgnoreCase));
    }

    private List<LyricResolveResult> BuildResults(
        IReadOnlyDictionary<ILyricProvider, LyricDocument?>? documents = null)
    {
        return _providers
            .Select(provider => new LyricResolveResult(
                provider.SourceApp,
                documents is not null && documents.TryGetValue(provider, out var document)
                    ? document
                    : null))
            .ToList();
    }

    private static bool IsUnknownTitle(string? title)
    {
        return string.IsNullOrWhiteSpace(title) ||
               string.Equals(title, "Unknown Title", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(ILyricProvider Provider, LyricDocument? Document)> RunProviderAsync(
        ILyricProvider provider,
        TrackInfo track,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!_providerGates.TryGetValue(provider, out var gate))
        {
            return (provider, null);
        }

        try
        {
            if (!await gate.WaitAsync(0, cancellationToken))
            {
                Log.Warn($"音源 [{provider.SourceApp}] 上一次请求仍未结束，跳过本次检索以避免任务堆积。");
                return (provider, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (provider, null);
        }

        Task<LyricDocument?>? providerTask = null;
        try
        {
            using var providerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            providerTask = provider.GetLyricsAsync(track, providerCts.Token);
            var timeoutTask = Task.Delay(timeout);
            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var completedTask = await Task.WhenAny(providerTask, timeoutTask, cancellationTask);
            if (completedTask == timeoutTask)
            {
                providerCts.Cancel();
                Log.Warn($"音源 [{provider.SourceApp}] 超过 {timeout.TotalSeconds:F0} 秒未返回，已跳过。");
                return (provider, null);
            }

            if (completedTask == cancellationTask)
            {
                providerCts.Cancel();
                return (provider, null);
            }

            return (provider, await providerTask);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (provider, null);
        }
        catch (Exception ex)
        {
            Log.Warn($"音源 [{provider.SourceApp}] 执行异常: {ex.Message}");
            return (provider, null);
        }
        finally
        {
            if (providerTask is null || providerTask.IsCompleted)
            {
                gate.Release();
            }
            else
            {
                _ = providerTask.ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        gate.Release();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    public async Task<LyricDocument?> GetLyricsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var results = await ResolveLyricsAsync(track, cancellationToken);
        return results
            .Where(result => result.Document is not null)
            .OrderByDescending(result => result.Document!.BestScore)
            .FirstOrDefault()?.Document;
    }

    private sealed record MappingResult(
        string Title,
        string Artist,
        string? PreferredProvider,
        LyricDocument? PureMusicDocument);

    private sealed record BatchResolveResult(
        IReadOnlyDictionary<ILyricProvider, LyricDocument?> Documents,
        bool HasUsableDocument);
}
