using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TaskbarLyrics.Core.Models;

public readonly record struct LyricProviderId
{
    public LyricProviderId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Provider ID cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public static class KnownLyricProviders
{
    public static readonly LyricProviderId Local = new("Local");
    public static readonly LyricProviderId QQMusic = new("QQMusic");
    public static readonly LyricProviderId Kugou = new("Kugou");
    public static readonly LyricProviderId Netease = new("Netease");
    public static readonly LyricProviderId LrcLib = new("LRCLIB");

    public static IReadOnlyList<LyricProviderId> OnlineTrustOrder { get; } =
        [QQMusic, Kugou, Netease, LrcLib];
}

public sealed record TrackIdentity(
    string TrackId,
    string Title,
    IReadOnlyList<string> Artists,
    string Album,
    TimeSpan Duration,
    string SourceApp,
    string? SongId,
    IReadOnlyList<string> VersionMarkers)
{
    public static TrackIdentity FromTrackInfo(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new TrackIdentity(
            track.Id,
            track.Title,
            string.IsNullOrWhiteSpace(track.Artist) ? [] : [track.Artist],
            track.Album,
            track.Duration < TimeSpan.Zero ? TimeSpan.Zero : track.Duration,
            track.SourceApp,
            track.SongId,
            []);
    }

    public string PrimaryArtist => Artists.Count == 0 ? string.Empty : Artists[0];
}

public sealed record SearchQueryVariant
{
    public SearchQueryVariant(
        string id,
        string title,
        IReadOnlyList<string> artists,
        string album,
        TimeSpan duration,
        IReadOnlyList<string> relaxationReasons)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Query variant ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Query title cannot be empty.", nameof(title));
        }

        Id = id;
        Title = title;
        Artists = artists;
        Album = album;
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        RelaxationReasons = relaxationReasons;
    }

    public string Id { get; }
    public string Title { get; }
    public IReadOnlyList<string> Artists { get; }
    public string Album { get; }
    public TimeSpan Duration { get; }
    public IReadOnlyList<string> RelaxationReasons { get; }
}

public sealed record LyricSearchPlan
{
    public LyricSearchPlan(
        TrackIdentity originalTrack,
        IReadOnlyList<SearchQueryVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(originalTrack);
        if (variants is null || variants.Count == 0)
        {
            throw new ArgumentException("A search plan requires at least one query variant.", nameof(variants));
        }

        OriginalTrack = originalTrack;
        Variants = variants;
    }

    public TrackIdentity OriginalTrack { get; }
    public IReadOnlyList<SearchQueryVariant> Variants { get; }
}

public sealed record SourceTrackCandidate
{
    public SourceTrackCandidate(
        LyricProviderId providerId,
        string candidateId,
        string title,
        IReadOnlyList<string> artists,
        string album,
        TimeSpan duration,
        string queryVariantId,
        IReadOnlyDictionary<string, string> fetchMetadata)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new ArgumentException("Candidate ID cannot be empty.", nameof(candidateId));
        }

        if (string.IsNullOrWhiteSpace(queryVariantId))
        {
            throw new ArgumentException("Query variant ID cannot be empty.", nameof(queryVariantId));
        }

        ProviderId = providerId;
        CandidateId = candidateId;
        Title = title;
        Artists = artists;
        Album = album;
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        QueryVariantId = queryVariantId;
        FetchMetadata = fetchMetadata;
    }

    public LyricProviderId ProviderId { get; }
    public string CandidateId { get; }
    public string Title { get; }
    public IReadOnlyList<string> Artists { get; }
    public string Album { get; }
    public TimeSpan Duration { get; }
    public string QueryVariantId { get; }
    public IReadOnlyDictionary<string, string> FetchMetadata { get; }
}

public enum LyricPayloadFormat
{
    Unknown,
    Lrc,
    Qrc,
    Krc,
    Yrc,
    Ttml,
    PlainText
}

public sealed record RawLyricPayload
{
    public RawLyricPayload(
        LyricProviderId providerId,
        string candidateId,
        LyricPayloadFormat format,
        string? originalLyrics,
        string? translationLyrics,
        bool isEncrypted,
        bool isPureMusic,
        IReadOnlyDictionary<string, string> diagnostics,
        bool hasStableIdentity = true)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new ArgumentException("Candidate ID cannot be empty.", nameof(candidateId));
        }

        ProviderId = providerId;
        CandidateId = candidateId;
        Format = format;
        OriginalLyrics = originalLyrics;
        TranslationLyrics = translationLyrics;
        IsEncrypted = isEncrypted;
        IsPureMusic = isPureMusic;
        Diagnostics = diagnostics;
        HasStableIdentity = hasStableIdentity;
    }

    public LyricProviderId ProviderId { get; }
    public string CandidateId { get; }
    public LyricPayloadFormat Format { get; }
    public string? OriginalLyrics { get; }
    public string? TranslationLyrics { get; }
    public bool IsEncrypted { get; }
    public bool IsPureMusic { get; }
    public IReadOnlyDictionary<string, string> Diagnostics { get; }
    public bool HasStableIdentity { get; }
}

