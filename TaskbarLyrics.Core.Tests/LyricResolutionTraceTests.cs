using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricResolutionTraceTests
{
    [Fact]
    public async Task TraceCapturesSmtcInputSearchVariantsCandidatesIdentityOutcomesAndSelection()
    {
        var track = new TrackInfo(
            "smtc-track-1",
            "Signal Song (feat. Guest)",
            "Primary, Guest",
            "Signal Album",
            "QQMusic",
            TimeSpan.FromMinutes(3),
            "qq-song-1");
        var qq = new TestSource(KnownLyricProviders.QQMusic);
        qq.SearchHandler = (plan, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>(
        [
            new SourceTrackCandidate(
                qq.ProviderId,
                "qq-wrong",
                "Different Song",
                ["Other Artist"],
                "Other Album",
                TimeSpan.FromMinutes(1),
                plan.Variants[0].Id,
                new Dictionary<string, string>
                {
                    ["mid"] = "qq-wrong-mid",
                    ["songId"] = "qq-wrong-song"
                }),
            new SourceTrackCandidate(
                qq.ProviderId,
                "qq-match",
                plan.OriginalTrack.Title,
                plan.OriginalTrack.Artists,
                plan.OriginalTrack.Album,
                plan.OriginalTrack.Duration,
                plan.Variants[0].Id,
                new Dictionary<string, string>
                {
                    ["mid"] = "qq-mid",
                    ["songId"] = "qq-song-1"
                })
        ]);
        qq.FetchHandler = (candidate, _) => Task.FromResult<RawLyricPayload?>(
            new RawLyricPayload(
                qq.ProviderId,
                candidate.CandidateId,
                LyricPayloadFormat.PlainText,
                "signal lyrics",
                null,
                false,
                false,
                new Dictionary<string, string>
                {
                    ["qqMetadata"] = "preserved"
                }));

        var lowerTrustSources = KnownLyricProviders.OnlineTrustOrder
            .Where(providerId => providerId != KnownLyricProviders.QQMusic)
            .Select(providerId =>
            {
                var source = new TestSource(providerId);
                source.SearchHandler = async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return Array.Empty<SourceTrackCandidate>();
                };
                return source;
            })
            .ToArray();
        var trace = new RecordingTraceSink();
        var cache = new RecordingCache();
        using var coordinator = new LyricResolutionCoordinator(
            [qq, .. lowerTrustSources],
            Array.Empty<ILyricPayloadDecoder>(),
            [new PlainTextParser()],
            cache,
            sourceTimeout: TimeSpan.FromSeconds(1),
            traceSink: trace);

        var resolved = await coordinator.ResolveAsync(track);
        await trace.WaitForSourcesAsync(4);

        Assert.NotNull(resolved);
        Assert.Equal(KnownLyricProviders.QQMusic, resolved!.ProviderId);
        Assert.Equal("qq-match", resolved.CandidateId);

        var request = Assert.Single(trace.Requests);
        Assert.Equal(track, request.OriginalTrack);
        Assert.Equal("QQMusic", request.OriginalTrack.SourceApp);
        Assert.Equal("qq-song-1", request.OriginalTrack.SongId);
        Assert.Equal(track, request.EffectiveTrack);
        Assert.Equal("QQMusic", request.EffectiveTrack.SourceApp);
        Assert.Contains(request.SearchPlan.Variants, variant => variant.Id == "exact");
        Assert.Contains(request.SearchPlan.Variants, variant => variant.Id == "primary-artist");
        Assert.Contains(request.SearchPlan.Variants, variant => variant.Id == "relaxed-title");

        var candidateTraces = trace.Candidates
            .Where(candidate => candidate.RequestId == request.RequestId)
            .ToArray();
        Assert.Equal(2, candidateTraces.Length);
        var rejected = Assert.Single(candidateTraces, candidate => candidate.Candidate.CandidateId == "qq-wrong");
        Assert.False(rejected.Evaluation.IsAdmitted);
        Assert.NotEmpty(rejected.Evaluation.RejectionReasons);
        var admitted = Assert.Single(candidateTraces, candidate => candidate.Candidate.CandidateId == "qq-match");
        Assert.True(admitted.Evaluation.IsAdmitted);
        Assert.Equal("qq-mid", admitted.Candidate.FetchMetadata["mid"]);
        Assert.Equal("qq-song-1", admitted.Candidate.FetchMetadata["songId"]);
        Assert.True(admitted.Evaluation.Score > 0);

        var sourceTraces = trace.Sources
            .Where(source => source.RequestId == request.RequestId)
            .ToArray();
        Assert.Equal(
            KnownLyricProviders.OnlineTrustOrder.Select(providerId => providerId.Value).OrderBy(value => value),
            sourceTraces.Select(source => source.ProviderId.Value).OrderBy(value => value));
        Assert.Equal(4, sourceTraces.Length);
        Assert.Equal(4, sourceTraces.Select(source => source.ProviderId).Distinct().Count());
        var qqOutcome = Assert.Single(sourceTraces, source => source.ProviderId == KnownLyricProviders.QQMusic);
        Assert.Equal(LyricSourceTerminalState.Succeeded, qqOutcome.State);
        Assert.All(
            sourceTraces.Where(source => source.ProviderId != KnownLyricProviders.QQMusic),
            source => Assert.Equal(LyricSourceTerminalState.Canceled, source.State));

        var selection = Assert.Single(trace.Selections);
        Assert.Equal(request.RequestId, selection.RequestId);
        Assert.NotNull(selection.Lyrics);
        Assert.Equal(KnownLyricProviders.QQMusic, selection.Lyrics!.ProviderId);
        Assert.Equal("qq-match", selection.Lyrics.CandidateId);
        Assert.Equal("exact", selection.Lyrics.Diagnostics["queryVariant"]);
        Assert.Equal(admitted.Evaluation.Score.ToString(System.Globalization.CultureInfo.InvariantCulture), selection.Lyrics.Diagnostics["identityScore"]);
        Assert.Equal("preserved", selection.Lyrics.Diagnostics["qqMetadata"]);

        Assert.Equal(1, cache.RawStoreCalls);
        Assert.Equal(1, cache.ParsedStoreCalls);
        Assert.DoesNotContain("qq-wrong", cache.StoredRawCandidateIds);
    }

    [Fact]
    public async Task ThrowingTraceSinkDoesNotAffectResolution()
    {
        var track = new TrackInfo(
            "trace-failure-track",
            "Trace Failure Song",
            "Trace Artist",
            "Trace Album",
            "QQMusic",
            TimeSpan.FromMinutes(3));
        var source = new TestSource(KnownLyricProviders.QQMusic);
        source.SearchHandler = (plan, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>(
        [
            new SourceTrackCandidate(
                source.ProviderId,
                "trace-failure-candidate",
                plan.OriginalTrack.Title,
                plan.OriginalTrack.Artists,
                plan.OriginalTrack.Album,
                plan.OriginalTrack.Duration,
                plan.Variants[0].Id,
                new Dictionary<string, string>())
        ]);
        source.FetchHandler = (candidate, _) => Task.FromResult<RawLyricPayload?>(
            new RawLyricPayload(
                source.ProviderId,
                candidate.CandidateId,
                LyricPayloadFormat.PlainText,
                "trace failure lyrics",
                null,
                false,
                false,
                new Dictionary<string, string>()));

        using var coordinator = new LyricResolutionCoordinator(
            [source],
            Array.Empty<ILyricPayloadDecoder>(),
            [new PlainTextParser()],
            new RecordingCache(),
            trustPolicy: new LyricProviderTrustPolicy([KnownLyricProviders.QQMusic], [KnownLyricProviders.QQMusic]),
            traceSink: new ThrowingTraceSink());

        var resolved = await coordinator.ResolveAsync(track);

        Assert.NotNull(resolved);
        Assert.Equal("trace-failure-candidate", resolved!.CandidateId);
    }

    private sealed class RecordingTraceSink : ILyricResolutionTraceSink
    {
        private readonly object _syncRoot = new();
        private readonly List<LyricResolutionRequestTrace> _requests = [];
        private readonly List<LyricResolutionCandidateTrace> _candidates = [];
        private readonly List<LyricResolutionSourceTrace> _sources = [];
        private readonly List<LyricResolutionSelectionTrace> _selections = [];

        public IReadOnlyList<LyricResolutionRequestTrace> Requests
        {
            get { lock (_syncRoot) return _requests.ToArray(); }
        }

        public IReadOnlyList<LyricResolutionCandidateTrace> Candidates
        {
            get { lock (_syncRoot) return _candidates.ToArray(); }
        }

        public LyricResolutionSourceTrace[] Sources
        {
            get { lock (_syncRoot) return _sources.ToArray(); }
        }

        public IReadOnlyList<LyricResolutionSelectionTrace> Selections
        {
            get { lock (_syncRoot) return _selections.ToArray(); }
        }

        public void RequestPrepared(LyricResolutionRequestTrace request)
        {
            lock (_syncRoot) _requests.Add(request);
        }

        public void CandidateEvaluated(LyricResolutionCandidateTrace candidate)
        {
            lock (_syncRoot) _candidates.Add(candidate);
        }

        public void SourceCompleted(LyricResolutionSourceTrace source)
        {
            lock (_syncRoot) _sources.Add(source);
        }

        public void SelectionCompleted(LyricResolutionSelectionTrace selection)
        {
            lock (_syncRoot) _selections.Add(selection);
        }

        public async Task WaitForSourcesAsync(int expectedCount)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (Sources.Length >= expectedCount)
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.Equal(expectedCount, Sources.Length);
        }
    }

    private sealed class ThrowingTraceSink : ILyricResolutionTraceSink
    {
        public void RequestPrepared(LyricResolutionRequestTrace request) => throw new InvalidOperationException("trace failure");

        public void CandidateEvaluated(LyricResolutionCandidateTrace candidate) => throw new InvalidOperationException("trace failure");

        public void SourceCompleted(LyricResolutionSourceTrace source) => throw new InvalidOperationException("trace failure");

        public void SelectionCompleted(LyricResolutionSelectionTrace selection) => throw new InvalidOperationException("trace failure");
    }

    private sealed class TestSource(LyricProviderId providerId) : ILyricSource
    {
        public LyricProviderId ProviderId { get; } = providerId;

        public Func<LyricSearchPlan, CancellationToken, Task<IReadOnlyList<SourceTrackCandidate>>> SearchHandler { get; set; } =
            (_, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>([]);

        public Func<SourceTrackCandidate, CancellationToken, Task<RawLyricPayload?>> FetchHandler { get; set; } =
            (_, _) => Task.FromResult<RawLyricPayload?>(null);

        public Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
            LyricSearchPlan plan,
            CancellationToken cancellationToken = default) =>
            SearchHandler(plan, cancellationToken);

        public Task<RawLyricPayload?> FetchAsync(
            SourceTrackCandidate candidate,
            CancellationToken cancellationToken = default) =>
            FetchHandler(candidate, cancellationToken);
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

    private sealed class RecordingCache : ILyricPipelineCache
    {
        private readonly Dictionary<string, RawLyricPayload> _raw = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ParsedLyrics> _parsed = new(StringComparer.Ordinal);
        private readonly object _syncRoot = new();

        public int RawStoreCalls { get; private set; }
        public int ParsedStoreCalls { get; private set; }
        public List<string> StoredRawCandidateIds { get; } = [];

        public bool TryGetRaw(
            LyricProviderId providerId,
            string candidateId,
            out RawLyricPayload? payload,
            out LyricAcquisitionKind acquisition)
        {
            lock (_syncRoot)
            {
                if (_raw.TryGetValue(Key(providerId, candidateId), out payload))
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
                RawStoreCalls++;
                StoredRawCandidateIds.Add(payload.CandidateId);
                _raw[Key(payload.ProviderId, payload.CandidateId)] = payload;
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
                if (_parsed.TryGetValue(Key(rawPayload, parserId, parserVersion, normalizationVersion), out parsedLyrics))
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
                ParsedStoreCalls++;
                _parsed[Key(rawPayload, parserId, parserVersion, normalizationVersion)] = parsedLyrics;
            }
        }

        private static string Key(LyricProviderId providerId, string candidateId) =>
            $"{providerId.Value}\u001f{candidateId}";

        private static string Key(
            RawLyricPayload payload,
            string parserId,
            string parserVersion,
            string normalizationVersion) =>
            $"{Key(payload.ProviderId, payload.CandidateId)}\u001f{parserId}\u001f{parserVersion}\u001f{normalizationVersion}";
    }
}
