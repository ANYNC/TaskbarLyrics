using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricPipelineContractsTests
{
    [Fact]
    public void TrackIdentityRetainsOriginalSourceAppAndSongId()
    {
        var track = new TrackInfo(
            "track-1",
            "Contract song",
            "Contract artist",
            "Contract album",
            "QQMusic",
            TimeSpan.FromMinutes(3),
            SongId: "qq-song-1");

        var identity = TrackIdentity.FromTrackInfo(track);

        Assert.Equal(track.Id, identity.TrackId);
        Assert.Equal(track.SourceApp, identity.SourceApp);
        Assert.Equal(track.SongId, identity.SongId);
        Assert.Equal(track.Title, identity.Title);
        Assert.Equal(track.Artist, identity.PrimaryArtist);
    }

    [Fact]
    public void ProviderIdsRejectEmptyValuesAndExposeStableOnlineTrustOrder()
    {
        Assert.Throws<ArgumentException>(() => new LyricProviderId("  "));

        Assert.All(
            new[]
            {
                KnownLyricProviders.Local,
                KnownLyricProviders.QQMusic,
                KnownLyricProviders.Kugou,
                KnownLyricProviders.Netease,
                KnownLyricProviders.LrcLib
            },
            provider => Assert.False(string.IsNullOrWhiteSpace(provider.Value)));

        Assert.Equal(
            ["QQMusic", "Kugou", "Netease", "LRCLIB"],
            KnownLyricProviders.OnlineTrustOrder.Select(provider => provider.Value));
    }

    [Fact]
    public void ParsedTimingRejectsInvalidValuesAndNormalizesSegmentOrder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ParsedLyricSegment(
                TimeSpan.FromMilliseconds(-1),
                TimeSpan.FromMilliseconds(100),
                "invalid"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ParsedLyricSegment(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(100),
                "invalid"));
        Assert.Throws<ArgumentException>(() =>
            new ParsedLyricSegment(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(100),
                string.Empty));

        var lateSegment = new ParsedLyricSegment(
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromMilliseconds(1700),
            "late");
        var earlySegment = new ParsedLyricSegment(
            TimeSpan.FromMilliseconds(1100),
            TimeSpan.FromMilliseconds(1300),
            "early");
        var line = new ParsedLyricLine(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            "line",
            segments: [lateSegment, earlySegment]);

        Assert.Collection(
            line.Segments,
            segment => Assert.Equal("early", segment.Text),
            segment => Assert.Equal("late", segment.Text));

        Assert.Throws<ArgumentException>(() =>
            new ParsedLyricLine(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                "line",
                segments:
                [
                    new ParsedLyricSegment(
                        TimeSpan.FromMilliseconds(900),
                        TimeSpan.FromMilliseconds(1100),
                        "outside")
                ]));
        Assert.Throws<ArgumentException>(() =>
            new ParsedLyricLine(
                TimeSpan.Zero,
                null,
                "   "));
    }

    [Fact]
    public void ParsedLyricsPreservesMixedGranularityAndProviderTimingProvenance()
    {
        var wordTimedLine = new ParsedLyricLine(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            "word line",
            segments:
            [
                new ParsedLyricSegment(
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(400),
                    "word")
            ]);
        var lineTimedLine = new ParsedLyricLine(
            TimeSpan.FromSeconds(1),
            null,
            "line timed");

        var parsed = new ParsedLyrics(
            [lineTimedLine, wordTimedLine],
            LyricTimingKind.Mixed,
            LyricTimingProvenance.ProviderSupplied,
            LyricPayloadFormat.Qrc);

        Assert.Equal(LyricTimingKind.Mixed, parsed.TimingKind);
        Assert.Equal(LyricTimingProvenance.ProviderSupplied, parsed.TimingProvenance);
        Assert.Equal(LyricPayloadFormat.Qrc, parsed.Format);
        Assert.Equal("word line", parsed.Lines[0].Text);
        Assert.Equal("line timed", parsed.Lines[1].Text);
    }

    [Fact]
    public void CandidateEvaluationRemainsSeparateFromParsedLyricsContent()
    {
        var evaluation = LyricCandidateEvaluation.Rejected(42, "version-conflict");
        var parsed = CreateParsedLyrics();

        Assert.False(evaluation.IsAdmitted);
        Assert.Equal(42, evaluation.Score);
        Assert.Equal(["version-conflict"], evaluation.RejectionReasons);
        Assert.Null(typeof(ParsedLyrics).GetProperty(nameof(LyricCandidateEvaluation.Score)));
        Assert.Null(typeof(ParsedLyrics).GetProperty(nameof(LyricCandidateEvaluation.IsAdmitted)));
        Assert.Null(typeof(ParsedLyrics).GetProperty(nameof(LyricCandidateEvaluation.RejectionReasons)));
        Assert.Single(parsed.Lines);
    }

    [Fact]
    public async Task FifthSourceCanImplementSourceAndParserSeamsWithoutLyricifyTypes()
    {
        var identity = TrackIdentity.FromTrackInfo(CreateTrack());
        var variant = new SearchQueryVariant(
            "exact",
            identity.Title,
            identity.Artists,
            identity.Album,
            identity.Duration,
            []);
        var plan = new LyricSearchPlan(identity, [variant]);
        var source = new FifthSource();

        var candidates = await source.SearchAsync(plan);
        var candidate = Assert.Single(candidates);
        var payload = await source.FetchAsync(candidate);

        Assert.NotNull(payload);
        Assert.Equal(source.ProviderId, payload!.ProviderId);
        Assert.Equal(candidate.CandidateId, payload.CandidateId);

        var decoded = new DecodedLyricPayload(
            payload.ProviderId,
            payload.CandidateId,
            payload.Format,
            payload.OriginalLyrics,
            payload.TranslationLyrics,
            payload.IsPureMusic,
            payload.Diagnostics);
        var parser = new FifthParser();
        var parsed = await parser.ParseAsync(decoded);

        Assert.Equal(LyricTimingKind.LineTimed, parsed.TimingKind);
        Assert.Equal("fifth-source", parsed.Lines[0].Text);
    }

    [Fact]
    public void CompatibilityProjectorPreservesTextSyllablesTranslationAndPureMusic()
    {
        var start = TimeSpan.FromSeconds(2);
        var segment = new ParsedLyricSegment(
            start,
            start + TimeSpan.FromMilliseconds(350),
            "syllable");
        var content = new ParsedLyrics(
            [
                new ParsedLyricLine(
                    start,
                    start + TimeSpan.FromSeconds(2),
                    "original",
                    translation: "translated",
                    segments: [segment])
            ],
            LyricTimingKind.WordTimed,
            LyricTimingProvenance.ProviderSupplied,
            LyricPayloadFormat.Krc,
            isPureMusic: true);
        var resolved = new ResolvedLyrics(
            content,
            KnownLyricProviders.Kugou,
            "kugou-candidate",
            LyricAcquisitionKind.Remote,
            new Dictionary<string, string>());

        var document = ResolvedLyricsCompatibilityProjector.ToLyricDocument(resolved);
        var line = Assert.Single(document.Lines);
        var syllable = Assert.Single(line.Syllables!);

        Assert.True(document.IsPureMusic);
        Assert.Equal("original", line.Text);
        Assert.Equal("translated", line.Translation);
        Assert.Equal(TimeSpan.Zero, syllable.RelativeOffset);
        Assert.Equal(TimeSpan.FromMilliseconds(350), syllable.Duration);
        Assert.Equal("syllable", syllable.Text);
    }

    private static ParsedLyrics CreateParsedLyrics() => new(
        [new ParsedLyricLine(TimeSpan.Zero, null, "content")],
        LyricTimingKind.LineTimed,
        LyricTimingProvenance.Unknown,
        LyricPayloadFormat.Lrc);

    private static TrackInfo CreateTrack() => new(
        "fifth-track",
        "Fifth source song",
        "Fifth source artist",
        "Fifth source album",
        "TestPlayer",
        TimeSpan.FromMinutes(3),
        "player-song-id");

    private sealed class FifthSource : ILyricSource
    {
        public LyricProviderId ProviderId { get; } = new("FifthSource");

        public Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
            LyricSearchPlan plan,
            CancellationToken cancellationToken = default)
        {
            var candidate = new SourceTrackCandidate(
                ProviderId,
                "fifth-candidate",
                plan.OriginalTrack.Title,
                plan.OriginalTrack.Artists,
                plan.OriginalTrack.Album,
                plan.OriginalTrack.Duration,
                plan.Variants[0].Id,
                new Dictionary<string, string>());
            return Task.FromResult<IReadOnlyList<SourceTrackCandidate>>([candidate]);
        }

        public Task<RawLyricPayload?> FetchAsync(
            SourceTrackCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RawLyricPayload?>(new RawLyricPayload(
                ProviderId,
                candidate.CandidateId,
                LyricPayloadFormat.PlainText,
                "fifth-source",
                null,
                false,
                false,
                new Dictionary<string, string>()));
        }
    }

    private sealed class FifthParser : ILyricPayloadParser
    {
        public bool CanParse(LyricPayloadFormat format) => format == LyricPayloadFormat.PlainText;

        public Task<ParsedLyrics> ParseAsync(
            DecodedLyricPayload payload,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ParsedLyrics(
                [new ParsedLyricLine(TimeSpan.Zero, null, payload.OriginalLyrics ?? string.Empty)],
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                payload.Format,
                payload.IsPureMusic));
        }
    }
}
