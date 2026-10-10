using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class QqMusicLyricSourceTests
{
    [Fact]
    public void SearchResponseMapsNumericSongIdAndTrackMetadata()
    {
        var songs = QqMusicResponseMapper.MapSearchResponse(ReadFixture("qq-search-response.json"));

        Assert.Equal(2, songs.Count);
        var first = songs[0];
        Assert.Equal("295618479", first.SongId);
        Assert.Equal("003WkhSf2FOUwq", first.Mid);
        Assert.Equal("嘘月", first.Title);
        Assert.Equal(["ヨルシカ"], first.Artists);
        Assert.Equal("創作", first.Album);
        Assert.Equal(TimeSpan.FromSeconds(290), first.Duration);

        Assert.Equal(string.Empty, songs[1].Album);

        var candidate = QqMusicLyricSource.MapSong(first, CreateVariant());
        Assert.Equal(KnownLyricProviders.QQMusic, candidate.ProviderId);
        Assert.Equal("295618479", candidate.CandidateId);
        Assert.Equal("exact", candidate.QueryVariantId);
        Assert.Equal(TimeSpan.FromSeconds(290), candidate.Duration);
        Assert.Equal("003WkhSf2FOUwq", candidate.FetchMetadata["mid"]);
    }

    [Fact]
    public void SearchResponseSkipsSongsWithoutNumericSongId()
    {
        const string json = """
            {
              "data": {
                "song": {
                  "list": [
                    { "songid": 295618479, "songmid": "numeric-number", "songname": "Number" },
                    { "songid": "325666794", "songmid": "numeric-string", "songname": "String" },
                    { "songmid": "003WkhSf2FOUwq", "songname": "MidOnly" },
                    { "songid": "0", "songmid": "zero", "songname": "Zero" },
                    { "songid": "abc", "songmid": "alpha", "songname": "Alpha" }
                  ]
                }
              }
            }
            """;

        var songs = QqMusicResponseMapper.MapSearchResponse(json);

        Assert.Equal(["295618479", "325666794"], songs.Select(song => song.SongId));
        Assert.All(songs, song => Assert.Equal(TimeSpan.Zero, song.Duration));
        Assert.All(songs, song => Assert.Empty(song.Artists));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("{\"data\":{\"song\":{\"list\":[]}}}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"song\":{\"list\":[{\"songid\":1}]}}")]
    public void SearchResponseToleratesMissingOrMalformedPayload(string? json)
    {
        Assert.Empty(QqMusicResponseMapper.MapSearchResponse(json));
    }

    [Fact]
    public void LyricDownloadMarksHexContentAsEncryptedQrc()
    {
        var candidate = CreateCandidate("739120");

        var payload = QqMusicResponseMapper.MapLyricDownload(
            candidate,
            ReadFixture("qq-lyric-download.xml"));

        Assert.NotNull(payload);
        Assert.Equal(KnownLyricProviders.QQMusic, payload!.ProviderId);
        Assert.Equal("739120", payload.CandidateId);
        Assert.Equal(LyricPayloadFormat.Qrc, payload.Format);
        Assert.True(payload.IsEncrypted);
        Assert.False(payload.IsPureMusic);
        Assert.True(QqMusicResponseMapper.IsHexPayload(payload.OriginalLyrics));
        Assert.Contains("[ti:Flower Dance]", payload.TranslationLyrics, StringComparison.Ordinal);
        Assert.Contains(
            "它们的作用是把氢变成可呼吸的氧气",
            payload.TranslationLyrics,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LyricDownloadKeepsPlainTextContentAsUnencryptedLrc()
    {
        const string response = """
            <!--
            <lyric musicid="1" encode="1"><content type="file"><![CDATA[[00:01.00]plain line]]></content><contentts type="file"><![CDATA[[00:01.00]译文]]></contentts><contentroma type="file"><![CDATA[]]></contentroma></lyric>
            -->
            """;

        var payload = QqMusicResponseMapper.MapLyricDownload(CreateCandidate("1"), response);

        Assert.NotNull(payload);
        Assert.Equal(LyricPayloadFormat.Lrc, payload!.Format);
        Assert.False(payload.IsEncrypted);
        Assert.Equal("[00:01.00]plain line", payload.OriginalLyrics);
        Assert.Equal("[00:01.00]译文", payload.TranslationLyrics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<!--<lyric><content type=\"file\"><![CDATA[   ]]></content><contentts><![CDATA[[00:01.00]only translation]]></contentts></lyric>-->")]
    [InlineData("<html>502 Bad Gateway</html>")]
    public void LyricDownloadReturnsNullWithoutUsableContent(string? response)
    {
        Assert.Null(QqMusicResponseMapper.MapLyricDownload(CreateCandidate("1"), response));
    }

    [Fact]
    public void CDataExtractionRequiresExactTagAndToleratesMissingTags()
    {
        const string response = "<contentts><![CDATA[translated]]></contentts><content><![CDATA[original]]></content>";

        Assert.Equal("original", QqMusicResponseMapper.ExtractCData(response, "content"));
        Assert.Equal("translated", QqMusicResponseMapper.ExtractCData(response, "contentts"));
        Assert.Null(QqMusicResponseMapper.ExtractCData(response, "contentroma"));
        Assert.Null(QqMusicResponseMapper.ExtractCData("<content>no cdata</content>", "content"));
        Assert.Null(QqMusicResponseMapper.ExtractCData(null, "content"));
    }

    [Fact]
    public void LegacyJsonpDecodesBase64LyricAndTranslation()
    {
        var payload = QqMusicResponseMapper.MapLegacyLyric(
            CreateCandidate("295618479"),
            ReadFixture("qq-legacy-lyric.jsonp"));

        Assert.NotNull(payload);
        Assert.Equal(LyricPayloadFormat.Lrc, payload!.Format);
        Assert.False(payload.IsEncrypted);
        Assert.StartsWith("[ti:嘘月", payload.OriginalLyrics, StringComparison.Ordinal);
        Assert.Contains("[00:03.77]ただ染まった", payload.OriginalLyrics, StringComparison.Ordinal);
        Assert.Null(payload.TranslationLyrics);

        Assert.Equal(
            (null, null),
            QqMusicResponseMapper.DecodeLegacyJsonp(
                "MusicJsonCallback_lrc({\"lyric\":null,\"trans\":null})"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MusicJsonCallback_lrc(")]
    [InlineData("MusicJsonCallback_lrc({\"lyric\":\"not base64!!\"})")]
    [InlineData("MusicJsonCallback_lrc({\"lyric\":\"\"})")]
    [InlineData("<html>502 Bad Gateway</html>")]
    public void LegacyMappingReturnsNullForUnusablePayload(string? response)
    {
        Assert.Null(QqMusicResponseMapper.MapLegacyLyric(CreateCandidate("295618479"), response));
    }

    [Theory]
    [InlineData("295618479", true)]
    [InlineData(" 295618479 ", true)]
    [InlineData("003WkhSf2FOUwq", false)]
    [InlineData("295618479abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NumericSongIdDetection(string? value, bool expected) =>
        Assert.Equal(expected, QqMusicResponseMapper.IsNumericSongId(value));

    [Theory]
    [InlineData("295618479", true)]
    [InlineData(" 325666794 ", true)]
    [InlineData("0", false)]
    [InlineData("000000000", false)]
    [InlineData("003WkhSf2FOUwq", false)]
    [InlineData(null, false)]
    public void UsableSongIdDetection(string? value, bool expected) =>
        Assert.Equal(expected, QqMusicResponseMapper.IsUsableSongId(value));

    [Theory]
    [InlineData("789C6D5A", true)]
    [InlineData("789c6d5a", true)]
    [InlineData("789c6d5", false)]
    [InlineData("[00:01.00]plain", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HexPayloadDetection(string? value, bool expected) =>
        Assert.Equal(expected, QqMusicResponseMapper.IsHexPayload(value));

    [Fact]
    public async Task EncryptedQrcPayloadDecodesAndParsesAsWordTimedLyrics()
    {
        var payload = QqMusicResponseMapper.MapLyricDownload(
            CreateCandidate("739120"),
            ReadFixture("qq-lyric-download.xml"));
        Assert.NotNull(payload);

        var decoded = await new LyricifyPayloadDecoder().DecodeAsync(payload!);

        Assert.NotNull(decoded.OriginalLyrics);
        Assert.Contains("[ti:Flower Dance]", decoded.OriginalLyrics, StringComparison.Ordinal);
        Assert.DoesNotContain("<QrcInfos", decoded.OriginalLyrics, StringComparison.Ordinal);
        Assert.Contains("\n[2670,2680]", decoded.OriginalLyrics, StringComparison.Ordinal);

        var parsed = await new LyricifyPayloadParser().ParseAsync(decoded);

        Assert.Equal(LyricPayloadFormat.Qrc, parsed.Format);
        Assert.NotEqual(LyricTimingKind.LineTimed, parsed.TimingKind);
        Assert.Equal(LyricTimingProvenance.ProviderSupplied, parsed.TimingProvenance);
        Assert.Equal(10, parsed.Lines.Count);
        var line = Assert.Single(parsed.Lines, candidate => candidate.StartTime == TimeSpan.FromMilliseconds(5354));
        Assert.Equal(TimeSpan.FromMilliseconds(9165), line.EndTime);
        Assert.Equal("They serve the purpose of changing hydrogen into breathable oxygen", line.Text);
        Assert.Equal("它们的作用是把氢变成可呼吸的氧气", line.Translation);
        Assert.NotEmpty(line.Segments);
    }

    [Fact]
    public void QrcContentUnwrappingLeavesPlainQrcUntouched() =>
        Assert.Equal(
            "[00:01.00]plain",
            QqMusicResponseMapper.UnwrapQrcContent("[00:01.00]plain"));

    [Fact]
    public async Task SearchUsesDirectCandidateOnlyForNumericQqSongId()
    {
        var source = new QqMusicLyricSource();
        var plan = LyricSearchPlanner.CreatePlan(CreateTrack("QQMusic", "295618479", "嘘月"));

        var candidates = await source.SearchAsync(plan);

        var candidate = Assert.Single(candidates);
        Assert.Equal(KnownLyricProviders.QQMusic, candidate.ProviderId);
        Assert.Equal("295618479", candidate.CandidateId);
        Assert.Equal("direct-song-id", candidate.QueryVariantId);
        Assert.Equal("true", candidate.FetchMetadata["direct"]);
    }

    [Theory]
    [InlineData("QQMusic", "295618479", true)]
    [InlineData("QQMusic", " 295618479 ", true)]
    [InlineData("QQMusic", "003WkhSf2FOUwq", false)]
    [InlineData("QQMusic", null, false)]
    [InlineData("Netease", "295618479", false)]
    [InlineData("UnknownPlayer", "295618479", false)]
    public void DirectSongIdRequiresNumericQqSongId(string sourceApp, string? songId, bool expected) =>
        Assert.Equal(expected, QqMusicLyricSource.CanUseDirectSongId(CreateTrack(sourceApp, songId, "嘘月")));

    private static SourceTrackCandidate CreateCandidate(string candidateId) =>
        new(
            KnownLyricProviders.QQMusic,
            candidateId,
            "嘘月",
            ["ヨルシカ"],
            "創作",
            TimeSpan.FromSeconds(290),
            "exact",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mid"] = "003WkhSf2FOUwq"
            });

    private static TrackIdentity CreateTrack(string sourceApp, string? songId, string title) =>
        new(
            $"{sourceApp}|{title}",
            title,
            ["ヨルシカ"],
            "創作",
            TimeSpan.FromSeconds(290),
            sourceApp,
            songId,
            []);

    private static SearchQueryVariant CreateVariant() =>
        new("exact", "嘘月", ["ヨルシカ"], "創作", TimeSpan.FromSeconds(290), []);

    private static string ReadFixture(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaskbarLyrics.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(
            directory!.FullName,
            "TaskbarLyrics.Core.Tests",
            "TestData",
            "Lyrics",
            fileName));
    }
}
