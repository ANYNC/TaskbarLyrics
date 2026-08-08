using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskbarLyrics.App;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.Diagnostics;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        DiagnosticOptions options;
        try
        {
            options = DiagnosticOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(DiagnosticOptions.Usage);
            return 2;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var report = await RunAsync(options, timeout.Token);
            var json = JsonSerializer.Serialize(report, JsonOptions);
            Console.WriteLine(json);
            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                var fullPath = Path.GetFullPath(options.OutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, json, timeout.Token);
                Console.Error.WriteLine($"Diagnostic report written to '{fullPath}'.");
            }

            return report.Track is null ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"Lyric diagnostics exceeded {options.TimeoutSeconds} seconds.");
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<LyricDiagnosticReport> RunAsync(
        DiagnosticOptions options,
        CancellationToken cancellationToken)
    {
        using var sessionProvider = new SmtcMusicSessionProvider();
        var snapshot = await sessionProvider.GetCurrentAsync(cancellationToken);
        if (snapshot.Track is null)
        {
            return new LyricDiagnosticReport(
                DateTimeOffset.UtcNow,
                null,
                null,
                [],
                [],
                null,
                null,
                "No active SMTC track was found.");
        }

        var diagnosticTrack = CreateDiagnosticTrack(snapshot.Track, options);
        if (diagnosticTrack is null)
        {
            return new LyricDiagnosticReport(
                DateTimeOffset.UtcNow,
                snapshot.Track,
                sessionProvider.GetLastTimelineDiagnostics(),
                [],
                [],
                null,
                null,
                "No searchable SMTC track was found. Play a song or supply --title and --artist.");
        }

        var sources = CreateSources(options.Provider);
        var decoders = new ILyricPayloadDecoder[] { new LyricifyPayloadDecoder() };
        var parsers = new ILyricPayloadParser[] { new LyricifyPayloadParser() };
        var trace = new CollectingTraceSink();
        var trustPolicy = new LyricProviderTrustPolicy(
            sources.Select(source => source.ProviderId),
            sources.Select(source => source.ProviderId));
        using var coordinator = new LyricResolutionCoordinator(
            sources,
            decoders,
            parsers,
            NoOpLyricPipelineCache.Instance,
            trustPolicy: trustPolicy,
            traceSink: trace);

        var resolved = await coordinator.ResolveAsync(diagnosticTrack, cancellationToken);
        await trace.WaitForSourcesAsync(sources.Length, TimeSpan.FromSeconds(2), cancellationToken);
        var forced = await FetchForcedCandidateAsync(
            options,
            sources,
            decoders,
            parsers,
            trace,
            cancellationToken);
        return trace.CreateReport(
            snapshot with { Track = diagnosticTrack },
            sessionProvider.GetLastTimelineDiagnostics(),
            resolved,
            forced);
    }

    private static TrackInfo? CreateDiagnosticTrack(TrackInfo? smtcTrack, DiagnosticOptions options)
    {
        if (options.Title is null && options.Artist is null)
        {
            return smtcTrack is null ||
                   string.IsNullOrWhiteSpace(smtcTrack.Title) ||
                   string.Equals(smtcTrack.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase)
                ? null
                : smtcTrack;
        }

        if (options.Title is null || options.Artist is null)
        {
            throw new ArgumentException("--title and --artist must be supplied together.");
        }

        var sourceApp = options.SourceApp ?? smtcTrack?.SourceApp ?? "Manual";
        return new TrackInfo(
            $"{sourceApp}|{options.Title}|{options.Artist}",
            options.Title,
            options.Artist,
            options.Album ?? smtcTrack?.Album ?? string.Empty,
            sourceApp,
            options.DurationSeconds.HasValue
                ? TimeSpan.FromSeconds(options.DurationSeconds.Value)
                : smtcTrack?.Duration ?? TimeSpan.Zero,
            options.SongId ?? smtcTrack?.SongId);
    }

    private static ILyricSource[] CreateSources(string? provider)
    {
        ILyricSource[] sources =
        [
            new QqMusicLyricSource(),
            new KugouLyricSource(),
            new NeteaseLyricSource(),
            new LrcLibLyricSource()
        ];
        if (string.IsNullOrWhiteSpace(provider))
        {
            return sources;
        }

        var selected = sources.FirstOrDefault(source =>
            string.Equals(source.ProviderId.Value, provider, StringComparison.OrdinalIgnoreCase));
        return selected is null
            ? throw new ArgumentException($"Unknown provider '{provider}'.")
            : [selected];
    }

    private static async Task<ForcedCandidateDiagnostic?> FetchForcedCandidateAsync(
        DiagnosticOptions options,
        IReadOnlyList<ILyricSource> sources,
        IReadOnlyList<ILyricPayloadDecoder> decoders,
        IReadOnlyList<ILyricPayloadParser> parsers,
        CollectingTraceSink trace,
        CancellationToken cancellationToken)
    {
        if (options.ForceProvider is null || options.ForceCandidate is null)
        {
            return null;
        }

        var source = sources.FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderId.Value, options.ForceProvider, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return ForcedCandidateDiagnostic.Failed(
                options.ForceProvider,
                options.ForceCandidate,
                "The forced provider was not included in this diagnostic run.");
        }

        var candidate = trace.FindCandidate(source.ProviderId, options.ForceCandidate);
        if (candidate is null)
        {
            return ForcedCandidateDiagnostic.Failed(
                source.ProviderId.Value,
                options.ForceCandidate,
                "The candidate was not returned by the current search.");
        }

        var raw = await source.FetchAsync(candidate, cancellationToken);
        if (raw is null)
        {
            return ForcedCandidateDiagnostic.Failed(
                source.ProviderId.Value,
                candidate.CandidateId,
                "The provider returned no lyric payload.");
        }

        var decoded = await DecodeAsync(raw, decoders, cancellationToken);
        var parser = parsers.FirstOrDefault(item => item.CanParse(decoded.Format));
        if (parser is null)
        {
            return ForcedCandidateDiagnostic.Failed(
                source.ProviderId.Value,
                candidate.CandidateId,
                $"No parser supports {decoded.Format}.");
        }

        var parsed = await parser.ParseAsync(decoded, cancellationToken);
        return new ForcedCandidateDiagnostic(
            source.ProviderId.Value,
            candidate.CandidateId,
            raw.Format,
            parsed.TimingKind,
            parsed.TimingProvenance,
            parsed.IsPureMusic,
            parsed.Lines.Count,
            parsed.Lines.Take(20).Select(line => new LyricLinePreview(
                line.StartTime,
                line.EndTime,
                line.Text,
                line.Translation)).ToArray(),
            null);
    }

    private static async Task<DecodedLyricPayload> DecodeAsync(
        RawLyricPayload raw,
        IReadOnlyList<ILyricPayloadDecoder> decoders,
        CancellationToken cancellationToken)
    {
        if (!raw.IsEncrypted)
        {
            return new DecodedLyricPayload(
                raw.ProviderId,
                raw.CandidateId,
                raw.Format,
                raw.OriginalLyrics,
                raw.TranslationLyrics,
                raw.IsPureMusic,
                raw.Diagnostics);
        }

        var decoder = decoders.FirstOrDefault(item => item.CanDecode(raw.Format))
            ?? throw new NotSupportedException($"No decoder supports encrypted {raw.Format} payloads.");
        return await decoder.DecodeAsync(raw, cancellationToken);
    }
}

