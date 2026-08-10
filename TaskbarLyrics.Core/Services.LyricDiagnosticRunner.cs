using System.Collections.Concurrent;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricDiagnosticRunner
{
    private readonly ILyricSource[] _sources;
    private readonly ILyricPayloadDecoder[] _decoders;
    private readonly ILyricPayloadParser[] _parsers;
    private readonly ILyricMappingResolver? _mappingResolver;
    private readonly LyricProviderTrustPolicy _trustPolicy;
    private readonly TimeSpan? _sourceTimeout;
    private int _hasRun;

    public IReadOnlyList<LyricProviderId> ProviderTrustOrder => _trustPolicy.Order;

    public LyricDiagnosticRunner(
        IEnumerable<ILyricSource> sources,
        IEnumerable<ILyricPayloadDecoder> decoders,
        IEnumerable<ILyricPayloadParser> parsers,
        ILyricMappingResolver? mappingResolver = null,
        LyricProviderTrustPolicy? trustPolicy = null,
        TimeSpan? sourceTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(decoders);
        ArgumentNullException.ThrowIfNull(parsers);

        _sources = sources.ToArray();
        _decoders = decoders.ToArray();
        _parsers = parsers.ToArray();
        _mappingResolver = mappingResolver;
        _trustPolicy = trustPolicy ?? LyricProviderTrustPolicy.CreateDefault(
            _sources.Select(source => source.ProviderId));
        _sourceTimeout = sourceTimeout;
    }

    public async Task<LyricDiagnosticReport> RunAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (Interlocked.Exchange(ref _hasRun, 1) != 0)
        {
            throw new InvalidOperationException("A lyric diagnostic runner can only be used once.");
        }

        var mapping = (_mappingResolver ?? new SongSearchMapResolver()).Resolve(track);
        var trace = new CollectingTraceSink();
        using var coordinator = new LyricResolutionCoordinator(
            _sources,
            _decoders,
            _parsers,
            NoOpLyricPipelineCache.Instance,
            mappingResolver: new DiagnosticMappingResolver(mapping),
            trustPolicy: _trustPolicy,
            sourceTimeout: _sourceTimeout,
            traceSink: trace,
            completeAllSourcesForTrace: true);
        var resolved = await coordinator.ResolveAsync(track, cancellationToken);
        return trace.CreateReport(track, _trustPolicy.Order, resolved, mapping.PreferredProvider);
    }

    private sealed class CollectingTraceSink : ILyricResolutionTraceSink
    {
        private readonly ConcurrentQueue<LyricResolutionCandidateTrace> _candidates = new();
        private readonly ConcurrentQueue<LyricResolutionSourceTrace> _sources = new();
        private LyricResolutionRequestTrace? _request;

        public void RequestPrepared(LyricResolutionRequestTrace request) => _request = request;

        public void CandidateEvaluated(LyricResolutionCandidateTrace candidate) =>
            _candidates.Enqueue(candidate);

        public void SourceCompleted(LyricResolutionSourceTrace source) => _sources.Enqueue(source);

        public void SelectionCompleted(LyricResolutionSelectionTrace selection)
        {
        }

        public LyricDiagnosticReport CreateReport(
            TrackInfo originalTrack,
            IReadOnlyList<LyricProviderId> providerOrder,
            ResolvedLyrics? resolved,
            string? preferredProvider)
        {
            var request = _request;
            var sourceLookup = _sources
                .GroupBy(source => source.ProviderId)
                .ToDictionary(group => group.Key, group => group.Last());
            var providerIds = providerOrder
                .Concat(_sources.Select(source => source.ProviderId))
                .Distinct()
                .ToArray();

            return new LyricDiagnosticReport(
                DateTimeOffset.UtcNow,
                originalTrack,
                request?.EffectiveTrack,
                request?.IsPureMusic ?? false,
                preferredProvider,
                request?.SearchPlan.Variants.Select(variant => new LyricDiagnosticSearchVariant(
                    variant.Id,
                    variant.Title,
                    variant.Artists,
                    variant.Album,
                    variant.Duration.TotalSeconds,
                    variant.RelaxationReasons)).ToArray() ?? [],
                providerIds.Select(providerId => CreateProvider(providerId, sourceLookup, resolved)).ToArray(),
                resolved is null ? null : CreateSelection(resolved),
                request is null ? "The coordinator did not prepare a searchable request." : null);
        }

        private LyricDiagnosticProvider CreateProvider(
            LyricProviderId providerId,
            Dictionary<LyricProviderId, LyricResolutionSourceTrace> sourceLookup,
            ResolvedLyrics? resolved)
        {
            sourceLookup.TryGetValue(providerId, out var source);
            var candidates = _candidates
                .Where(candidate => candidate.Candidate.ProviderId == providerId)
                .Select(CreateCandidate)
                .ToArray();
            return new LyricDiagnosticProvider(
                providerId.Value,
                source?.State,
                source?.Detail,
                resolved?.ProviderId == providerId,
                candidates);
        }

        private static LyricDiagnosticCandidate CreateCandidate(LyricResolutionCandidateTrace trace) =>
            new(
                trace.Candidate.CandidateId,
                trace.Candidate.Title,
                trace.Candidate.Artists,
                trace.Candidate.Album,
                trace.Candidate.Duration.TotalSeconds,
                trace.Candidate.QueryVariantId,
                trace.Candidate.FetchMetadata.Keys.Order(StringComparer.Ordinal).ToArray(),
                trace.Evaluation.IsAdmitted,
                trace.Evaluation.IsAdmitted && trace.Evaluation.Score >= LyricMatchingPolicy.ImmediateAcceptanceScore,
                trace.Evaluation.Score,
                trace.Evaluation.RejectionReasons);

        private static LyricDiagnosticSelection CreateSelection(ResolvedLyrics resolved) =>
            new(
                resolved.ProviderId.Value,
                resolved.CandidateId,
                resolved.Acquisition,
                resolved.Content.Format,
                resolved.Content.TimingKind,
                resolved.Content.TimingProvenance,
                resolved.Content.Lines.Count,
                resolved.Diagnostics);
    }

    private sealed class DiagnosticMappingResolver(LyricMapping mapping) : ILyricMappingResolver
    {
        public LyricMapping Resolve(TrackInfo track) => mapping with { PreferredProvider = null };
    }

    private sealed class NoOpLyricPipelineCache : ILyricPipelineCache
    {
        public static NoOpLyricPipelineCache Instance { get; } = new();

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
}
