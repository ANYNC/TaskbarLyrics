using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricSearchPlannerTests
{
    [Fact]
    public void CreatePlanPreservesOriginalIdentityAndProducesFeatureArtistAndRelaxedVariants()
    {
        var track = new TrackInfo(
            "planner-track",
            "Signal (Deluxe) feat. Guest",
            "Beyoncé feat. Zoë, Another Artist",
            "Planner album",
            "Spotify",
            TimeSpan.FromSeconds(210),
            "player-song-id");
        var identity = TrackIdentity.FromTrackInfo(track);

        var plan = LyricSearchPlanner.CreatePlan(identity);

        Assert.Same(identity, plan.OriginalTrack);
        Assert.Equal(track.SourceApp, plan.OriginalTrack.SourceApp);
        Assert.Equal(track.SongId, plan.OriginalTrack.SongId);
        Assert.Contains(plan.Variants, variant => variant.Id == "exact");
        Assert.Contains(plan.Variants, variant => variant.Id == "normalized");
        Assert.Contains(plan.Variants, variant => variant.Id == "primary-artist");
        Assert.Contains(plan.Variants, variant => variant.Id == "relaxed-title");

        var primaryArtist = Assert.Single(plan.Variants, variant => variant.Id == "primary-artist");
        Assert.Equal("Beyoncé", primaryArtist.Artists[0]);
        var relaxed = Assert.Single(plan.Variants, variant => variant.Id == "relaxed-title");
        Assert.DoesNotContain("Deluxe", relaxed.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("feat", relaxed.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("removed-feature-or-bracket-suffix", relaxed.RelaxationReasons);
    }

    [Fact]
    public void CreatePlanNormalizesPunctuationDiacriticsAndScriptWithoutReplacingOriginalTrack()
    {
        var track = new TrackInfo(
            "planner-normalization",
            "Café, déjà vu!",
            "Beyoncé & Zoë",
            "繁體專輯",
            "Spotify",
            TimeSpan.FromSeconds(180));
        var identity = TrackIdentity.FromTrackInfo(track);

        var plan = LyricSearchPlanner.CreatePlan(identity);

        var normalized = Assert.Single(plan.Variants, variant => variant.Id == "normalized");
        Assert.Equal("cafe deja vu", normalized.Title);
        Assert.Equal(["beyonce", "zoe"], normalized.Artists);
        Assert.Equal(track.Title, plan.OriginalTrack.Title);
        Assert.Equal(track.Artist, plan.OriginalTrack.PrimaryArtist);
    }

    [Theory]
    [InlineData("Anti-Hero - ILLENIUM Remix", "Anti-Hero")]
    [InlineData("Versioned song – Live", "Versioned song")]
    [InlineData("Versioned song — Acoustic Version", "Versioned song")]
    public void CreatePlanProducesDelimitedVersionRelaxedVariant(string title, string expectedTitle)
    {
        var identity = CreateIdentity(title);

        var plan = LyricSearchPlanner.CreatePlan(identity);

        var exact = Assert.Single(plan.Variants, variant => variant.Id == "exact");
        var relaxed = Assert.Single(plan.Variants, variant => variant.Id == "version-relaxed-title");
        Assert.Equal(expectedTitle, relaxed.Title);
        Assert.Equal(exact.Artists, relaxed.Artists);
        Assert.Contains("removed-delimited-version-suffix", relaxed.RelaxationReasons);
        Assert.Equal(title, plan.OriginalTrack.Title);
    }

    [Theory]
    [InlineData("Anti-Hero")]
    [InlineData("Versioned song - Chapter Two")]
    public void CreatePlanDoesNotRelaxOrdinaryHyphenatedTitle(string title)
    {
        var plan = LyricSearchPlanner.CreatePlan(CreateIdentity(title));

        Assert.DoesNotContain(plan.Variants, variant => variant.Id == "version-relaxed-title");
    }

    [Fact]
    public void DelimitedVersionRelaxedQueryStillUsesOriginalVersionIdentityForAdmission()
    {
        var identity = CreateIdentity("Anti-Hero - ILLENIUM Remix");
        var plan = LyricSearchPlanner.CreatePlan(identity);
        var relaxed = Assert.Single(plan.Variants, variant => variant.Id == "version-relaxed-title");
        var candidate = CreateIdentityCandidate(identity, relaxed.Title);

        var evaluation = LyricIdentityEvaluator.Evaluate(identity, candidate);

        Assert.False(evaluation.IsAdmitted);
        Assert.Equal(0, evaluation.Score);
        Assert.Contains("identity-conflict", evaluation.RejectionReasons);
    }

    [Theory]
    [InlineData("Live")]
    [InlineData("Remix")]
    [InlineData("Acoustic")]
    [InlineData("Instrumental")]
    [InlineData("Karaoke")]
    public void RelaxedQueryStillUsesOriginalVersionIdentityForAdmission(string versionMarker)
    {
        var track = new TrackInfo(
            "version-track",
            $"Versioned song ({versionMarker})",
            "Version artist",
            "Version album",
            "Spotify",
            TimeSpan.FromSeconds(200));
        var identity = TrackIdentity.FromTrackInfo(track);
        var plan = LyricSearchPlanner.CreatePlan(identity);
        var relaxed = Assert.Single(plan.Variants, variant => variant.Id == "relaxed-title");
        var candidate = new SourceTrackCandidate(
            KnownLyricProviders.QQMusic,
            $"candidate-{versionMarker}",
            relaxed.Title,
            ["Version artist"],
            track.Album,
            track.Duration,
            relaxed.Id,
            new Dictionary<string, string>());

        var evaluation = LyricIdentityEvaluator.Evaluate(identity, candidate);

        Assert.False(evaluation.IsAdmitted);
        Assert.Equal(0, evaluation.Score);
        Assert.Contains("identity-conflict", evaluation.RejectionReasons);
    }

    [Theory]
    [InlineData("Run Away With Me", "Run Away With Me (Simlish Version)")]
    [InlineData("Run Away With Me (Simlish Version)", "Run Away With Me")]
    public void IdentityEvaluationRejectsMismatchedSimlishVersions(
        string originalTitle,
        string candidateTitle)
    {
        var identity = CreateIdentity(originalTitle);
        var evaluation = LyricIdentityEvaluator.Evaluate(
            identity,
            CreateIdentityCandidate(identity, candidateTitle));

        Assert.False(evaluation.IsAdmitted);
        Assert.Equal(0, evaluation.Score);
        Assert.Contains("identity-conflict", evaluation.RejectionReasons);
    }

    [Fact]
    public void IdentityEvaluationAllowsCandidatesWhenBothTitlesAreSimlish()
    {
        var identity = CreateIdentity("Run Away With Me (Simlish Version)");

        var evaluation = LyricIdentityEvaluator.Evaluate(
            identity,
            CreateIdentityCandidate(identity, "Run Away With Me (Simlish Version)"));

        Assert.True(evaluation.IsAdmitted);
        Assert.InRange(evaluation.Score, 95, 100);
        Assert.Empty(evaluation.RejectionReasons);
    }

    [Theory]
    [InlineData("Run Away With Me", "Run Away With Me (Deluxe Edition)")]
    [InlineData("Run Away With Me (Deluxe Edition)", "Run Away With Me")]
    [InlineData("Run Away With Me", "Run Away With Me (Remastered 2024)")]
    public void IdentityEvaluationDoesNotRejectOrdinaryBracketedMetadata(
        string originalTitle,
        string candidateTitle)
    {
        var identity = CreateIdentity(originalTitle);
        var evaluation = LyricIdentityEvaluator.Evaluate(
            identity,
            CreateIdentityCandidate(identity, candidateTitle));

        Assert.True(evaluation.IsAdmitted);
        Assert.InRange(evaluation.Score, 95, 100);
        Assert.Empty(evaluation.RejectionReasons);
    }

    [Fact]
    public void IdentityScoreOnlyControlsAdmissionAndDoesNotExposeCrossSourceRanking()
    {
        var identity = TrackIdentity.FromTrackInfo(new TrackInfo(
            "score-track",
            "Score song",
            "Score artist",
            "Score album",
            "Spotify",
            TimeSpan.FromSeconds(200)));
        var exactCandidate = CreateCandidate(KnownLyricProviders.QQMusic, "Score song", "score-exact");
        var relaxedCandidate = CreateCandidate(KnownLyricProviders.Kugou, "Score song extended", "score-relaxed");

        var exactEvaluation = LyricIdentityEvaluator.Evaluate(identity, exactCandidate);
        var relaxedEvaluation = LyricIdentityEvaluator.Evaluate(identity, relaxedCandidate);

        Assert.True(exactEvaluation.IsAdmitted);
        Assert.True(relaxedEvaluation.IsAdmitted);
        Assert.NotEqual(exactEvaluation.Score, relaxedEvaluation.Score);
        Assert.Null(typeof(LyricCandidateEvaluation).GetProperty(nameof(SourceTrackCandidate.ProviderId)));
    }

    [Fact]
    public void IdentityEvaluationPrefersMatchingAlbumWithoutRejectingAnotherAlbum()
    {
        var identity = TrackIdentity.FromTrackInfo(new TrackInfo(
            "album-track",
            "Album song",
            "Album artist",
            "Studio album",
            "Spotify",
            TimeSpan.FromSeconds(200)));
        var matchingAlbum = new SourceTrackCandidate(
            KnownLyricProviders.QQMusic,
            "matching-album",
            identity.Title,
            identity.Artists,
            identity.Album,
            identity.Duration,
            "exact",
            new Dictionary<string, string>());
        var differentAlbum = new SourceTrackCandidate(
            KnownLyricProviders.QQMusic,
            "different-album",
            identity.Title,
            identity.Artists,
            "Compilation",
            identity.Duration,
            "exact",
            new Dictionary<string, string>());

        var matchingEvaluation = LyricIdentityEvaluator.Evaluate(identity, matchingAlbum);
        var differentEvaluation = LyricIdentityEvaluator.Evaluate(identity, differentAlbum);

        Assert.True(matchingEvaluation.IsAdmitted);
        Assert.True(differentEvaluation.IsAdmitted);
        Assert.True(matchingEvaluation.Score > differentEvaluation.Score);
    }

    [Theory]
    [InlineData("QQMusic", "QQMusic", true)]
    [InlineData("QQMusic", "Netease", false)]
    [InlineData("Netease", "Netease", true)]
    [InlineData("Kugou", "Kugou", true)]
    [InlineData("Spotify", "QQMusic", false)]
    public void SongIdDirectLookupRequiresOwningSourceProvider(
        string sourceApp,
        string provider,
        bool expected)
    {
        var identity = TrackIdentity.FromTrackInfo(new TrackInfo(
            "song-id-track",
            "Song ID track",
            "Song ID artist",
            "Song ID album",
            sourceApp,
            TimeSpan.FromSeconds(180),
            "provider-song-id"));

        Assert.Equal(
            expected,
            ProviderSongIdPolicy.CanUseDirectSongId(identity, new LyricProviderId(provider)));
    }

    private static SourceTrackCandidate CreateCandidate(
        LyricProviderId provider,
        string title,
        string candidateId) => new(
        provider,
        candidateId,
        title,
        ["Score artist"],
        "Score album",
        TimeSpan.FromSeconds(200),
        "exact",
        new Dictionary<string, string>());

    private static TrackIdentity CreateIdentity(string title) =>
        TrackIdentity.FromTrackInfo(new TrackInfo(
            "identity-track",
            title,
            "Run Away With Me artist",
            "Identity album",
            "Spotify",
            TimeSpan.FromSeconds(210)));

    private static SourceTrackCandidate CreateIdentityCandidate(
        TrackIdentity identity,
        string title) => new(
        KnownLyricProviders.QQMusic,
        "identity-candidate",
        title,
        identity.Artists,
        identity.Album,
        identity.Duration,
        "exact",
        new Dictionary<string, string>());
}
