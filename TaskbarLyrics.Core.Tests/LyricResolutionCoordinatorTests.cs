using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricResolutionCoordinatorTests
{
    [Fact]
    public async Task OnlineSelectionPicksHighestTrustSourceWhenPrimaryIsRejected()
    {
        var track = CreateTrack("Trust Song");
        var qq = CreateValidSource(
            KnownLyricProviders.QQMusic,
            searchDelay: TimeSpan.FromMilliseconds(120),
            candidateTitle: "Trust Songs",
            candidateDuration: track.Duration - TimeSpan.FromSeconds(12));
        var kugou = CreateValidSource(
            KnownLyricProviders.Kugou,
            searchDelay: TimeSpan.FromMilliseconds(5));
        var netease = CreateValidSource(
            KnownLyricProviders.Netease,
            searchDelay: TimeSpan.FromMilliseconds(10));
        var lrclib = CreateValidSource(
            KnownLyricProviders.LrcLib,
            searchDelay: TimeSpan.FromMilliseconds(15));

        using var coordinator = CreateCoordinator([qq, kugou, netease, lrclib]);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.Kugou, resolved!.ProviderId);
        Assert.Equal("lyrics:Kugou:Trust Song", resolved.Content.Lines[0].Text);
        Assert.All(new[] { qq, kugou, netease, lrclib }, source => Assert.Equal(1, source.SearchCalls));
    }

    [Fact]
    public async Task PrimarySourceImmediateAcceptanceWhenScoreAtLeast95()
    {
        var track = CreateTrack("Perfect Song");
        var qq = CreateValidSource(KnownLyricProviders.QQMusic);
        var kugou = CreateValidSource(
            KnownLyricProviders.Kugou,
            searchDelay: TimeSpan.FromMilliseconds(200));
        var netease = CreateNoLyricsSource(KnownLyricProviders.Netease);
        var lrclib = CreateNoLyricsSource(KnownLyricProviders.LrcLib);

        using var coordinator = CreateCoordinator([qq, kugou, netease, lrclib]);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.QQMusic, resolved!.ProviderId);
        var score = int.Parse(
            resolved.Diagnostics["identityScore"],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(score >= LyricMatchingPolicy.ImmediateAcceptanceScore);
    }

    [Fact]
    public async Task PrimarySourceBelow95SelectsHighestTrust95PlusSource()
    {
        var track = CreateTrack("Edge Song");
        var qq = CreateValidSource(
            KnownLyricProviders.QQMusic,
            candidateTitle: "Edge Songs",
            candidateDuration: track.Duration - TimeSpan.FromSeconds(8));
        var kugou = CreateValidSource(KnownLyricProviders.Kugou);
        var netease = CreateNoLyricsSource(KnownLyricProviders.Netease);
        var lrclib = CreateNoLyricsSource(KnownLyricProviders.LrcLib);

        using var coordinator = CreateCoordinator([qq, kugou, netease, lrclib]);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.Kugou, resolved!.ProviderId);
        var selectedScore = int.Parse(
            resolved.Diagnostics["identityScore"],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(selectedScore >= LyricMatchingPolicy.ImmediateAcceptanceScore);
    }

    [Fact]
    public async Task TrustOrderTakesPrecedenceOverScoreWhenMultiple95PlusSources()
    {
        var track = CreateTrack("Priority Song");
        var qq = CreateValidSource(
            KnownLyricProviders.QQMusic,
            candidateTitle: "Priority Songs",
            candidateDuration: track.Duration - TimeSpan.FromSeconds(8));
        var kugou = CreateValidSource(
            KnownLyricProviders.Kugou,
            candidateDuration: track.Duration - TimeSpan.FromSeconds(3));
        var netease = CreateValidSource(KnownLyricProviders.Netease);
        var lrclib = CreateNoLyricsSource(KnownLyricProviders.LrcLib);

        using var coordinator = CreateCoordinator([qq, kugou, netease, lrclib]);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.Kugou, resolved!.ProviderId);
        var selectedScore = int.Parse(
            resolved.Diagnostics["identityScore"],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(selectedScore >= LyricMatchingPolicy.ImmediateAcceptanceScore);
    }

    [Fact]
    public async Task FallbackTo80PlusTrustOrderedWhenNo95PlusSource()
    {
        var track = CreateTrack("Fuzzy Song");
        var qq = CreateValidSource(
            KnownLyricProviders.QQMusic,
            candidateTitle: "Fuzzy Songs",
            candidateDuration: track.Duration - TimeSpan.FromSeconds(8));
        var kugou = CreateValidSource(
            KnownLyricProviders.Kugou,
            candidateDuration: track.Duration - TimeSpan.FromSeconds(5));
        var netease = CreateNoLyricsSource(KnownLyricProviders.Netease);
        var lrclib = CreateNoLyricsSource(KnownLyricProviders.LrcLib);

        using var coordinator = CreateCoordinator([qq, kugou, netease, lrclib]);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.QQMusic, resolved!.ProviderId);
        var selectedScore = int.Parse(
            resolved.Diagnostics["identityScore"],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(selectedScore >= LyricMatchingPolicy.MinimumAcceptedMatchScore);
        Assert.True(selectedScore < LyricMatchingPolicy.ImmediateAcceptanceScore);
    }

    [Fact]
    public async Task IdentityRejectionAllowsLowerTrustSource()
    {
        var track = CreateTrack("Identity Song");
        var qq = CreateValidSource(KnownLyricProviders.QQMusic, candidateTitle: "Identity Song (Live)");
        var kugou = CreateValidSource(KnownLyricProviders.Kugou);
        var netease = CreateNoLyricsSource(KnownLyricProviders.Netease);
        var lrclib = CreateNoLyricsSource(KnownLyricProviders.LrcLib);

        using var coordinator = CreateCoordinator([qq, kugou, netease, lrclib]);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.Kugou, resolved!.ProviderId);
        Assert.Equal(0, qq.FetchCalls);
        Assert.Equal("lyrics:Kugou:Identity Song", resolved.Content.Lines[0].Text);
    }

    [Fact]
    public async Task HigherTrustNoLyricsIsTerminalAndAllowsLowerTrustSource()
    {
        var track = CreateTrack("No Lyrics Song");
        var qq = CreateNoLyricsSource(KnownLyricProviders.QQMusic);
        var kugou = CreateValidSource(KnownLyricProviders.Kugou);
        var netease = CreateNoLyricsSource(KnownLyricProviders.Netease);
        var lrclib = CreateNoLyricsSource(KnownLyricProviders.LrcLib);

        using var coordinator = CreateCoordinator([qq, kugou, netease, lrclib]);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.Kugou, resolved!.ProviderId);
    }

    [Fact]
    public async Task HigherTrustFailureIsTerminalAndAllowsLowerTrustSource()
    {
        var track = CreateTrack("Failed Source Song");
        var qq = CreateFailingSource(KnownLyricProviders.QQMusic);
        var kugou = CreateValidSource(KnownLyricProviders.Kugou);
        var netease = CreateNoLyricsSource(KnownLyricProviders.Netease);
        var lrclib = CreateNoLyricsSource(KnownLyricProviders.LrcLib);

        using var coordinator = CreateCoordinator([qq, kugou, netease, lrclib]);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.Kugou, resolved!.ProviderId);
    }

    [Fact]
    public async Task PreferredProviderFailureDoesNotFallbackAndUsesMappedIdentity()
    {
        var track = CreateTrack("Mapped Song");
        var mapping = new TestMappingResolver(
            new LyricMapping(
                "Mapped title",
                "Mapped artist",
                KnownLyricProviders.Kugou.Value,
                false,
                Album: "Mapped album"));
        var qq = CreateValidSource(KnownLyricProviders.QQMusic);
        var kugou = CreateNoLyricsSource(KnownLyricProviders.Kugou);
        var netease = CreateValidSource(KnownLyricProviders.Netease);
        var lrclib = CreateValidSource(KnownLyricProviders.LrcLib);

        using var coordinator = CreateCoordinator([qq, kugou, netease, lrclib], mapping);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.Null(resolved);
        Assert.Equal(0, qq.SearchCalls);
        Assert.Equal(0, netease.SearchCalls);
        Assert.Equal(0, lrclib.SearchCalls);
        Assert.Equal(1, kugou.SearchCalls);
        Assert.NotNull(kugou.LastPlan);
        Assert.Equal("Mapped title", kugou.LastPlan!.OriginalTrack.Title);
        Assert.Equal("Mapped artist", kugou.LastPlan.OriginalTrack.PrimaryArtist);
        Assert.Equal("Mapped album", kugou.LastPlan.OriginalTrack.Album);
    }

    [Fact]
    public async Task ValidLocalLyricsShortCircuitOnlineSources()
    {
        var track = CreateTrack("Local Song");
        var local = new TestProvider(
            "Local",
            (_, _) => Task.FromResult<LyricDocument?>(
                new LyricDocument([new LyricLine(TimeSpan.Zero, "local lyrics")])));
        var sources = CreateValidSources();

        using var coordinator = CreateCoordinator(sources, localProvider: local);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.Local, resolved!.ProviderId);
        Assert.Equal("local lyrics", resolved.Content.Lines[0].Text);
        Assert.Equal(1, local.GetLyricsCalls);
        Assert.All(sources, source => Assert.Equal(0, source.SearchCalls));
    }

    [Fact]
    public async Task LocalSyllablesSurviveSemanticAndCompatibilityProjection()
    {
        var lineStart = TimeSpan.FromSeconds(10);
        var local = new TestProvider(
            "Local",
            (_, _) => Task.FromResult<LyricDocument?>(
                new LyricDocument(
                [
                    new LyricLine(
                        lineStart,
                        "one two",
                        "translation",
                        [
                            new LyricSyllable(TimeSpan.Zero, TimeSpan.FromMilliseconds(200), "one"),
                            new LyricSyllable(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(300), "two")
                        ]),
                    new LyricLine(TimeSpan.FromSeconds(11), "next")
                ])));
        var sources = CreateValidSources();

        using var coordinator = CreateCoordinator(sources, localProvider: local);

        var resolved = await coordinator.ResolveAsync(CreateTrack("Local Syllable Song"));

        Assert.NotNull(resolved);
        Assert.Equal(LyricTimingKind.Mixed, resolved!.Content.TimingKind);
        Assert.Equal(LyricTimingProvenance.Unknown, resolved.Content.TimingProvenance);
        var parsedLine = resolved.Content.Lines[0];
        Assert.Equal(TimeSpan.FromSeconds(11), parsedLine.EndTime);
        Assert.Equal("translation", parsedLine.Translation);
        Assert.Collection(parsedLine.Segments,
            segment =>
            {
                Assert.Equal(lineStart, segment.StartTime);
                Assert.Equal(lineStart + TimeSpan.FromMilliseconds(200), segment.EndTime);
                Assert.Equal("one", segment.Text);
            },
            segment =>
            {
                Assert.Equal(lineStart + TimeSpan.FromMilliseconds(200), segment.StartTime);
                Assert.Equal(lineStart + TimeSpan.FromMilliseconds(500), segment.EndTime);
                Assert.Equal("two", segment.Text);
            });

        var compatibleDocument = ResolvedLyricsCompatibilityProjector.ToLyricDocument(resolved);
        var compatibleLine = compatibleDocument.Lines[0];
        Assert.Collection(compatibleLine.Syllables!,
            syllable =>
            {
                Assert.Equal(TimeSpan.Zero, syllable.RelativeOffset);
                Assert.Equal(TimeSpan.FromMilliseconds(200), syllable.Duration);
                Assert.Equal("one", syllable.Text);
            },
            syllable =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(200), syllable.RelativeOffset);
                Assert.Equal(TimeSpan.FromMilliseconds(300), syllable.Duration);
                Assert.Equal("two", syllable.Text);
            });
    }

    [Fact]
    public async Task InvalidLocalSyllableMakesSemanticProjectionAtomicPerLine()
    {
        var lineStart = TimeSpan.FromSeconds(10);
        var local = new TestProvider(
            "Local",
            (_, _) => Task.FromResult<LyricDocument?>(
                new LyricDocument(
                [
                    new LyricLine(
                        lineStart,
                        "one two",
                        "translation",
                        [
                            new LyricSyllable(TimeSpan.Zero, TimeSpan.FromMilliseconds(200), "one"),
                            new LyricSyllable(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(300), "two"),
                            new LyricSyllable(TimeSpan.FromMilliseconds(-1), TimeSpan.FromMilliseconds(100), "negative offset"),
                            new LyricSyllable(TimeSpan.FromMilliseconds(500), TimeSpan.Zero, "zero duration"),
                            new LyricSyllable(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(100), " "),
                            new LyricSyllable(TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(200), "past line end")
                        ]),
                    new LyricLine(TimeSpan.FromSeconds(11), "next")
                ])));
        var sources = CreateValidSources();

        using var coordinator = CreateCoordinator(sources, localProvider: local);

        var resolved = await coordinator.ResolveAsync(CreateTrack("Invalid Local Syllable Song"));

        Assert.NotNull(resolved);
        Assert.Equal(LyricTimingKind.LineTimed, resolved!.Content.TimingKind);
        var parsedLine = resolved.Content.Lines[0];
        Assert.Equal(TimeSpan.FromSeconds(11), parsedLine.EndTime);
        Assert.Equal("translation", parsedLine.Translation);
        Assert.Empty(parsedLine.Segments);

        var compatibleDocument = ResolvedLyricsCompatibilityProjector.ToLyricDocument(resolved);
        Assert.Null(compatibleDocument.Lines[0].Syllables);
    }

    [Fact]
    public async Task PureMusicMappingShortCircuitDoesNotCallLocalOrOnlineSources()
    {
        var track = CreateTrack("Pure Music Song");
        var mapping = new TestMappingResolver(
            new LyricMapping("Mapped Instrumental", "Mapped Artist", null, true));
        var local = new TestProvider(
            "Local",
            (_, _) => Task.FromResult<LyricDocument?>(
                new LyricDocument([new LyricLine(TimeSpan.Zero, "unexpected local lyrics")])));
        var sources = CreateValidSources();

        using var coordinator = CreateCoordinator(sources, mapping, local);

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.Local, resolved!.ProviderId);
        Assert.Equal(LyricAcquisitionKind.SongMapping, resolved.Acquisition);
        Assert.True(resolved.Content.IsPureMusic);
        Assert.Equal(0, local.GetLyricsCalls);
        Assert.All(sources, source => Assert.Equal(0, source.SearchCalls));
    }

    [Fact]
    public void TrustPolicyRejectsDuplicateMissingAndUnknownProviderIds()
    {
        var registered = new[]
        {
            KnownLyricProviders.QQMusic,
            KnownLyricProviders.Kugou,
            KnownLyricProviders.Netease
        };

        Assert.Throws<ArgumentException>(() =>
            new LyricProviderTrustPolicy(
                [KnownLyricProviders.QQMusic, new LyricProviderId("qqmusic"), KnownLyricProviders.Netease],
                registered));
        Assert.Throws<ArgumentException>(() =>
            new LyricProviderTrustPolicy(
                [KnownLyricProviders.QQMusic, KnownLyricProviders.Netease, KnownLyricProviders.LrcLib],
                registered));
        Assert.Throws<ArgumentException>(() =>
            new LyricProviderTrustPolicy(
                [KnownLyricProviders.QQMusic, KnownLyricProviders.Kugou],
                registered));
    }

    [Fact]
    public async Task TrackReplacementCancellationPreventsLateResultFromWinning()
    {
        var trackA = CreateTrack("First Song");
        var trackB = CreateTrack("Second Song");
        var firstSearchRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = CreateValidSource(KnownLyricProviders.QQMusic);
        var searchCall = 0;
        source.SearchHandler = async (plan, _) =>
        {
            if (Interlocked.Increment(ref searchCall) == 1)
            {
                await firstSearchRelease.Task;
            }

            return [CreateCandidate(source.ProviderId, plan)];
        };
        var otherSources = new[]
        {
            source,
            CreateNoLyricsSource(KnownLyricProviders.Kugou),
            CreateNoLyricsSource(KnownLyricProviders.Netease),
            CreateNoLyricsSource(KnownLyricProviders.LrcLib)
        };

        using var coordinator = CreateCoordinator(otherSources);
        using var replacementCancellation = new CancellationTokenSource();
        var firstResolution = coordinator.ResolveAsync(trackA, replacementCancellation.Token);
        await source.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        replacementCancellation.Cancel();
        firstSearchRelease.TrySetResult(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstResolution);
        var secondResolution = await coordinator.ResolveAsync(trackB);

        Assert.NotNull(secondResolution);
        Assert.Equal(KnownLyricProviders.QQMusic, secondResolution!.ProviderId);
        Assert.Equal("lyrics:QQMusic:Second Song", secondResolution.Content.Lines[0].Text);
    }

    [Fact]
    public async Task DisposeCancelsInFlightResolutionDisposesSourcesAndIsIdempotent()
    {
        var sources = CreateCancellationSensitiveSources();
        using var coordinator = CreateCoordinator(sources);
        var resolution = coordinator.ResolveAsync(CreateTrack("Dispose Song"));
        await sources[0].SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.Dispose();
        coordinator.Dispose();

        Assert.Null(await resolution);
        await WaitUntilAsync(
            () => sources.All(source => source.IsDisposed),
            TimeSpan.FromSeconds(2));
        var afterDispose = await coordinator.ResolveAsync(CreateTrack("After Dispose"));

        Assert.Null(afterDispose);
        Assert.All(sources, source => Assert.Equal(1, source.SearchCalls));
    }

    private static LyricResolutionCoordinator CreateCoordinator(
        IEnumerable<TestSource> sources,
        TestMappingResolver? mappingResolver = null,
        ILyricProvider? localProvider = null,
        LyricProviderTrustPolicy? trustPolicy = null,
        TimeSpan? sourceTimeout = null)
    {
        return new LyricResolutionCoordinator(
            sources,
            Array.Empty<ILyricPayloadDecoder>(),
            [new PlainTextParser()],
            new RecordingCache(),
            mappingResolver ?? new TestMappingResolver(),
            localProvider,
            trustPolicy,
            sourceTimeout ?? TimeSpan.FromSeconds(1));
    }

    private static TestSource[] CreateValidSources() =>
    [
        CreateValidSource(KnownLyricProviders.QQMusic),
        CreateValidSource(KnownLyricProviders.Kugou),
        CreateValidSource(KnownLyricProviders.Netease),
        CreateValidSource(KnownLyricProviders.LrcLib)
    ];

    private static TestSource CreateValidSource(
        LyricProviderId providerId,
        TimeSpan? searchDelay = null,
        string? candidateTitle = null,
        TimeSpan? candidateDuration = null)
    {
        var source = new TestSource(providerId);
        source.SearchHandler = async (plan, cancellationToken) =>
        {
            if (searchDelay is { } delay && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            return [CreateCandidate(source.ProviderId, plan, candidateTitle, candidateDuration)];
        };
        source.FetchHandler = (candidate, _) => Task.FromResult<RawLyricPayload?>(
            new RawLyricPayload(
                source.ProviderId,
                candidate.CandidateId,
                LyricPayloadFormat.PlainText,
                $"lyrics:{candidate.CandidateId}",
                null,
                false,
                false,
                new Dictionary<string, string>()));
        return source;
    }

    private static TestSource CreateNoLyricsSource(LyricProviderId providerId)
    {
        var source = new TestSource(providerId)
        {
            SearchHandler = (_, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>([]),
            FetchHandler = (_, _) => throw new InvalidOperationException("A no-lyrics source must not fetch.")
        };
        return source;
    }

    private static TestSource CreateFailingSource(LyricProviderId providerId)
    {
        var source = new TestSource(providerId)
        {
            SearchHandler = (_, _) => throw new InvalidOperationException("synthetic source failure"),
            FetchHandler = (_, _) => throw new InvalidOperationException("A failed source must not fetch.")
        };
        return source;
    }

    private static TestSource[] CreateCancellationSensitiveSources()
    {
        return KnownLyricProviders.OnlineTrustOrder
            .Select(providerId =>
            {
                var source = new TestSource(providerId);
                source.SearchHandler = async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return Array.Empty<SourceTrackCandidate>();
                };
                source.FetchHandler = (_, _) => throw new InvalidOperationException("A canceled source must not fetch.");
                return source;
            })
            .ToArray();
    }

    private static SourceTrackCandidate CreateCandidate(
        LyricProviderId providerId,
        LyricSearchPlan plan,
        string? title = null,
        TimeSpan? duration = null)
    {
        var original = plan.OriginalTrack;
        return new SourceTrackCandidate(
            providerId,
            $"{providerId.Value}:{original.Title}",
            title ?? original.Title,
            original.Artists,
            original.Album,
            duration ?? original.Duration,
            plan.Variants[0].Id,
            new Dictionary<string, string>());
    }

    private static TrackInfo CreateTrack(string title, string sourceApp = "TestPlayer") =>
        new(
            $"track:{title}",
            title,
            "Trust Artist",
            "Trust Album",
            sourceApp,
            TimeSpan.FromMinutes(3));

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(predicate(), "The expected condition was not reached before the timeout.");
    }

    private sealed class TestMappingResolver : ILyricMappingResolver
    {
        private readonly LyricMapping? _mapping;

        public TestMappingResolver(LyricMapping? mapping = null)
        {
            _mapping = mapping;
        }

        public int ResolveCalls { get; private set; }

        public LyricMapping Resolve(TrackInfo track)
        {
            ResolveCalls++;
            return _mapping ?? LyricMapping.Unchanged(track);
        }
    }

    private sealed class TestSource : ILyricSource, IDisposable
    {
        public TestSource(LyricProviderId providerId)
        {
            ProviderId = providerId;
        }

        public LyricProviderId ProviderId { get; }

        public Func<LyricSearchPlan, CancellationToken, Task<IReadOnlyList<SourceTrackCandidate>>> SearchHandler { get; set; } =
            (_, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>([]);

        public Func<SourceTrackCandidate, CancellationToken, Task<RawLyricPayload?>> FetchHandler { get; set; } =
            (_, _) => Task.FromResult<RawLyricPayload?>(null);

        public TaskCompletionSource<bool> SearchStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LyricSearchPlan? LastPlan { get; private set; }
        public int SearchCalls { get; private set; }
        public int FetchCalls { get; private set; }
        public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

        private int _isDisposed;

        public Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
            LyricSearchPlan plan,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            LastPlan = plan;
            SearchStarted.TrySetResult(true);
            return SearchHandler(plan, cancellationToken);
        }

        public Task<RawLyricPayload?> FetchAsync(
            SourceTrackCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            FetchCalls++;
            return FetchHandler(candidate, cancellationToken);
        }

        public void Dispose() => Interlocked.Exchange(ref _isDisposed, 1);
    }

    private sealed class TestProvider : ILyricProvider, IDisposable
    {
        private readonly Func<TrackInfo, CancellationToken, Task<LyricDocument?>> _getLyrics;
        private int _isDisposed;

        public TestProvider(
            string sourceApp,
            Func<TrackInfo, CancellationToken, Task<LyricDocument?>> getLyrics)
        {
            SourceApp = sourceApp;
            _getLyrics = getLyrics;
        }

        public string SourceApp { get; }
        public int GetLyricsCalls { get; private set; }

        public Task<LyricDocument?> GetLyricsAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default)
        {
            GetLyricsCalls++;
            return _getLyrics(track, cancellationToken);
        }

        public void Dispose() => Interlocked.Exchange(ref _isDisposed, 1);
    }

    private sealed class PlainTextParser : ILyricPayloadParser
    {
        public bool CanParse(LyricPayloadFormat format) => format == LyricPayloadFormat.PlainText;

        public Task<ParsedLyrics> ParseAsync(
            DecodedLyricPayload payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = string.IsNullOrWhiteSpace(payload.OriginalLyrics)
                ? "parsed lyrics"
                : payload.OriginalLyrics;
            return Task.FromResult(new ParsedLyrics(
                [new ParsedLyricLine(TimeSpan.Zero, null, text)],
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                payload.Format,
                payload.IsPureMusic));
        }
    }

    private sealed class RecordingCache : ILyricPipelineCache
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<string, RawLyricPayload> _raw = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ParsedLyrics> _parsed = new(StringComparer.Ordinal);

        public bool TryGetRaw(
            LyricProviderId providerId,
            string candidateId,
            out RawLyricPayload? payload,
            out LyricAcquisitionKind acquisition)
        {
            lock (_syncRoot)
            {
                if (_raw.TryGetValue(GetRawKey(providerId, candidateId), out payload))
                {
                    acquisition = LyricAcquisitionKind.MemoryCache;
                    return true;
                }
            }

            payload = null;
            acquisition = LyricAcquisitionKind.Unknown;
            return false;
        }

        public void StoreRaw(RawLyricPayload payload, DateTimeOffset fetchedAtUtc)
        {
            lock (_syncRoot)
            {
                _raw[GetRawKey(payload.ProviderId, payload.CandidateId)] = payload;
            }
        }

        public bool TryGetParsed(
            RawLyricPayload rawPayload,
            string parserId,
            string parserVersion,
            string normalizationVersion,
            out ParsedLyrics? parsedLyrics,
            out LyricAcquisitionKind acquisition)
        {
            lock (_syncRoot)
            {
                if (_parsed.TryGetValue(
                        GetParsedKey(rawPayload, parserId, parserVersion, normalizationVersion),
                        out parsedLyrics))
                {
                    acquisition = LyricAcquisitionKind.MemoryCache;
                    return true;
                }
            }

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
            lock (_syncRoot)
            {
                _parsed[GetParsedKey(rawPayload, parserId, parserVersion, normalizationVersion)] = parsedLyrics;
            }
        }

        private static string GetRawKey(LyricProviderId providerId, string candidateId) =>
            $"{providerId.Value}\u001f{candidateId}";

        private static string GetParsedKey(
            RawLyricPayload rawPayload,
            string parserId,
            string parserVersion,
            string normalizationVersion) =>
            $"{GetRawKey(rawPayload.ProviderId, rawPayload.CandidateId)}\u001f{parserId}\u001f{parserVersion}\u001f{normalizationVersion}";
    }
}
