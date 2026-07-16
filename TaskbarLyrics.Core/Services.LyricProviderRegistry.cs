using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Database;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

#pragma warning disable CS0162

namespace TaskbarLyrics.Core.Services;

public sealed class LyricProviderRegistry : ILyricProviderRegistry
{
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
            return BuildResults(_providers.ToDictionary(
                provider => provider,
                _ => new LyricFetchResult(
                    mapping.PureMusicDocument,
                    LyricAcquisitionKind.SongMapping,
                    stopwatch.ElapsedMilliseconds)));
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
                LyricMatchingPolicy.OfficialSourceTimeout,
                cancellationToken);
            return preferredResult.Result.Document is null
                ? BuildResults()
                : BuildResults(new Dictionary<ILyricProvider, LyricFetchResult>
                {
                    [preferred] = preferredResult.Result
                });
        }

        var localProvider = FindProviders("Local").FirstOrDefault();
        if (localProvider is not null)
        {
            Log.Info($"Local lyric source enabled, trying before player official source. Timeout={LyricMatchingPolicy.LocalProviderTimeout.TotalSeconds:F0}s");
            var localResult = await RunProviderAsync(
                localProvider,
                overriddenTrack,
                LyricMatchingPolicy.LocalProviderTimeout,
                cancellationToken);
            if (localResult.Result.Document is not null)
            {
                stopwatch.Stop();
                Log.Info($"Local lyric source returned a valid document. Total elapsed: {stopwatch.ElapsedMilliseconds} ms");
                return BuildResults(new Dictionary<ILyricProvider, LyricFetchResult>
                {
                    [localProvider] = ApplyQualityWeight(localProvider, localResult.Result)
                });
            }
        }

        if (LyricSourceRoutingPolicy.TryGetOfficialProvider(track.SourceApp, out var officialSource))
        {
            var officialProvider = FindProviders(officialSource).FirstOrDefault();
            if (officialProvider is not null)
            {
                Log.Info($"播放器 [{track.SourceApp}] 已适配，歌词源 [{officialSource}] 进入最长 {LyricMatchingPolicy.OfficialSourceTimeout.TotalSeconds:F0} 秒独占检索阶段。");
                var officialResult = await RunProviderAsync(
                    officialProvider,
                    overriddenTrack,
                    LyricMatchingPolicy.OfficialSourceTimeout,
                    cancellationToken);
                if (officialResult.Result.Document is not null)
                {
                    var weightedOfficial = ApplyQualityWeight(officialProvider, officialResult.Result);
                    if (weightedOfficial.Document!.BestScore >= LyricMatchingPolicy.OfficialImmediateAcceptScore)
                    {
                        stopwatch.Stop();
                        Log.Info($"Official lyric source [{officialSource}] returned high confidence score {weightedOfficial.Document.BestScore}, accepting exclusively. Elapsed: {stopwatch.ElapsedMilliseconds} ms");
                        return BuildResults(new Dictionary<ILyricProvider, LyricFetchResult>
                        {
                            [officialProvider] = weightedOfficial
                        });
                    }

                    Log.Info($"Official lyric source [{officialSource}] returned medium confidence score {weightedOfficial.Document.BestScore}, running fallback competition.");
                    var competitionResults = await ResolveFallbackAsync(overriddenTrack, cancellationToken);
                    var bestFallback = competitionResults
                        .Where(pair => pair.Value.Document is not null)
                        .OrderByDescending(pair => pair.Value.Document!.BestScore)
                        .FirstOrDefault();

                    var selectedProvider = officialProvider;
                    var selectedResult = weightedOfficial;
                    if (bestFallback.Key is not null &&
                        bestFallback.Value.Document!.BestScore >= weightedOfficial.Document.BestScore + LyricMatchingPolicy.FallbackOverrideMargin)
                    {
                        selectedProvider = bestFallback.Key;
                        selectedResult = bestFallback.Value;
                        Log.Info($"Fallback lyric source [{selectedProvider.SourceApp}] overrides official [{officialSource}]: {selectedResult.Document.BestScore} vs {weightedOfficial.Document.BestScore}");
                    }

                    stopwatch.Stop();
                    Log.Info($"Official/fallback competition completed. Selected source: [{selectedProvider.SourceApp}], score: {selectedResult.Document!.BestScore}, elapsed: {stopwatch.ElapsedMilliseconds} ms");
                    return BuildResults(new Dictionary<ILyricProvider, LyricFetchResult>
                    {
                        [selectedProvider] = selectedResult
                    });

                    stopwatch.Stop();
                    Log.Info($"官方歌词源 [{officialSource}] 返回有效歌词，独占采用。总耗时: {stopwatch.ElapsedMilliseconds} ms");
                    return BuildResults(new Dictionary<ILyricProvider, LyricFetchResult>
                    {
                        [officialProvider] = officialResult.Result
                    });
                }

                Log.Info($"官方歌词源 [{officialSource}] 未返回有效歌词，立即启用跨平台回退。");
            }
        }

        var fallbackResults = await ResolveFallbackAsync(overriddenTrack, cancellationToken);
        stopwatch.Stop();
        var best = fallbackResults
            .Where(pair => pair.Value.Document is not null)
            .OrderByDescending(pair => pair.Value.Document!.BestScore)
            .FirstOrDefault();
        Log.Info($"ResolveLyricsAsync 回退检索结束，总耗时: {stopwatch.ElapsedMilliseconds} ms，最佳歌词源: [{best.Key?.SourceApp ?? "None"}]，最终分: {best.Value?.Document?.BestScore ?? 0}");
        if (best.Key is null)
        {
            LogNoLyricsSummary(overriddenTrack, track.SourceApp, fallbackResults, stopwatch.Elapsed);
        }

        return BuildResults(fallbackResults);
    }

    private void LogNoLyricsSummary(
        TrackInfo track,
        string sourceApp,
        IReadOnlyDictionary<ILyricProvider, LyricFetchResult> fallbackResults,
        TimeSpan elapsed)
    {
        var official = LyricSourceRoutingPolicy.TryGetOfficialProvider(sourceApp, out var officialSource)
            ? officialSource
            : "None";
        var fallbackSummary = fallbackResults.Count == 0
            ? "None"
            : string.Join(", ", fallbackResults
                .OrderBy(pair => pair.Key.SourceApp, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key.SourceApp}:{FormatDocumentSummary(pair.Value.Document)}"));

        Log.Warn(
            "No lyrics resolved. " +
            $"Track='{track.Title}' Artist='{track.Artist}' SourceApp='{sourceApp}' " +
            $"Official='{official}' FallbackResults='{fallbackSummary}' ElapsedMs={elapsed.TotalMilliseconds:F0}");
    }

    private static string FormatDocumentSummary(LyricDocument? document)
    {
        if (document is null)
        {
            return "null";
        }

        return $"score={document.BestScore},lines={document.Lines.Count}";
    }

    private async Task<Dictionary<ILyricProvider, LyricFetchResult>> ResolveFallbackAsync(
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        var resolvedResults = new Dictionary<ILyricProvider, LyricFetchResult>();
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
            foreach (var (provider, result) in batchResults.Results)
            {
                resolvedResults[provider] = result;
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
            .Select(provider => RunProviderAsync(provider, track, LyricMatchingPolicy.FallbackProviderTimeout, batchCts.Token))
            .ToList();
        var results = new Dictionary<ILyricProvider, LyricFetchResult>();
        var bestScore = 0;
        DateTimeOffset? weakWaitDeadline = null;

        while (pendingTasks.Count > 0)
        {
            Task completedTask;
            if (weakWaitDeadline is not null)
            {
                var remainingWait = weakWaitDeadline.Value - DateTimeOffset.UtcNow;
                if (remainingWait <= TimeSpan.Zero)
                {
                    Log.Info($"回退批次弱等待窗口结束，采用当前最佳候选 {bestScore} 分。");
                    batchCts.Cancel();
                    break;
                }

                var softTimeoutTask = Task.Delay(remainingWait, batchCts.Token);
                completedTask = await Task.WhenAny(pendingTasks.Cast<Task>().Append(softTimeoutTask));
                if (completedTask == softTimeoutTask)
                {
                    Log.Info($"回退批次已有 {bestScore} 分候选，{LyricMatchingPolicy.FallbackSoftWait.TotalMilliseconds:F0} ms 等待窗口结束。");
                    batchCts.Cancel();
                    break;
                }
            }
            else
            {
                completedTask = await Task.WhenAny(pendingTasks);
            }

            var providerTask = (Task<(ILyricProvider Provider, LyricFetchResult Result)>)completedTask;
            pendingTasks.Remove(providerTask);
            var (provider, result) = await providerTask;
            results[provider] = result;
            if (result.Document is null)
            {
                continue;
            }

            var weightedResult = ApplyQualityWeight(provider, result);
            results[provider] = weightedResult;
            bestScore = Math.Max(bestScore, weightedResult.Document!.BestScore);
            if (bestScore >= LyricMatchingPolicy.FallbackImmediateExitScore &&
                weakWaitDeadline is null &&
                pendingTasks.Count > 0)
            {
                weakWaitDeadline = DateTimeOffset.UtcNow + LyricMatchingPolicy.FallbackSoftWait;
                Log.Info($"回退歌词源 [{provider.SourceApp}] 达到 {bestScore} 分，进入 {LyricMatchingPolicy.FallbackSoftWait.TotalMilliseconds:F0} ms 弱等待窗口。");
            }
        }

        return new BatchResolveResult(results, results.Values.Any(result => result.Document is not null));
    }

    private static LyricFetchResult ApplyQualityWeight(ILyricProvider provider, LyricFetchResult result)
    {
        var document = result.Document!;
        var qualityWeight = LyricMatchingPolicy.SourceQualityWeights.TryGetValue(provider.SourceApp, out var configuredWeight)
            ? configuredWeight
            : 0;
        var weightedScore = document.BestScore + qualityWeight;
        Log.Info($"回退歌词源 [{provider.SourceApp}] 基础分: {document.BestScore}，质量权重: +{qualityWeight}，最终分: {weightedScore}");
        return result with
        {
            Document = new LyricDocument(document.Lines, weightedScore, document.IsPureMusic)
        };
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
                    100,
                    isPureMusic: true);
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
        IReadOnlyDictionary<ILyricProvider, LyricFetchResult>? results = null)
    {
        return _providers
            .Select(provider =>
            {
                if (results is not null && results.TryGetValue(provider, out var result))
                {
                    return new LyricResolveResult(
                        provider.SourceApp,
                        result.Document,
                        result.Acquisition,
                        result.ElapsedMilliseconds);
                }

                return new LyricResolveResult(provider.SourceApp, null);
            })
            .ToList();
    }

    private static bool IsUnknownTitle(string? title)
    {
        return string.IsNullOrWhiteSpace(title) ||
               string.Equals(title, "Unknown Title", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(ILyricProvider Provider, LyricFetchResult Result)> RunProviderAsync(
        ILyricProvider provider,
        TrackInfo track,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!_providerGates.TryGetValue(provider, out var gate))
        {
            return (provider, NotFound());
        }

        try
        {
            if (!await gate.WaitAsync(0, cancellationToken))
            {
                Log.Warn($"音源 [{provider.SourceApp}] 上一次请求仍未结束，跳过本次检索以避免任务堆积。");
                return (provider, NotFound());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (provider, NotFound());
        }

        Task<LyricFetchResult>? providerTask = null;
        try
        {
            using var providerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            providerTask = provider.GetLyricsWithDiagnosticsAsync(track, providerCts.Token);
            var timeoutTask = Task.Delay(timeout);
            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var completedTask = await Task.WhenAny(providerTask, timeoutTask, cancellationTask);
            if (completedTask == timeoutTask)
            {
                providerCts.Cancel();
                Log.Warn($"音源 [{provider.SourceApp}] 超过 {timeout.TotalSeconds:F0} 秒未返回，已跳过。");
                return (provider, NotFound());
            }

            if (completedTask == cancellationTask)
            {
                providerCts.Cancel();
                return (provider, NotFound());
            }

            return (provider, await providerTask);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (provider, NotFound());
        }
        catch (Exception ex)
        {
            Log.Warn($"音源 [{provider.SourceApp}] 执行异常: {ex.Message}");
            return (provider, NotFound());
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

        static LyricFetchResult NotFound()
        {
            return new LyricFetchResult(null, LyricAcquisitionKind.NotFound, 0);
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
        IReadOnlyDictionary<ILyricProvider, LyricFetchResult> Results,
        bool HasUsableDocument);
}