internal sealed class CollectingTraceSink : ILyricResolutionTraceSink
{
    private readonly ConcurrentQueue<LyricResolutionCandidateTrace> _candidates = new();
    private readonly ConcurrentQueue<LyricResolutionSourceTrace> _sources = new();
    private LyricResolutionRequestTrace? _request;
    private LyricResolutionSelectionTrace? _selection;

    public void RequestPrepared(LyricResolutionRequestTrace request) => _request = request;

    public void CandidateEvaluated(LyricResolutionCandidateTrace candidate) => _candidates.Enqueue(candidate);

    public void SourceCompleted(LyricResolutionSourceTrace source) => _sources.Enqueue(source);

    public void SelectionCompleted(LyricResolutionSelectionTrace selection) => _selection = selection;

    public SourceTrackCandidate? FindCandidate(LyricProviderId providerId, string candidateId) =>
        _candidates
            .Select(item => item.Candidate)
            .FirstOrDefault(candidate =>
                candidate.ProviderId == providerId &&
                string.Equals(candidate.CandidateId, candidateId, StringComparison.Ordinal));

    public async Task WaitForSourcesAsync(
        int expectedCount,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_sources.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    public LyricDiagnosticReport CreateReport(
        PlaybackSnapshot snapshot,
        SmtcTimelineDiagnostics? timeline,
        ResolvedLyrics? resolved,
        ForcedCandidateDiagnostic? forced)
    {
        var request = _request;
        return new LyricDiagnosticReport(
            DateTimeOffset.UtcNow,
            snapshot.Track,
            timeline,
            request?.SearchPlan.Variants.Select(variant => new SearchVariantDiagnostic(
                variant.Id,
                variant.Title,
                variant.Artists,
                variant.Album,
                variant.Duration,
                variant.RelaxationReasons)).ToArray() ?? [],
            _sources.Select(source => new ProviderDiagnostic(
                source.ProviderId.Value,
                source.State,
                source.Detail,
                resolved?.ProviderId == source.ProviderId,
                _candidates
                    .Where(candidate => candidate.Candidate.ProviderId == source.ProviderId)
                    .Select(candidate => CandidateDiagnostic.From(candidate))
                    .ToArray())).ToArray(),
            resolved is null ? null : SelectionDiagnostic.From(resolved),
            forced,
            request is null ? "The coordinator did not prepare a searchable request." : null);
    }
}

internal sealed record DiagnosticOptions(
    string? Provider,
    string? ForceProvider,
    string? ForceCandidate,
    string? OutputPath,
    int TimeoutSeconds,
    string? Title,
    string? Artist,
    string? Album,
    string? SourceApp,
    double? DurationSeconds,
    string? SongId)
{
    public const string Usage = "Usage: dotnet run --project TaskbarLyrics.Diagnostics -- [--provider QQMusic] [--force-provider QQMusic --force-candidate ID] [--output PATH] [--timeout-seconds 30] [--title TITLE --artist ARTIST --album ALBUM --source SOURCE --duration-seconds N --song-id ID]";

    public static DiagnosticOptions Parse(IReadOnlyList<string> args)
    {
        string? provider = null;
        string? forceProvider = null;
        string? forceCandidate = null;
        string? outputPath = null;
        string? title = null;
        string? artist = null;
        string? album = null;
        string? sourceApp = null;
        string? songId = null;
        double? durationSeconds = null;
        var timeoutSeconds = 30;
        for (var index = 0; index < args.Count; index++)
        {
            var value = index + 1 < args.Count ? args[++index] : throw new ArgumentException($"Missing value for '{args[index]}'.");
            switch (args[index - 1])
            {
                case "--provider": provider = value; break;
                case "--force-provider": forceProvider = value; break;
                case "--force-candidate": forceCandidate = value; break;
                case "--output": outputPath = value; break;
                case "--title": title = value; break;
                case "--artist": artist = value; break;
                case "--album": album = value; break;
                case "--source": sourceApp = value; break;
                case "--song-id": songId = value; break;
                case "--duration-seconds" when double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var duration) && duration >= 0:
                    durationSeconds = duration;
                    break;
                case "--timeout-seconds" when int.TryParse(value, out var parsed) && parsed > 0:
                    timeoutSeconds = parsed;
                    break;
                default: throw new ArgumentException($"Unknown or invalid option '{args[index - 1]}'.");
            }
        }

        if ((forceProvider is null) != (forceCandidate is null))
        {
            throw new ArgumentException("--force-provider and --force-candidate must be supplied together.");
        }

        return new DiagnosticOptions(
            provider,
            forceProvider,
            forceCandidate,
            outputPath,
            timeoutSeconds,
            title,
            artist,
            album,
            sourceApp,
            durationSeconds,
            songId);
    }
}

