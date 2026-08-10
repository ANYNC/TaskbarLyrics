using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricDiagnosticRunnerTests
{
    [Fact]
    public async Task RunAsyncWaitsForEverySourceAndSelectsTheHighestTrustResult()
    {
        var track = CreateTrack();
        var delays = new Dictionary<LyricProviderId, int>
        {
            [KnownLyricProviders.QQMusic] = 120,
            [KnownLyricProviders.Kugou] = 15,
            [KnownLyricProviders.Netease] = 35,
            [KnownLyricProviders.LrcLib] = 5
        };
        var sources = KnownLyricProviders.OnlineTrustOrder
            .Select(providerId => CreateSuccessfulSource(providerId, delays[providerId]))
            .ToArray();

        var runner = CreateRunner(sources);

        var report = await runner.RunAsync(track);

        Assert.Null(report.Error);
        Assert.Equal(
            KnownLyricProviders.OnlineTrustOrder.Select(providerId => providerId.Value),
            report.Providers.Select(provider => provider.ProviderId));
        Assert.All(report.Providers, provider =>
        {
            Assert.Equal(LyricSourceTerminalState.Succeeded, provider.State);
            Assert.NotEmpty(provider.Candidates);
        });
        Assert.All(sources, source => Assert.Equal(1, source.SearchCalls));
        Assert.Equal(KnownLyricProviders.QQMusic.Value, report.Selection!.ProviderId);
        Assert.Equal("QQMusic-candidate", report.Selection.CandidateId);
        Assert.True(report.Providers.Single(provider => provider.ProviderId == KnownLyricProviders.QQMusic.Value).Selected);
        Assert.All(
            report.Providers.Where(provider => provider.ProviderId != KnownLyricProviders.QQMusic.Value),
            provider => Assert.False(provider.Selected));
    }

    [Fact]
    public async Task RunAsyncReportsAdmissionReasonsAndSortedCandidateMetadataKeys()
    {
        var source = new TestSource(KnownLyricProviders.QQMusic);
        source.SearchHandler = (plan, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>
        ([
            CreateCandidate(
                source,
                plan,
                "rejected",
                "A Completely Different Song",
                new Dictionary<string, string>
                {
                    ["rejected-key"] = "value"
                }),
            CreateCandidate(
                source,
                plan,
                "admitted",
                plan.OriginalTrack.Title,
                new Dictionary<string, string>
                {
                    ["zeta"] = "last",
                    ["alpha"] = "first"
                })
        ]);
        source.FetchHandler = (candidate, _) => Task.FromResult<RawLyricPayload?>(CreatePayload(source, candidate));

        var report = await CreateRunner([source]).RunAsync(CreateTrack());

        var provider = Assert.Single(report.Providers);
        var rejected = Assert.Single(provider.Candidates, candidate => candidate.CandidateId == "rejected");
        Assert.False(rejected.IsAdmitted);
        Assert.True(rejected.Score < LyricMatchingPolicy.MinimumAcceptedMatchScore);
        Assert.Contains("below-admission-threshold", rejected.RejectionReasons);
        Assert.Collection(rejected.FetchMetadataKeys, key => Assert.Equal("rejected-key", key));

        var admitted = Assert.Single(provider.Candidates, candidate => candidate.CandidateId == "admitted");
        Assert.True(admitted.IsAdmitted);
        Assert.True(admitted.Score >= LyricMatchingPolicy.MinimumAcceptedMatchScore);
        Assert.Collection(
            admitted.FetchMetadataKeys,
            key => Assert.Equal("alpha", key),
            key => Assert.Equal("zeta", key));
        Assert.Equal("admitted", report.Selection!.CandidateId);
    }

    [Fact]
    public async Task RunAsyncUsesNoOpCacheAndDoesNotReuseResultsAcrossRuns()
    {
        var source = CreateSuccessfulSource(KnownLyricProviders.QQMusic, searchDelayMilliseconds: 0);

        var firstReport = await CreateRunner([source]).RunAsync(CreateTrack());
        var secondReport = await CreateRunner([source]).RunAsync(CreateTrack());

        Assert.Equal(LyricAcquisitionKind.Remote, firstReport.Selection!.Acquisition);
        Assert.Equal(LyricAcquisitionKind.Remote, secondReport.Selection!.Acquisition);
        Assert.Equal(2, source.SearchCalls);
        Assert.Equal(2, source.FetchCalls);
    }

    [Fact]
    public async Task RunAsyncPropagatesCancellationToEveryInFlightSource()
    {
        var sources = KnownLyricProviders.OnlineTrustOrder
            .Select(providerId => CreateCancellationSensitiveSource(providerId))
            .ToArray();
        var runner = CreateRunner(sources, sourceTimeout: TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var run = runner.RunAsync(CreateTrack(), cancellation.Token);

        await Task.WhenAll(sources.Select(source => source.SearchStarted.Task))
            .WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run);
        await Task.WhenAll(sources.Select(source => source.SearchCancelled.Task))
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static LyricDiagnosticRunner CreateRunner(
        IEnumerable<TestSource> sources,
        TimeSpan? sourceTimeout = null) =>
        new(
            sources,
            Array.Empty<ILyricPayloadDecoder>(),
            [new PlainTextParser()],
            trustPolicy: new LyricProviderTrustPolicy(
                KnownLyricProviders.OnlineTrustOrder.Where(providerId => sources.Any(source => source.ProviderId == providerId)),
                sources.Select(source => source.ProviderId)),
            sourceTimeout: sourceTimeout);

    private static TestSource CreateSuccessfulSource(
        LyricProviderId providerId,
        int searchDelayMilliseconds)
    {
        var source = new TestSource(providerId);
        source.SearchHandler = async (plan, cancellationToken) =>
        {
            if (searchDelayMilliseconds > 0)
            {
                await Task.Delay(searchDelayMilliseconds, cancellationToken);
            }

            return [CreateCandidate(source, plan, $"{providerId.Value}-candidate", plan.OriginalTrack.Title)];
        };
        source.FetchHandler = (candidate, _) => Task.FromResult<RawLyricPayload?>(CreatePayload(source, candidate));
        return source;
    }

    private static TestSource CreateCancellationSensitiveSource(LyricProviderId providerId)
    {
        var source = new TestSource(providerId);
        source.SearchHandler = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Array.Empty<SourceTrackCandidate>();
        };
        return source;
    }

    private static SourceTrackCandidate CreateCandidate(
        TestSource source,
        LyricSearchPlan plan,
        string candidateId,
        string title,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var track = plan.OriginalTrack;
        return new SourceTrackCandidate(
            source.ProviderId,
            candidateId,
            title,
            track.Artists,
            track.Album,
            track.Duration,
            plan.Variants[0].Id,
            metadata ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static RawLyricPayload CreatePayload(TestSource source, SourceTrackCandidate candidate) =>
        new(
            source.ProviderId,
            candidate.CandidateId,
            LyricPayloadFormat.PlainText,
            $"lyrics:{candidate.CandidateId}",
            null,
            false,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static TrackInfo CreateTrack() =>
        new(
            "diagnostic-track",
            "Diagnostic Song",
            "Diagnostic Artist",
            "Diagnostic Album",
            "TestPlayer",
            TimeSpan.FromMinutes(3));

    private sealed class TestSource(LyricProviderId providerId) : ILyricSource
    {
        public LyricProviderId ProviderId { get; } = providerId;

        public Func<LyricSearchPlan, CancellationToken, Task<IReadOnlyList<SourceTrackCandidate>>> SearchHandler { get; set; } =
            (_, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>([]);

        public Func<SourceTrackCandidate, CancellationToken, Task<RawLyricPayload?>> FetchHandler { get; set; } =
            (_, _) => Task.FromResult<RawLyricPayload?>(null);

        public TaskCompletionSource<bool> SearchStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> SearchCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SearchCalls { get; private set; }
        public int FetchCalls { get; private set; }

        public async Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
            LyricSearchPlan plan,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            SearchStarted.TrySetResult(true);
            try
            {
                return await SearchHandler(plan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SearchCancelled.TrySetResult(true);
                throw;
            }
        }

        public async Task<RawLyricPayload?> FetchAsync(
            SourceTrackCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            FetchCalls++;
            return await FetchHandler(candidate, cancellationToken);
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
                [new ParsedLyricLine(TimeSpan.Zero, null, payload.OriginalLyrics ?? "")],
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                payload.Format));
        }
    }
}
