using System.Diagnostics;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricResolutionCoordinator : ILyricResolutionCoordinator
{
    private const string NormalizationVersion = "2";

    private readonly Dictionary<string, ILyricSource> _sources;
    private readonly IReadOnlyList<ILyricPayloadDecoder> _decoders;
    private readonly IReadOnlyList<ILyricPayloadParser> _parsers;
    private readonly IReadOnlyDictionary<string, SemaphoreSlim> _sourceGates;
    private readonly ILyricMappingResolver _mappingResolver;
    private readonly ILyricPipelineCache _cache;
    private readonly LyricProviderTrustPolicy _trustPolicy;
    private readonly ILyricProvider? _localProvider;
    private readonly ILyricResolutionTraceSink? _traceSink;
    private readonly bool _completeAllSourcesForTrace;
    private readonly TimeSpan _sourceTimeout;
    private readonly SemaphoreSlim _localGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private int _activeOperations;
    private int _activeSourceBatches;
    private int _isDisposed;
    private int _resourcesDisposed;

    public IReadOnlyList<LyricProviderId> ProviderTrustOrder => _trustPolicy.Order;

    public LyricResolutionCoordinator(
        IEnumerable<ILyricSource> sources,
        IEnumerable<ILyricPayloadDecoder> decoders,
        IEnumerable<ILyricPayloadParser> parsers,
        ILyricPipelineCache cache,
        ILyricMappingResolver? mappingResolver = null,
        ILyricProvider? localProvider = null,
        LyricProviderTrustPolicy? trustPolicy = null,
        TimeSpan? sourceTimeout = null,
        ILyricResolutionTraceSink? traceSink = null,
        bool completeAllSourcesForTrace = false)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(decoders);
        ArgumentNullException.ThrowIfNull(parsers);
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        var sourceArray = sources.ToArray();
        var duplicate = sourceArray
            .GroupBy(source => source.ProviderId.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate lyric source '{duplicate.Key}'.", nameof(sources));
        }

        _sources = sourceArray.ToDictionary(
            source => source.ProviderId.Value,
            StringComparer.OrdinalIgnoreCase);
        _decoders = decoders.ToArray();
        _parsers = parsers.ToArray();
        if (_parsers.Count == 0)
        {
            throw new ArgumentException("At least one lyric parser is required.", nameof(parsers));
        }

        _sourceGates = _sources.Keys.ToDictionary(
            providerId => providerId,
            _ => new SemaphoreSlim(1, 1),
            StringComparer.OrdinalIgnoreCase);
        _mappingResolver = mappingResolver ?? new SongSearchMapResolver();
        _localProvider = localProvider;
        _traceSink = traceSink;
        _completeAllSourcesForTrace = completeAllSourcesForTrace;
        _trustPolicy = trustPolicy ?? LyricProviderTrustPolicy.CreateDefault(sourceArray.Select(source => source.ProviderId));
        _sourceTimeout = sourceTimeout ?? LyricMatchingPolicy.OnlineSourceTimeout;
        if (_sourceTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceTimeout), "Source timeout must be positive.");
        }
    }

    public async Task<ResolvedLyrics?> ResolveAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!TryEnterOperation())
        {
            return null;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var requestId = Guid.NewGuid().ToString("N");
        Log.Diagnostic(
            "LYRIC_PIPELINE",
            $"Request='{requestId}' Track='{track.Id}' State='Started'.");
        try
        {
            var token = linkedCancellation.Token;
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(track.Title) ||
                string.Equals(track.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase))
            {
                LogSelection(requestId, null);
                return null;
            }

            var mapping = _mappingResolver.Resolve(track);
            var mappedTrack = track with
            {
                Title = mapping.Title,
                Artist = mapping.Artist,
                Album = string.IsNullOrWhiteSpace(mapping.Album) ? track.Album : mapping.Album
            };
            var searchPlan = LyricSearchPlanner.CreatePlan(TrackIdentity.FromTrackInfo(mappedTrack));
            Trace(sink => sink.RequestPrepared(new LyricResolutionRequestTrace(
                requestId,
                track,
                mappedTrack,
                searchPlan,
                mapping.IsPureMusic,
                mapping.PreferredProvider)));
            if (mapping.IsPureMusic)
            {
                var pureMusic = CreateMappedPureMusic(mappedTrack);
                LogSelection(requestId, pureMusic);
                return pureMusic;
            }

            if (!string.IsNullOrWhiteSpace(mapping.PreferredProvider))
            {
                var preferred = await ResolvePreferredAsync(
                    mappedTrack,
                    searchPlan,
                    mapping.PreferredProvider,
                    requestId,
                    token);
                LogSelection(requestId, preferred);
                return preferred;
            }

            var local = await ResolveLocalAsync(mappedTrack, token);
            if (local is not null)
            {
                LogSelection(requestId, local);
                return local;
            }

            var online = await ResolveOnlineAsync(mappedTrack, searchPlan, requestId, token);
            LogSelection(requestId, online);
            return online;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Log.Diagnostic("LYRIC_PIPELINE", $"Request='{requestId}' State='Disposed'.");
            return null;
        }
        catch (OperationCanceledException)
        {
            Log.Diagnostic("LYRIC_PIPELINE", $"Request='{requestId}' State='Cancelled'.");
            throw;
        }
        finally
        {
            ExitOperation();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        TryDisposeResources();
    }

    private async Task<ResolvedLyrics?> ResolvePreferredAsync(
        TrackInfo track,
        LyricSearchPlan searchPlan,
        string providerName,
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!_sources.TryGetValue(providerName, out var source))
        {
            Log.Warn($"Mapped preferred lyric provider '{providerName}' is not registered.");
            return null;
        }

        var outcome = await ResolveSourceWithTraceAsync(
            source,
            track,
            searchPlan,
            requestId,
            cancellationToken);
        return outcome.Lyrics;
    }

    private async Task<ResolvedLyrics?> ResolveLocalAsync(
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        if (_localProvider is null || !await _localGate.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            var result = await _localProvider.GetLyricsWithDiagnosticsAsync(track, cancellationToken);
            if (result.Document is null || result.Document.Lines.Count == 0)
            {
                return null;
            }

            return new ResolvedLyrics(
                LyricDocumentSemanticProjector.ToParsedLyrics(result.Document),
                KnownLyricProviders.Local,
                BuildLocalCandidateId(track),
                result.Acquisition,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["source"] = "local"
                });
        }
        finally
        {
            _localGate.Release();
        }
    }

    private async Task<ResolvedLyrics?> ResolveOnlineAsync(
        TrackInfo track,
        LyricSearchPlan searchPlan,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = _trustPolicy.Order.ToDictionary(
            providerId => providerId.Value,
            providerId => ResolveSourceWithTraceAsync(
                _sources[providerId.Value],
                track,
                searchPlan,
                requestId,
                batchCancellation.Token),
            StringComparer.OrdinalIgnoreCase);
        TrackSourceBatch(tasks.Values);

        var primaryProviderId = _trustPolicy.Order[0];

        if (!_completeAllSourcesForTrace)
        {
            var primaryOutcome = await tasks[primaryProviderId.Value];
            if (primaryOutcome.State == LyricSourceTerminalState.Succeeded &&
                primaryOutcome.Lyrics is not null &&
                TryGetIdentityScore(primaryOutcome.Lyrics, out var primaryScore) &&
                primaryScore >= LyricMatchingPolicy.ImmediateAcceptanceScore)
            {
                batchCancellation.Cancel();
                return primaryOutcome.Lyrics;
            }
        }

        var outcomes = new List<(LyricProviderId ProviderId, SourceOutcome Outcome)>();
        foreach (var providerId in _trustPolicy.Order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = tasks[providerId.Value];
            if (!outcome.IsCompleted)
            {
                await outcome;
            }
            if (outcome.Result is { State: LyricSourceTerminalState.Succeeded, Lyrics: not null } result)
            {
                outcomes.Add((providerId, outcome.Result));
            }
        }

        if (outcomes.Count == 0)
        {
            return null;
        }

        return SelectBestOutcome(outcomes);
    }

    private ResolvedLyrics SelectBestOutcome(
        IReadOnlyList<(LyricProviderId ProviderId, SourceOutcome Outcome)> outcomes)
    {
        var trustOrder = _trustPolicy.Order.ToArray();

        var highConfidence = outcomes
            .Where(entry => TryGetIdentityScore(entry.Outcome.Lyrics!, out var score) &&
                            score >= LyricMatchingPolicy.ImmediateAcceptanceScore)
            .ToArray();

        var pool = highConfidence.Length > 0 ? highConfidence : outcomes;

        return pool
            .OrderBy(entry => Array.IndexOf(trustOrder, entry.ProviderId))
            .ThenByDescending(entry => TryGetIdentityScore(entry.Outcome.Lyrics!, out var score) ? score : 0)
            .First()
            .Outcome.Lyrics!;
    }

    private static bool TryGetIdentityScore(ResolvedLyrics lyrics, out int score)
    {
        score = 0;
        return lyrics.Diagnostics.TryGetValue("identityScore", out var value) &&
               int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out score);
    }

    private async Task<SourceOutcome> ResolveSourceWithTraceAsync(
        ILyricSource source,
        TrackInfo track,
        LyricSearchPlan searchPlan,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await ResolveSourceAsync(source, track, searchPlan, requestId, cancellationToken);
            LogOutcome(requestId, outcome);
            return outcome;
        }
        catch (OperationCanceledException)
        {
            LogOutcome(requestId, new SourceOutcome(
                source.ProviderId,
                LyricSourceTerminalState.Canceled,
                null,
                "request-canceled"));
            throw;
        }
    }

    private async Task<SourceOutcome> ResolveSourceAsync(
        ILyricSource source,
        TrackInfo track,
        LyricSearchPlan searchPlan,
        string requestId,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_sourceTimeout);
        var token = timeout.Token;
        var gate = _sourceGates[source.ProviderId.Value];
        try
        {
            if (!await gate.WaitAsync(0, token))
            {
                return new SourceOutcome(source.ProviderId, LyricSourceTerminalState.Disabled, null, "provider-busy");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return new SourceOutcome(source.ProviderId, LyricSourceTerminalState.TimedOut, null, "gate-timeout");
        }

        try
        {
            var identity = searchPlan.OriginalTrack;
            var candidates = await source.SearchAsync(searchPlan, token);
            if (candidates.Count == 0)
            {
                return new SourceOutcome(source.ProviderId, LyricSourceTerminalState.NoLyrics, null, "no-candidates");
            }

            var evaluated = candidates
                .Select(candidate => new CandidateAdmission(candidate, LyricIdentityEvaluator.Evaluate(identity, candidate)))
                .ToArray();
            foreach (var admission in evaluated)
            {
                LogCandidateEvaluation(requestId, admission);
            }

            var admitted = evaluated
                .Where(admission => admission.Evaluation.IsAdmitted)
                .OrderByDescending(admission => admission.Evaluation.Score)
                .ToArray();
            if (admitted.Length == 0)
            {
                return new SourceOutcome(source.ProviderId, LyricSourceTerminalState.IdentityRejected, null, "all-candidates-rejected");
            }

            var sawInvalidContent = false;
            foreach (var admission in admitted)
            {
                token.ThrowIfCancellationRequested();
                var resolved = await ResolveCandidateAsync(source, admission, requestId, token);
                if (resolved.Lyrics is not null)
                {
                    return new SourceOutcome(source.ProviderId, LyricSourceTerminalState.Succeeded, resolved.Lyrics, null);
                }

                sawInvalidContent |= resolved.InvalidContent;
            }

            return new SourceOutcome(
                source.ProviderId,
                sawInvalidContent ? LyricSourceTerminalState.InvalidContent : LyricSourceTerminalState.NoLyrics,
                null,
                sawInvalidContent ? "candidate-content-invalid" : "candidate-has-no-lyrics");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return new SourceOutcome(source.ProviderId, LyricSourceTerminalState.TimedOut, null, "source-timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn($"Lyric request '{requestId}' source '{source.ProviderId}' failed: {exception.Message}");
            return new SourceOutcome(source.ProviderId, LyricSourceTerminalState.Failed, null, exception.GetType().Name);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CandidateResolution> ResolveCandidateAsync(
        ILyricSource source,
        CandidateAdmission admission,
        string requestId,
        CancellationToken cancellationToken)
    {
        var candidate = admission.Candidate;
        var stopwatch = Stopwatch.StartNew();
        RawLyricPayload? rawPayload;
        LyricAcquisitionKind acquisition;
        if (!_cache.TryGetRaw(source.ProviderId, candidate.CandidateId, out rawPayload, out acquisition))
        {
            rawPayload = await source.FetchAsync(candidate, cancellationToken);
            if (rawPayload is null)
            {
                return CandidateResolution.NoLyrics;
            }

            acquisition = LyricAcquisitionKind.Remote;
            _cache.StoreRaw(rawPayload, DateTimeOffset.UtcNow);
        }

        if (rawPayload is null ||
            rawPayload.ProviderId != source.ProviderId ||
            !string.Equals(rawPayload.CandidateId, candidate.CandidateId, StringComparison.Ordinal))
        {
            return CandidateResolution.Invalid;
        }

        var parser = _parsers.FirstOrDefault(candidateParser => candidateParser.CanParse(rawPayload.Format));
        if (parser is null)
        {
            return CandidateResolution.Invalid;
        }

        var parserId = parser.GetType().FullName ?? parser.GetType().Name;
        var parserVersion = parser.GetType().Assembly.GetName().Version?.ToString() ?? "0";
        if (!_cache.TryGetParsed(
                rawPayload,
                parserId,
                parserVersion,
                NormalizationVersion,
                out var parsed,
                out var parsedAcquisition))
        {
            try
            {
                var decoded = await DecodeAsync(rawPayload, cancellationToken);
                parsed = await parser.ParseAsync(decoded, cancellationToken);
            }
            catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
            {
                Log.Warn($"Lyric payload rejected. Request='{requestId}' Provider='{source.ProviderId}' Candidate='{candidate.CandidateId}' Format='{rawPayload.Format}' Error='{exception.Message}'");
                return CandidateResolution.Invalid;
            }

            if (parsed.Lines.Count == 0 && !parsed.IsPureMusic)
            {
                return CandidateResolution.Invalid;
            }

            _cache.StoreParsed(rawPayload, parsed, parserId, parserVersion, NormalizationVersion);
        }
        else
        {
            acquisition = parsedAcquisition;
        }

        var diagnostics = new Dictionary<string, string>(rawPayload.Diagnostics, StringComparer.Ordinal)
        {
            ["requestId"] = requestId,
            ["queryVariant"] = candidate.QueryVariantId,
            ["identityScore"] = admission.Evaluation.Score.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["format"] = rawPayload.Format.ToString(),
            ["elapsedMs"] = stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return new CandidateResolution(
            new ResolvedLyrics(
                parsed!,
                source.ProviderId,
                candidate.CandidateId,
                acquisition,
                diagnostics),
            false);
    }

    private async Task<DecodedLyricPayload> DecodeAsync(
        RawLyricPayload payload,
        CancellationToken cancellationToken)
    {
        if (!payload.IsEncrypted)
        {
            return new DecodedLyricPayload(
                payload.ProviderId,
                payload.CandidateId,
                payload.Format,
                payload.OriginalLyrics,
                payload.TranslationLyrics,
                payload.IsPureMusic,
                payload.Diagnostics);
        }

        var decoder = _decoders.FirstOrDefault(candidate => candidate.CanDecode(payload.Format));
        if (decoder is null)
        {
            throw new NotSupportedException($"No decoder is registered for encrypted {payload.Format} payloads.");
        }

        return await decoder.DecodeAsync(payload, cancellationToken);
    }

    private static ResolvedLyrics CreateMappedPureMusic(TrackInfo track)
    {
        var content = new ParsedLyrics(
            [new ParsedLyricLine(TimeSpan.Zero, null, "🎶🎶🎶")],
            LyricTimingKind.Unsynced,
            LyricTimingProvenance.Unknown,
            LyricPayloadFormat.PlainText,
            isPureMusic: true);
        return new ResolvedLyrics(
            content,
            KnownLyricProviders.Local,
            $"mapping:{track.Id}",
            LyricAcquisitionKind.SongMapping,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "mapping"
            });
    }

    private static string BuildLocalCandidateId(TrackInfo track) =>
        $"local:{(string.IsNullOrWhiteSpace(track.SongId) ? track.Id : track.SongId)}";

    private void LogCandidateEvaluation(string requestId, CandidateAdmission admission)
    {
        Log.Diagnostic(
            "LYRIC_PIPELINE",
            $"Request='{requestId}' Provider='{admission.Candidate.ProviderId}' Candidate='{admission.Candidate.CandidateId}' QueryVariant='{admission.Candidate.QueryVariantId}' IdentityAdmitted='{admission.Evaluation.IsAdmitted}' IdentityScore='{admission.Evaluation.Score}'.");
        Trace(sink => sink.CandidateEvaluated(new LyricResolutionCandidateTrace(
            requestId,
            admission.Candidate,
            admission.Evaluation)));
    }

    private void LogOutcome(string requestId, SourceOutcome outcome)
    {
        Log.Diagnostic(
            "LYRIC_PIPELINE",
            $"Request='{requestId}' Provider='{outcome.ProviderId}' TerminalState='{outcome.State}' Detail='{outcome.Detail ?? "none"}' Selected='{outcome.Lyrics is not null}'.");
        Trace(sink => sink.SourceCompleted(new LyricResolutionSourceTrace(
            requestId,
            outcome.ProviderId,
            outcome.State,
            outcome.Detail)));
    }

    private void LogSelection(string requestId, ResolvedLyrics? lyrics)
    {
        Trace(sink => sink.SelectionCompleted(new LyricResolutionSelectionTrace(requestId, lyrics)));
        if (lyrics is null)
        {
            Log.Diagnostic("LYRIC_PIPELINE", $"Request='{requestId}' Selection='None'.");
            return;
        }

        lyrics.Diagnostics.TryGetValue("queryVariant", out var queryVariant);
        lyrics.Diagnostics.TryGetValue("identityScore", out var identityScore);
        Log.Diagnostic(
            "LYRIC_PIPELINE",
            $"Request='{requestId}' Selection='Accepted' Provider='{lyrics.ProviderId}' Candidate='{lyrics.CandidateId}' QueryVariant='{queryVariant ?? "none"}' IdentityScore='{identityScore ?? "none"}' Format='{lyrics.Content.Format}' TimingProvenance='{lyrics.Content.TimingProvenance}' CacheAcquisition='{lyrics.Acquisition}'.");
    }

    private void Trace(Action<ILyricResolutionTraceSink> write)
    {
        if (_traceSink is null)
        {
            return;
        }

        try
        {
            write(_traceSink);
        }
        catch (Exception exception)
        {
            Log.Warn($"Lyric resolution trace sink failed: {exception.Message}");
        }
    }

    private void TrackSourceBatch(IEnumerable<Task<SourceOutcome>> tasks)
    {
        Interlocked.Increment(ref _activeSourceBatches);
        _ = ObserveSourceBatchAsync(tasks.ToArray());
    }

    private async Task ObserveSourceBatchAsync(IReadOnlyCollection<Task<SourceOutcome>> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Selection and track replacement cancel lower-priority work by design.
        }
        catch (Exception exception)
        {
            Log.Error($"Late lyric source work failed: {exception}");
        }
        finally
        {
            Interlocked.Decrement(ref _activeSourceBatches);
            TryDisposeResources();
        }
    }

    private bool TryEnterOperation()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return false;
        }

        Interlocked.Increment(ref _activeOperations);
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            return true;
        }

        ExitOperation();
        return false;
    }

    private void ExitOperation()
    {
        Interlocked.Decrement(ref _activeOperations);
        TryDisposeResources();
    }

    private void TryDisposeResources()
    {
        if (Volatile.Read(ref _isDisposed) == 0 ||
            Volatile.Read(ref _activeOperations) != 0 ||
            Volatile.Read(ref _activeSourceBatches) != 0 ||
            Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        if (_localProvider is IDisposable localDisposable)
        {
            localDisposable.Dispose();
        }

        foreach (var disposable in _sources.Values.OfType<IDisposable>())
        {
            disposable.Dispose();
        }

        foreach (var gate in _sourceGates.Values)
        {
            gate.Dispose();
        }

        _localGate.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private sealed record CandidateAdmission(
        SourceTrackCandidate Candidate,
        LyricCandidateEvaluation Evaluation);

    private sealed record SourceOutcome(
        LyricProviderId ProviderId,
        LyricSourceTerminalState State,
        ResolvedLyrics? Lyrics,
        string? Detail);

    private sealed record CandidateResolution(ResolvedLyrics? Lyrics, bool InvalidContent)
    {
        public static CandidateResolution NoLyrics { get; } = new(null, false);
        public static CandidateResolution Invalid { get; } = new(null, true);
    }
}