internal sealed record LyricDiagnosticReport(
    DateTimeOffset CapturedAtUtc,
    TrackInfo? Track,
    SmtcTimelineDiagnostics? Timeline,
    IReadOnlyList<SearchVariantDiagnostic> SearchVariants,
    IReadOnlyList<ProviderDiagnostic> Providers,
    SelectionDiagnostic? Selection,
    ForcedCandidateDiagnostic? ForcedCandidate,
    string? Error);

internal sealed record SearchVariantDiagnostic(
    string Id,
    string Title,
    IReadOnlyList<string> Artists,
    string Album,
    TimeSpan Duration,
    IReadOnlyList<string> RelaxationReasons);

internal sealed record ProviderDiagnostic(
    string ProviderId,
    LyricSourceTerminalState State,
    string? Detail,
    bool Selected,
    IReadOnlyList<CandidateDiagnostic> Candidates);

internal sealed record CandidateDiagnostic(
    string CandidateId,
    string Title,
    IReadOnlyList<string> Artists,
    string Album,
    TimeSpan Duration,
    string QueryVariantId,
    IReadOnlyList<string> FetchMetadataKeys,
    bool IsAdmitted,
    int Score,
    IReadOnlyList<string> RejectionReasons)
{
    public static CandidateDiagnostic From(LyricResolutionCandidateTrace trace) => new(
        trace.Candidate.CandidateId,
        trace.Candidate.Title,
        trace.Candidate.Artists,
        trace.Candidate.Album,
        trace.Candidate.Duration,
        trace.Candidate.QueryVariantId,
        trace.Candidate.FetchMetadata.Keys.Order(StringComparer.Ordinal).ToArray(),
        trace.Evaluation.IsAdmitted,
        trace.Evaluation.Score,
        trace.Evaluation.RejectionReasons);
}

