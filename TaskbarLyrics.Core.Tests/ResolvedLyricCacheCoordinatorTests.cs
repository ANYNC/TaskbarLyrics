using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class ResolvedLyricCacheCoordinatorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OldQqFallbackRefreshPreservesTheCandidateContextAndUsableContent(bool manual, bool succeeds)
    {
        using var fixture = CacheFile.Create();
        var track = CreateTrack();
        using var cache = new JsonResolvedLyricCache(fixture.Path);
        var context = manual ? LyricSourceSelection.ManualCacheContext : "configured";
        var old = new ResolvedLyrics(new ParsedLyrics([new ParsedLyricLine(TimeSpan.FromSeconds(1), null, "old fallback")],
            LyricTimingKind.LineTimed, LyricTimingProvenance.ProviderSupplied, LyricPayloadFormat.Lrc),
            KnownLyricProviders.QQMusic, "232271227", LyricAcquisitionKind.Remote, new Dictionary<string, string>());
        Assert.True(cache.Store(track, old, context));
        var pipeline = new RecordingLyricPipelineCache();
        pipeline.StoreRaw(new RawLyricPayload(old.ProviderId, old.CandidateId, LyricPayloadFormat.Lrc,
            "[00:01.00]old fallback", null, false, false, new Dictionary<string, string>()), DateTimeOffset.UtcNow);
        var source = new RecordingSource(old.ProviderId);
        source.FetchHandler = (candidate, _) =>
        {
            Assert.Equal(old.CandidateId, candidate.CandidateId);
            if (!succeeds) throw new System.Net.Http.HttpRequestException("offline");
            return Task.FromResult<RawLyricPayload?>(new RawLyricPayload(old.ProviderId, old.CandidateId, LyricPayloadFormat.Qrc,
                "[1000,1000]A(1000,500)B(1500,500)", "[00:01.00]译文", false, false,
                new Dictionary<string, string> { [QqMusicContentRevision.DiagnosticKey] = QqMusicContentRevision.Current }));
        };
        using var coordinator = new LyricResolutionCoordinator([source], [], [new LyricifyPayloadParser()], pipeline,
            new RecordingMappingResolver(), trustPolicy: new LyricProviderTrustPolicy([source.ProviderId], [source.ProviderId]),
            resolvedLyricCache: cache, sourceSelectionResolver: _ =>
                new LyricSourceSelection(manual ? [] : [source.ProviderId], "configured"));
        var result = Assert.IsType<ResolvedLyrics>(await coordinator.ResolveAsync(track));
        Assert.Equal(old.CandidateId, result.CandidateId);
        Assert.Equal(0, source.SearchCalls);
        Assert.Equal(1, source.FetchCalls);
        Assert.True(cache.TryGet(track, context, out var saved));
        if (succeeds)
        {
            Assert.Equal(LyricPayloadFormat.Qrc, result.Content.Format);
            Assert.Equal(2, Assert.Single(result.Content.Lines).Segments.Count);
            Assert.Equal("译文", result.Content.Lines[0].Translation);
            Assert.Equal(LyricPayloadFormat.Qrc, saved!.Content.Format);
            Assert.NotNull(await coordinator.ResolveAsync(track));
            Assert.Equal(1, source.FetchCalls); // Current content does not refetch on every cache hit.
        }
        else
        {
            Assert.Equal("old fallback", Assert.Single(result.Content.Lines).Text);
            Assert.Equal(LyricPayloadFormat.Lrc, saved!.Content.Format);
            Assert.True(pipeline.TryGetRaw(old.ProviderId, old.CandidateId, out var raw, out _));
            Assert.Equal(LyricPayloadFormat.Lrc, raw!.Format);
        }
        if (manual) Assert.False(cache.TryGet(track, "configured", out _));
    }

    [Fact]
    public async Task CancelingOldQqRefreshLeavesTheRememberedSelectionUntouched()
    {
        using var fixture = CacheFile.Create();
        using var cache = new JsonResolvedLyricCache(fixture.Path);
        var track = CreateTrack();
        var old = new ResolvedLyrics(new ParsedLyrics([new ParsedLyricLine(TimeSpan.Zero, null, "remembered")],
            LyricTimingKind.LineTimed, LyricTimingProvenance.ProviderSupplied, LyricPayloadFormat.Lrc),
            KnownLyricProviders.QQMusic, "remembered-id", LyricAcquisitionKind.UserBinding, new Dictionary<string, string>());
        Assert.True(cache.Store(track, old, LyricSourceSelection.ManualCacheContext));
        var source = new RecordingSource(old.ProviderId);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.FetchHandler = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        };
        using var coordinator = CreateCoordinator(source, new RecordingMappingResolver(), cache);
        using var cancellation = new CancellationTokenSource();
        var pending = coordinator.ResolveAsync(track, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(cache.TryGet(track, LyricSourceSelection.ManualCacheContext, out var retained));
        Assert.Equal(old.CandidateId, retained!.CandidateId);
        Assert.Equal("remembered", Assert.Single(retained.Content.Lines).Text);
        Assert.Equal(0, source.SearchCalls);
    }

    [Fact]
    public async Task ManualSelectionSurvivesDisabledAutomaticProviders()
    {
        using var fixture = CacheFile.Create();
        var track = CreateTrack();
        using var cache = new JsonResolvedLyricCache(fixture.Path);
        Assert.True(cache.Store(track, CreateResolved("chosen", LyricAcquisitionKind.UserBinding),
            LyricSourceSelection.ManualCacheContext));
        var source = new RecordingSource(KnownLyricProviders.QQMusic);
        using var coordinator = new LyricResolutionCoordinator(
            [source], [], [new PlainTextParser()], new EmptyPipelineCache(),
            trustPolicy: new LyricProviderTrustPolicy([source.ProviderId], [source.ProviderId]),
            resolvedLyricCache: cache,
            sourceSelectionResolver: _ => new LyricSourceSelection([], "all-disabled"));

        var resolved = await coordinator.ResolveAsync(track);

        Assert.Equal("chosen", resolved!.Content.Lines[0].Text);
        Assert.Equal(0, source.SearchCalls);
    }

    [Fact]
    public async Task CacheHitSkipsMappingLocalAndOnlineResolution()
    {
        var source = new RecordingSource(KnownLyricProviders.QQMusic);
        var mapping = new RecordingMappingResolver();
        var cache = new RecordingResolvedCache(CreateResolved("cached", LyricAcquisitionKind.MemoryCache));
        using var coordinator = CreateCoordinator(source, mapping, cache);

        var resolved = await coordinator.ResolveAsync(CreateTrack());

        Assert.Same(cache.Result, resolved);
        Assert.Equal(1, cache.TryGetCalls);
        Assert.Equal(0, cache.StoreCalls);
        Assert.Equal(0, mapping.ResolveCalls);
        Assert.Equal(0, source.SearchCalls);
        Assert.Equal(0, source.FetchCalls);
    }

    [Fact]
    public async Task AutomaticRemoteSelectionIsStoredAndASecondCoordinatorHitsIt()
    {
        using var fixture = CacheFile.Create();
        var track = CreateTrack();
        var source = CreateLyricSource(KnownLyricProviders.QQMusic);
        using (var firstCache = new JsonResolvedLyricCache(fixture.Path))
        using (var firstCoordinator = CreateCoordinator(source, new RecordingMappingResolver(), firstCache))
        {
            var resolved = await firstCoordinator.ResolveAsync(track);
            Assert.NotNull(resolved);
            Assert.Equal("remote candidate", resolved!.Content.Lines[0].Text);
            Assert.Equal(1, source.SearchCalls);
            Assert.Equal(1, source.FetchCalls);
        }

        var secondSource = new RecordingSource(KnownLyricProviders.QQMusic);
        using var secondCache = new JsonResolvedLyricCache(fixture.Path);
        using var secondCoordinator = CreateCoordinator(secondSource, new RecordingMappingResolver(), secondCache);
        var cached = await secondCoordinator.ResolveAsync(track with { SourceApp = "Another Player", Album = "Another Album" });

        Assert.NotNull(cached);
        Assert.Equal(LyricAcquisitionKind.DiskCache, cached!.Acquisition);
        Assert.Equal("remote candidate", cached.Content.Lines[0].Text);
        Assert.Equal(0, secondSource.SearchCalls);
        Assert.Equal(0, secondSource.FetchCalls);
    }

    [Fact]
    public async Task ResolveCandidateDoesNotRememberManualCandidate()
    {
        using var fixture = CacheFile.Create();
        var source = CreateLyricSource(KnownLyricProviders.QQMusic);
        using var cache = new JsonResolvedLyricCache(fixture.Path);
        using var coordinator = CreateCoordinator(source, new RecordingMappingResolver(), cache);
        var track = CreateTrack();
        var plan = LyricSearchPlanner.CreatePlan(TrackIdentity.FromTrackInfo(track));
        var candidate = CreateCandidate(source.ProviderId, plan);

        var resolved = await coordinator.ResolveCandidateAsync(track, candidate);

        Assert.NotNull(resolved);
        Assert.False(cache.TryGet(track, out _));
    }

    private static LyricResolutionCoordinator CreateCoordinator(
        ILyricSource source,
        ILyricMappingResolver mapping,
        IResolvedLyricCache? resolvedLyricCache) =>
        new(
            [source],
            [],
            [new PlainTextParser()],
            new EmptyPipelineCache(),
            mapping,
            trustPolicy: new LyricProviderTrustPolicy([source.ProviderId], [source.ProviderId]),
            sourceTimeout: TimeSpan.FromSeconds(1),
            resolvedLyricCache: resolvedLyricCache);

    private static TrackInfo CreateTrack() =>
        new(
            "track-id",
            "Cached Song",
            "Cached Artist",
            "Cached Album",
            "Player",
            TimeSpan.FromMinutes(3),
            "song-id");

    private static ResolvedLyrics CreateResolved(
        string text,
        LyricAcquisitionKind acquisition) =>
        new(
            new ParsedLyrics(
                [new ParsedLyricLine(TimeSpan.Zero, null, text)],
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                LyricPayloadFormat.PlainText),
            KnownLyricProviders.QQMusic,
            "candidate-1",
            acquisition,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["selectedBy"] = "test"
            });

    private static RecordingSource CreateLyricSource(LyricProviderId providerId)
    {
        var source = new RecordingSource(providerId);
        source.SearchHandler = (plan, _) =>
            Task.FromResult<IReadOnlyList<SourceTrackCandidate>>([CreateCandidate(providerId, plan)]);
        source.FetchHandler = (candidate, _) => Task.FromResult<RawLyricPayload?>(
            new RawLyricPayload(
                providerId,
                candidate.CandidateId,
                LyricPayloadFormat.PlainText,
                "remote candidate",
                null,
                false,
                false,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        return source;
    }

    private static SourceTrackCandidate CreateCandidate(
        LyricProviderId providerId,
        LyricSearchPlan plan) =>
        new(
            providerId,
            "remote-candidate",
            plan.OriginalTrack.Title,
            plan.OriginalTrack.Artists,
            plan.OriginalTrack.Album,
            plan.OriginalTrack.Duration,
            plan.Variants[0].Id,
            new Dictionary<string, string>(StringComparer.Ordinal));

    private sealed class RecordingResolvedCache(ResolvedLyrics? result) : IResolvedLyricCache
    {
        public ResolvedLyrics? Result { get; } = result;
        public int TryGetCalls { get; private set; }
        public int StoreCalls { get; private set; }

        public bool TryGet(TrackInfo track, out ResolvedLyrics? resolvedLyrics)
        {
            TryGetCalls++;
            resolvedLyrics = Result;
            return Result is not null;
        }

        public bool Store(TrackInfo track, ResolvedLyrics resolvedLyrics)
        {
            StoreCalls++;
            return true;
        }

        public void Clear()
        {
        }
    }

    private sealed class RecordingMappingResolver : ILyricMappingResolver
    {
        public int ResolveCalls { get; private set; }

        public LyricMapping Resolve(TrackInfo track)
        {
            ResolveCalls++;
            return LyricMapping.Unchanged(track);
        }
    }

    private sealed class RecordingSource(LyricProviderId providerId) : ILyricSource
    {
        public LyricProviderId ProviderId { get; } = providerId;
        public int SearchCalls { get; private set; }
        public int FetchCalls { get; private set; }
        public Func<LyricSearchPlan, CancellationToken, Task<IReadOnlyList<SourceTrackCandidate>>> SearchHandler { get; set; } =
            (_, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>([]);
        public Func<SourceTrackCandidate, CancellationToken, Task<RawLyricPayload?>> FetchHandler { get; set; } =
            (_, _) => Task.FromResult<RawLyricPayload?>(null);

        public Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
            LyricSearchPlan plan,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return SearchHandler(plan, cancellationToken);
        }

        public Task<RawLyricPayload?> FetchAsync(
            SourceTrackCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            FetchCalls++;
            return FetchHandler(candidate, cancellationToken);
        }
    }

    private sealed class PlainTextParser : ILyricPayloadParser
    {
        public bool CanParse(LyricPayloadFormat format) => format == LyricPayloadFormat.PlainText;

        public Task<ParsedLyrics> ParseAsync(
            DecodedLyricPayload payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ParsedLyrics(
                [new ParsedLyricLine(TimeSpan.Zero, null, payload.OriginalLyrics ?? "parsed lyrics")],
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                LyricPayloadFormat.PlainText));
        }
    }

    private sealed class EmptyPipelineCache : ILyricPipelineCache
    {
        public bool TryGetRaw(
            LyricProviderId providerId,
            string candidateId,
            out RawLyricPayload? payload,
            out LyricAcquisitionKind acquisition)
        {
            payload = null;
            acquisition = LyricAcquisitionKind.Unknown;
            return false;
        }

        public void StoreRaw(RawLyricPayload payload, DateTimeOffset fetchedAtUtc)
        {
        }

        public void Invalidate(LyricProviderId providerId, string candidateId)
        {
        }

        public bool TryGetParsed(
            RawLyricPayload rawPayload,
            string parserId,
            string parserVersion,
            string normalizationVersion,
            out ParsedLyrics? parsedLyrics,
            out LyricAcquisitionKind acquisition)
        {
            parsedLyrics = null;
            acquisition = LyricAcquisitionKind.Unknown;
            return false;
        }

        public void StoreParsed(
            RawLyricPayload rawPayload,
            ParsedLyrics parsedLyrics,
            string parserId,
            string parserVersion,
            string normalizationVersion)
        {
        }
    }

    private sealed class CacheFile : IDisposable
    {
        private CacheFile(string directoryPath)
        {
            DirectoryPath = directoryPath;
            Path = System.IO.Path.Combine(directoryPath, JsonResolvedLyricCache.DefaultFileName);
        }

        public string DirectoryPath { get; }
        public string Path { get; }

        public static CacheFile Create()
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TaskbarLyrics",
                "ResolvedLyricCacheCoordinatorTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new CacheFile(directory);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
