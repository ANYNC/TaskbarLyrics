using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricifySourceAdaptersTests
{
    [Fact]
    public void KugouMappingKeepsLyricCandidateAndKrcFormat()
    {
        var song = new Lyricify.Lyrics.Providers.Web.Kugou.SearchSongResponse.DataItem.InfoItem
        {
            Hash = "song-hash",
            SongName = "Song",
            SingerName = "Artist",
            AlbumName = "Album"
        };
        var lyric = new Lyricify.Lyrics.Providers.Web.Kugou.SearchLyricsResponse.Candidate
        {
            Id = "lyric-id",
            AccessKey = "access-key",
            Song = "Song",
            Singer = "Artist",
            Duration = 180000
        };

        var candidate = KugouLyricSource.MapCandidate(song, lyric, CreateVariant());
        Assert.NotNull(candidate);
        Assert.Equal(KnownLyricProviders.Kugou, candidate.ProviderId);
        Assert.Equal("lyric-id", candidate.CandidateId);
        Assert.Equal("Song", candidate.Title);
        Assert.Equal(TimeSpan.FromMinutes(3), candidate.Duration);
        Assert.Equal("access-key", candidate.FetchMetadata["access-key"]);
        Assert.Equal("song-hash", candidate.FetchMetadata["song-hash"]);

        var payload = KugouLyricSource.MapKrc(candidate, "[0,1000]<0,1000,0>word");
        Assert.Equal(LyricPayloadFormat.Krc, payload!.Format);
        Assert.Equal(KnownLyricProviders.Kugou, payload.ProviderId);
        Assert.Equal("lyric-id", payload.CandidateId);
        Assert.Equal("[0,1000]<0,1000,0>word", payload.OriginalLyrics);
        Assert.Null(KugouLyricSource.MapCandidate(
            song,
            new Lyricify.Lyrics.Providers.Web.Kugou.SearchLyricsResponse.Candidate(),
            CreateVariant()));
    }

    [Fact]
    public void NeteaseMappingPrefersYrcAndFallsBackToLrc()
    {
        var candidate = NeteaseLyricSource.MapSong(
            new Lyricify.Lyrics.Providers.Web.Netease.Song
            {
                Id = "netease-id",
                Name = "Song",
                Duration = 180000,
                Album = new Lyricify.Lyrics.Providers.Web.Netease.Al { Name = "Album" },
                Artists =
                [
                    new Lyricify.Lyrics.Providers.Web.Netease.Ar { Id = 1, Name = "Artist" }
                ]
            },
            CreateVariant());
        Assert.NotNull(candidate);
        Assert.Equal(KnownLyricProviders.Netease, candidate.ProviderId);
        Assert.Equal("netease-id", candidate.CandidateId);
        Assert.Equal("Song", candidate.Title);
        Assert.Equal(TimeSpan.FromMinutes(3), candidate.Duration);
        Assert.Empty(candidate.FetchMetadata);

        var yrc = NeteaseLyricSource.MapLyrics(
            candidate,
            new Lyricify.Lyrics.Providers.Web.Netease.LyricResult
            {
                Yrc = new Lyricify.Lyrics.Providers.Web.Netease.Lyrics { Lyric = "yrc" },
                Ytlrc = new Lyricify.Lyrics.Providers.Web.Netease.Lyrics { Lyric = "yrc-translation" },
                Lrc = new Lyricify.Lyrics.Providers.Web.Netease.Lyrics { Lyric = "lrc" }
            });
        var lrc = NeteaseLyricSource.MapLyrics(
            candidate,
            new Lyricify.Lyrics.Providers.Web.Netease.LyricResult
            {
                Lrc = new Lyricify.Lyrics.Providers.Web.Netease.Lyrics { Lyric = "lrc" },
                Tlyric = new Lyricify.Lyrics.Providers.Web.Netease.Lyrics { Lyric = "lrc-translation" }
            });

        Assert.Equal(LyricPayloadFormat.Yrc, yrc!.Format);
        Assert.Equal(KnownLyricProviders.Netease, yrc.ProviderId);
        Assert.Equal("netease-id", yrc.CandidateId);
        Assert.Equal("yrc", yrc.OriginalLyrics);
        Assert.Equal("yrc-translation", yrc.TranslationLyrics);
        Assert.Equal(LyricPayloadFormat.Lrc, lrc!.Format);
        Assert.Equal(KnownLyricProviders.Netease, lrc.ProviderId);
        Assert.Equal("netease-id", lrc.CandidateId);
        Assert.Equal("lrc", lrc.OriginalLyrics);
        Assert.Equal("lrc-translation", lrc.TranslationLyrics);
        Assert.Null(NeteaseLyricSource.MapLyrics(
            candidate,
            new Lyricify.Lyrics.Providers.Web.Netease.LyricResult { Nolyric = true }));
    }

    [Fact]
    public void LrcLibMappingUsesStructuredIdentityAndSupportsAllPayloadKinds()
    {
        var candidate = LrcLibLyricSource.MapSearchResult(
            new Lyricify.Lyrics.Providers.Web.LRCLIB.SearchResultItem
            {
                Id = 42,
                TrackName = "Song",
                ArtistName = "Artist",
                AlbumName = "Album",
                Duration = 180
            },
            CreateVariant());
        Assert.NotNull(candidate);
        Assert.Equal(KnownLyricProviders.LrcLib, candidate.ProviderId);
        Assert.Equal("42", candidate.CandidateId);
        Assert.Equal("Song", candidate.Title);
        Assert.Equal(TimeSpan.FromMinutes(3), candidate.Duration);
        Assert.Empty(candidate.FetchMetadata);

        var synced = LrcLibLyricSource.MapLyrics(
            candidate,
            new Lyricify.Lyrics.Providers.Web.LRCLIB.GetLyricResult
            {
                SyncedLyrics = "[00:01.00]line",
                PlainLyrics = "line"
            });
        var plain = LrcLibLyricSource.MapLyrics(
            candidate,
            new Lyricify.Lyrics.Providers.Web.LRCLIB.GetLyricResult { PlainLyrics = "plain" });
        var instrumental = LrcLibLyricSource.MapLyrics(
            candidate,
            new Lyricify.Lyrics.Providers.Web.LRCLIB.GetLyricResult { Instrumental = true });

        Assert.Equal(LyricPayloadFormat.Lrc, synced!.Format);
        Assert.Equal(KnownLyricProviders.LrcLib, synced.ProviderId);
        Assert.Equal("42", synced.CandidateId);
        Assert.Equal("[00:01.00]line", synced.OriginalLyrics);
        Assert.Equal(LyricPayloadFormat.PlainText, plain!.Format);
        Assert.Equal("plain", plain!.OriginalLyrics);
        Assert.True(instrumental!.IsPureMusic);
        Assert.Null(LrcLibLyricSource.MapSearchResult(
            new Lyricify.Lyrics.Providers.Web.LRCLIB.SearchResultItem(),
            CreateVariant()));
    }

    [Theory]
    [InlineData("QQMusic", "295618479")]
    [InlineData("Netease", "provider-song-id")]
    public async Task MatchingProviderSongIdCreatesDirectCandidateWithoutNetwork(string sourceApp, string songId)
    {
        var track = new TrackIdentity(
            "track",
            "Song",
            ["Artist"],
            "Album",
            TimeSpan.FromMinutes(3),
            sourceApp,
            songId,
            []);
        var plan = LyricSearchPlanner.CreatePlan(track);
        var candidates = sourceApp == "QQMusic"
            ? await new QqMusicLyricSource().SearchAsync(plan)
            : await new NeteaseLyricSource().SearchAsync(plan);

        var candidate = Assert.Single(candidates);
        Assert.Equal(songId, candidate.CandidateId);
        Assert.Equal("direct-song-id", candidate.QueryVariantId);
    }

    private static SearchQueryVariant CreateVariant() =>
        new("exact", "Song", ["Artist"], "Album", TimeSpan.FromMinutes(3), []);
}