internal sealed record SelectionDiagnostic(
    string ProviderId,
    string CandidateId,
    LyricAcquisitionKind Acquisition,
    LyricPayloadFormat Format,
    LyricTimingKind TimingKind,
    LyricTimingProvenance TimingProvenance,
    int LineCount,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public static SelectionDiagnostic From(ResolvedLyrics lyrics) => new(
        lyrics.ProviderId.Value,
        lyrics.CandidateId,
        lyrics.Acquisition,
        lyrics.Content.Format,
        lyrics.Content.TimingKind,
        lyrics.Content.TimingProvenance,
        lyrics.Content.Lines.Count,
        lyrics.Diagnostics);
}

internal sealed record ForcedCandidateDiagnostic(
    string ProviderId,
    string CandidateId,
    LyricPayloadFormat? Format,
    LyricTimingKind? TimingKind,
    LyricTimingProvenance? TimingProvenance,
    bool IsPureMusic,
    int LineCount,
    IReadOnlyList<LyricLinePreview> Lines,
    string? Error)
{
    public static ForcedCandidateDiagnostic Failed(string providerId, string candidateId, string error) =>
        new(providerId, candidateId, null, null, null, false, 0, [], error);
}

internal sealed record LyricLinePreview(
    TimeSpan StartTime,
    TimeSpan? EndTime,
    string Text,
    string? Translation);

internal sealed class NoOpLyricPipelineCache : ILyricPipelineCache
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