public sealed record DecodedLyricPayload(
    LyricProviderId ProviderId,
    string CandidateId,
    LyricPayloadFormat Format,
    string? OriginalLyrics,
    string? TranslationLyrics,
    bool IsPureMusic,
    IReadOnlyDictionary<string, string> Diagnostics);

public enum LyricTimingKind
{
    Unsynced,
    LineTimed,
    WordTimed,
    CharacterTimed,
    Mixed
}

public enum LyricTimingProvenance
{
    Unknown,
    ProviderSupplied,
    Synthetic
}

public sealed record ParsedLyricSegment
{
    [JsonConstructor]
    public ParsedLyricSegment(TimeSpan startTime, TimeSpan endTime, string text)
    {
        if (startTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startTime), "Segment start time cannot be negative.");
        }

        if (endTime <= startTime)
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), "Segment end time must be after its start time.");
        }

        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Segment text cannot be empty.", nameof(text));
        }

        StartTime = startTime;
        EndTime = endTime;
        Text = text;
    }

    public TimeSpan StartTime { get; }
    public TimeSpan EndTime { get; }
    public string Text { get; }
}

public sealed record ParsedLyricLine
{
    [JsonConstructor]
    public ParsedLyricLine(
        TimeSpan startTime,
        TimeSpan? endTime,
        string text,
        string? translation = null,
        IReadOnlyList<ParsedLyricSegment>? segments = null,
        bool isInformationLine = false)
    {
        if (startTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startTime), "Line start time cannot be negative.");
        }

        if (endTime is { } value && value < startTime)
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), "Line end time cannot be before its start time.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Line text cannot be empty.", nameof(text));
        }

        var orderedSegments = (segments ?? [])
            .OrderBy(segment => segment.StartTime)
            .ToArray();
        if (orderedSegments.Any(segment => segment.StartTime < startTime) ||
            (endTime is { } lineEnd && orderedSegments.Any(segment => segment.EndTime > lineEnd)))
        {
            throw new ArgumentException("Segments must stay within the line timing range.", nameof(segments));
        }

        StartTime = startTime;
        EndTime = endTime;
        Text = text.Trim();
        Translation = string.IsNullOrWhiteSpace(translation) ? null : translation.Trim();
        Segments = orderedSegments;
        IsInformationLine = isInformationLine;
    }

    public TimeSpan StartTime { get; }
    public TimeSpan? EndTime { get; }
    public string Text { get; }
    public string? Translation { get; }
    public IReadOnlyList<ParsedLyricSegment> Segments { get; }
    public bool IsInformationLine { get; }
}

public sealed record ParsedLyrics
{
    [JsonConstructor]
    public ParsedLyrics(
        IReadOnlyList<ParsedLyricLine> lines,
        LyricTimingKind timingKind,
        LyricTimingProvenance timingProvenance,
        LyricPayloadFormat format,
        bool isPureMusic = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Lines = new ReadOnlyCollection<ParsedLyricLine>(
            lines.OrderBy(line => line.StartTime).ToArray());
        TimingKind = timingKind;
        TimingProvenance = timingProvenance;
        Format = format;
        IsPureMusic = isPureMusic;
    }

    public IReadOnlyList<ParsedLyricLine> Lines { get; }
    public LyricTimingKind TimingKind { get; }
    public LyricTimingProvenance TimingProvenance { get; }
    public LyricPayloadFormat Format { get; }
    public bool IsPureMusic { get; }
}

public sealed record LyricCandidateEvaluation(
    int Score,
    bool IsAdmitted,
    IReadOnlyList<string> RejectionReasons)
{
    public static LyricCandidateEvaluation Accepted(int score) => new(score, true, []);

    public static LyricCandidateEvaluation Rejected(int score, params string[] reasons) =>
        new(score, false, reasons);
}

public sealed record ResolvedLyrics
{
    public ResolvedLyrics(
        ParsedLyrics content,
        LyricProviderId providerId,
        string candidateId,
        LyricAcquisitionKind acquisition,
        IReadOnlyDictionary<string, string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new ArgumentException("Candidate ID cannot be empty.", nameof(candidateId));
        }

        Content = content;
        ProviderId = providerId;
        CandidateId = candidateId;
        Acquisition = acquisition;
        Diagnostics = diagnostics;
    }

    public ParsedLyrics Content { get; }
    public LyricProviderId ProviderId { get; }
    public string CandidateId { get; }
    public LyricAcquisitionKind Acquisition { get; }
    public IReadOnlyDictionary<string, string> Diagnostics { get; }
}
