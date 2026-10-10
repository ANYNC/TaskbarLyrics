using System.Net;
using System.Net.Http;
using System.Text;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class QqMusicRecoveryTests
{
    private const string Mid = "003WkhSf2FOUwq";

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"song\":[]}}")]
    public void WrongJsonNodeTypesDoNotThrow(string json) =>
        Assert.Empty(QqMusicResponseMapper.MapSearchResponse(json));

    [Fact]
    public void MalformedEntriesDoNotDiscardValidSongs()
    {
        const string json = """
            {"data":{"song":{"list":[null, [], {"songid":"1","singer":[null,1,{"name":"Artist"}],"interval":1e309}]}}}
            """;
        var song = Assert.Single(QqMusicResponseMapper.MapSearchResponse(json));
        Assert.Equal("1", song.SongId);
        Assert.Equal(["Artist"], song.Artists);
        Assert.Equal(TimeSpan.Zero, song.Duration);
        Assert.Null(QqMusicResponseMapper.MapLegacyLyric(Candidate(), "MusicJsonCallback_lrc([])"));
    }

    [Theory]
    [InlineData(2001, true)]
    [InlineData(401, false)]
    public async Task SearchBusinessFailureIsNotReportedAsNoLyrics(int code, bool transient)
    {
        using var source = Source(_ => System.Text.Json.JsonSerializer.Serialize(
            new { code, data = new { song = new { list = Array.Empty<object>() } } }));
        var exception = await Assert.ThrowsAsync<QqMusicApiException>(() =>
            source.SearchAsync(Plan("OtherPlayer", null)));
        Assert.Equal(transient, LyricTransientFailurePolicy.IsTransient(exception));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"data\":null}")]
    public async Task MalformedSearchResponseRemainsAProviderFailure(string response)
    {
        using var source = Source(_ => response);
        await Assert.ThrowsAsync<FormatException>(() => source.SearchAsync(Plan("OtherPlayer", null)));
    }

    [Theory]
    [InlineData("{\"code\":{},\"data\":{\"song\":{\"list\":[]}}}")]
    [InlineData("{\"code\":null,\"data\":{\"song\":{\"list\":[]}}}")]
    [InlineData("{\"code\":\"invalid\",\"data\":{\"song\":{\"list\":[]}}}")]
    public async Task InvalidBusinessCodeIsAProviderFailure(string response)
    {
        using var source = Source(_ => response);
        await Assert.ThrowsAsync<FormatException>(() => source.SearchAsync(Plan("OtherPlayer", null)));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void QrcXmlPreservesAttributeNewlinesAndDecodesEntitiesRegardlessOfOrder(string newline)
    {
        var xml = "<QrcInfos><Lyric_1 LyricContent='[0,1000]A &amp; B&#10;[1000,1000]&quot;C&quot;" +
                  newline + "[2000,1000]D' LyricType=\"1\" /></QrcInfos>";
        Assert.Equal("[0,1000]A & B\n[1000,1000]\"C\"" + newline + "[2000,1000]D",
            QqMusicResponseMapper.UnwrapQrcContent(xml));
        Assert.Throws<FormatException>(() =>
            QqMusicResponseMapper.UnwrapQrcContent("<!DOCTYPE x [<!ENTITY y 'bad'>]><x LyricContent='&y;'/>"));
    }

    [Fact]
    public async Task SongMidConvertsToExactSongIdWithoutKeywordSearch()
    {
        var requests = new List<Request>();
        using var source = Source(request =>
        {
            requests.Add(request);
            Assert.Contains("songmid=" + Mid, request.Body, StringComparison.Ordinal);
            return Detail("295618479", Mid);
        });
        var candidate = Assert.Single(await source.SearchAsync(Plan("QQMusic", Mid)));
        Assert.Equal("295618479", candidate.CandidateId);
        Assert.Equal(Mid, candidate.FetchMetadata["mid"]);
        Assert.Equal("direct-song-id", candidate.QueryVariantId);
        Assert.Single(requests);
        Assert.EndsWith("fcg_play_single_song.fcg", requests[0].Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrong-mid")]
    [InlineData(null)]
    public async Task UnresolvedMidFallsBackToKeywordSearch(string? responseMid)
    {
        var paths = new List<string>();
        using var source = Source(request =>
        {
            paths.Add(request.Path);
            return request.Path.EndsWith("fcg_play_single_song.fcg", StringComparison.Ordinal)
                ? responseMid is null ? "{\"code\":0,\"data\":[]}" : Detail("999", responseMid)
                : Fixture("qq-search-response.json");
        });
        var candidates = await source.SearchAsync(Plan("QQMusic", Mid));
        var candidate = Assert.Single(candidates, item => item.CandidateId == "295618479");
        Assert.Equal("exact", candidate.QueryVariantId);
        Assert.Equal(2, paths.Count);
    }

    [Theory]
    [InlineData("<content><![CDATA[]]></content>")]
    [InlineData("<content><![CDATA[AA]]></content>")]
    [InlineData("<content><![CDATA[not timed lyrics]]></content>")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("<result>2001</result><content><![CDATA[]]></content>")]
    public async Task InvalidQrcFallsBackToTheSameSongLrc(string download)
    {
        var requests = new List<Request>();
        using var source = Source(request =>
        {
            requests.Add(request);
            return request.Path.EndsWith("lyric_download.fcg", StringComparison.Ordinal)
                ? download : Legacy("[00:01.00]fallback", "[00:01.00]译文");
        });
        var payload = await source.FetchAsync(Candidate());
        Assert.NotNull(payload);
        Assert.Equal("739120", payload.CandidateId);
        Assert.Equal(LyricPayloadFormat.Lrc, payload.Format);
        Assert.False(payload.IsEncrypted);
        var parsed = await new LyricifyPayloadParser().ParseAsync(Decoded(payload));
        Assert.Equal("fallback", Assert.Single(parsed.Lines).Text);
        Assert.Equal("译文", parsed.Lines[0].Translation);
        Assert.Equal(2, requests.Count);
        Assert.Contains("songmid=" + Mid, requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NumericDirectCandidateLooksUpMidOnlyWhenLrcFallbackIsNeeded()
    {
        var requests = new List<Request>();
        using var source = Source(request =>
        {
            requests.Add(request);
            if (request.Path.EndsWith("lyric_download.fcg", StringComparison.Ordinal))
            {
                return "<content><![CDATA[]]></content>";
            }

            if (request.Path.EndsWith("fcg_play_single_song.fcg", StringComparison.Ordinal))
            {
                Assert.Contains("songid=739120", request.Body, StringComparison.Ordinal);
                return Detail("739120", Mid);
            }

            Assert.Contains("songmid=" + Mid, request.Body, StringComparison.Ordinal);
            return Legacy("[00:01.00]fallback");
        });
        var candidate = Assert.Single(await source.SearchAsync(Plan("QQMusic", " 739120 ")));
        Assert.Empty(requests);
        Assert.Equal("739120", candidate.CandidateId);
        Assert.NotNull(await source.FetchAsync(candidate));
        Assert.Equal(3, requests.Count);
    }

    [Fact]
    public async Task ValidQrcDoesNotRequestLegacyLyrics()
    {
        var calls = 0;
        using var source = Source(_ =>
        {
            calls++;
            return Fixture("qq-lyric-download.xml");
        });
        var payload = await source.FetchAsync(Candidate());
        Assert.NotNull(payload);
        Assert.False(payload.IsEncrypted);
        var parsed = await new LyricifyPayloadParser().ParseAsync(Decoded(payload));
        Assert.Equal(10, parsed.Lines.Count);
        Assert.Contains(parsed.Lines, line => line.Segments.Count > 0);
        Assert.Contains(parsed.Lines, line => line.Translation == "它们的作用是把氢变成可呼吸的氧气");
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EncryptedTranslationIsDecodedIndependentlyOfTheOriginal(bool plainOriginal)
    {
        var fixture = Fixture("qq-lyric-download.xml");
        var cipher = QqMusicResponseMapper.ExtractCData(fixture, "content")!;
        var response = plainOriginal
            ? $"<content><![CDATA[[00:05.354]original]]></content><contentts><![CDATA[{cipher}]]></contentts>"
            : fixture.Replace(QqMusicResponseMapper.ExtractCData(fixture, "contentts")!, cipher, StringComparison.Ordinal);
        using var source = Source(_ => response);
        var payload = await source.FetchAsync(Candidate());
        Assert.NotNull(payload);
        Assert.False(QqMusicResponseMapper.IsHexPayload(payload.TranslationLyrics));
        Assert.DoesNotContain("<QrcInfos", payload.TranslationLyrics, StringComparison.Ordinal);
        var parsed = await new LyricifyPayloadParser().ParseAsync(Decoded(payload));
        Assert.Contains(parsed.Lines, line =>
            line.Translation == "They serve the purpose of changing hydrogen into breathable oxygen");
    }

    [Fact]
    public async Task DamagedTranslationKeepsUsableOriginalLyrics()
    {
        var fixture = Fixture("qq-lyric-download.xml");
        var response = fixture.Replace(
            QqMusicResponseMapper.ExtractCData(fixture, "contentts")!, "AA", StringComparison.Ordinal);
        using var source = Source(_ => response);
        var payload = await source.FetchAsync(Candidate());
        Assert.NotNull(payload);
        Assert.Null(payload.TranslationLyrics);
        Assert.Equal(10, (await new LyricifyPayloadParser().ParseAsync(Decoded(payload))).Lines.Count);
    }

    [Fact]
    public async Task CallerCancellationDoesNotRetryOrRequestFallback()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var api = new QqMusicApiClient(() => new HttpClient(new Handler(async (_, token) =>
        {
            calls++;
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response("");
        })));
        using var source = new QqMusicLyricSource(api);
        var fetch = source.FetchAsync(Candidate(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        Assert.Equal(1, calls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.SearchAsync(Plan("QQMusic", "739120"), cancellation.Token));
    }

    [Fact]
    public async Task NetworkRecoveryRefreshesClientAndRetriesOnce()
    {
        var failed = new Handler((_, _) => throw new HttpRequestException("connection lost"));
        var recovered = new Handler((_, _) => Task.FromResult(Response("recovered")));
        var clients = new Queue<HttpClient>([new(failed), new(recovered)]);
        using var api = new QqMusicApiClient(() => clients.Dequeue());
        Assert.Equal("recovered", await api.SearchAsync("song", CancellationToken.None));
        Assert.True(failed.IsDisposed);
        Assert.False(recovered.IsDisposed);
        Assert.Empty(clients);
    }

    [Fact]
    public async Task RefreshDoesNotDisposeAnotherActiveRequest()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishOther = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var failed = new Handler(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstStarted.SetResult();
                await failFirst.Task.WaitAsync(token);
                throw new HttpRequestException("connection lost");
            }

            otherStarted.SetResult();
            await finishOther.Task.WaitAsync(token);
            return Response("other");
        });
        var clients = new Queue<HttpClient>([new(failed),
            new(new Handler((_, _) => Task.FromResult(Response("recovered"))))]);
        using var api = new QqMusicApiClient(() => clients.Dequeue());
        var first = api.SearchAsync("first", CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var other = api.SearchAsync("other", CancellationToken.None);
        await otherStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        failFirst.SetResult();
        Assert.Equal("recovered", await first.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(failed.IsDisposed);
        finishOther.SetResult();
        Assert.Equal("other", await other.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(failed.IsDisposed);
    }

    [Fact]
    public async Task PermanentHttpFailureDoesNotRefreshOrRetry()
    {
        var calls = 0;
        var factories = 0;
        using var api = new QqMusicApiClient(() =>
        {
            factories++;
            return new HttpClient(new Handler((_, _) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }));
        });
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => api.SearchAsync("song", CancellationToken.None));
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal(1, factories);
    }

    [Theory]
    [InlineData("<html>bad gateway</html>")]
    [InlineData("MusicJsonCallback_lrc([])")]
    [InlineData("MusicJsonCallback_lrc({\"code\":0,\"lyric\":\"not base64!\"})")]
    public async Task InvalidLegacyResponseIsNotClassifiedAsNoLyrics(string legacy)
    {
        using var source = Source(request => request.Path.EndsWith("lyric_download.fcg", StringComparison.Ordinal)
            ? "<content><![CDATA[]]></content>" : legacy);
        await Assert.ThrowsAsync<FormatException>(() => source.FetchAsync(Candidate()));
    }

    [Fact]
    public async Task GenuineEmptyLyricsReturnNullWithoutRetrying()
    {
        var calls = 0;
        using var source = Source(request =>
        {
            calls++;
            return request.Path.EndsWith("lyric_download.fcg", StringComparison.Ordinal)
                ? "<content><![CDATA[]]></content>" : Legacy("");
        });
        Assert.Null(await source.FetchAsync(Candidate()));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PrimaryBusinessFailureRemainsRetryableWhenFallbackHasNoLyrics()
    {
        using var source = Source(request => request.Path.EndsWith("lyric_download.fcg", StringComparison.Ordinal)
            ? "<result>2001</result>" : Legacy(""));
        var exception = await Assert.ThrowsAsync<QqMusicApiException>(() => source.FetchAsync(Candidate()));
        Assert.True(LyricTransientFailurePolicy.IsTransient(exception));
    }

    [Fact]
    public async Task MissingMidLookupFailureIsNotClassifiedAsNoLyrics()
    {
        using var source = Source(request => request.Path.EndsWith("lyric_download.fcg", StringComparison.Ordinal)
            ? "<content><![CDATA[]]></content>" : "{\"code\":2001}");
        var candidate = Assert.Single(await source.SearchAsync(Plan("QQMusic", "739120")));
        var exception = await Assert.ThrowsAsync<QqMusicApiException>(() => source.FetchAsync(candidate));
        Assert.True(LyricTransientFailurePolicy.IsTransient(exception));
    }

    [Theory]
    [InlineData("{\"code\":0,\"data\":[{\"songid\":1,\"songmid\":\"mid\"}]}")]
    [InlineData("{\"code\":0,\"data\":[{\"id\":1,\"mid\":\"mid\"}]}")]
    public void SongDetailSupportsBothResponseSchemasAndRejectsOtherIds(string response)
    {
        Assert.Equal("1", QqMusicResponseMapper.MapSongDetail(response, "mid")!.SongId);
        Assert.Null(QqMusicResponseMapper.MapSongDetail(response, "different-mid"));
        Assert.Null(QqMusicResponseMapper.MapSongDetail(response, "2"));
    }

    [Fact]
    public async Task PersistentNetworkFailureRetriesOnlyOnce()
    {
        var calls = 0;
        using var api = new QqMusicApiClient(() =>
            new HttpClient(new Handler((_, _) =>
            {
                calls++;
                throw new HttpRequestException("still disconnected");
            })));
        await Assert.ThrowsAsync<HttpRequestException>(() => api.SearchAsync("song", CancellationToken.None));
        Assert.Equal(2, calls);
    }

    private static QqMusicLyricSource Source(Func<Request, string> respond) =>
        new(new QqMusicApiClient(() => new HttpClient(new Handler(async (request, token) =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
            return Response(respond(new Request(request.RequestUri!.AbsolutePath, body)));
        }))));

    private static HttpResponseMessage Response(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    private static string Detail(string id, string mid) =>
        $$"""getOneSongInfoCallback({"code":0,"data":[{"id":"{{id}}","mid":"{{mid}}"}]})""";

    private static string Legacy(string original, string? translation = null) =>
        $$"""MusicJsonCallback_lrc({"code":0,"lyric":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(original))}}","trans":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(translation ?? ""))}}"})""";

    private static LyricSearchPlan Plan(string sourceApp, string? id) =>
        LyricSearchPlanner.CreatePlan(new TrackIdentity("track", "嘘月", ["ヨルシカ"], "創作",
            TimeSpan.FromSeconds(290), sourceApp, id, []));

    private static SourceTrackCandidate Candidate() =>
        new(KnownLyricProviders.QQMusic, "739120", "Flower Dance", ["DJ OKAWARI"], "",
            TimeSpan.FromSeconds(240), "exact",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["mid"] = Mid });

    private static DecodedLyricPayload Decoded(RawLyricPayload payload) =>
        new(payload.ProviderId, payload.CandidateId, payload.Format,
            payload.OriginalLyrics, payload.TranslationLyrics, payload.IsPureMusic, payload.Diagnostics);

    private static string Fixture(string name) => QqMusicLyricSourceTests.ReadFixture(name);

    private sealed record Request(string Path, string Body);

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public bool IsDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
