using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class BrowserSessionBridgeTests
{
    private static BrowserMediaDetails Details(long trackVersion = 1, double? position = 10, double rate = 2) =>
        new(1, trackVersion, "media-session", "media-session", "playing", position, 200, rate, 0,
            [], ["pause", "seekto", "nexttrack"], [new("https://cdn.test/cover.webp?signature=needed", "256x256", "image/webp")]);

    private static JsonElement DetailedSession(long revision, BrowserMediaDetails details, long sequence = 1)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Session(revision, sequence).GetRawText())!;
        node["payload"]!["details"] = JsonSerializer.SerializeToNode(details, BrowserMessageProtocol.Json);
        return JsonSerializer.SerializeToElement(node);
    }

    [Fact]
    public void OptionalDetailsPreserveRatesUnknownsAndArtworkWhileLegacyPacketsStillWork()
    {
        var clock = new ManualClock();
        using var bridge = new BrowserSessionBridge(clock);
        bridge.Configure(Settings());
        var client = new BrowserSessionBridge.Client();
        var revision = Handshake(bridge, client);
        Assert.True(bridge.Receive(client, DetailedSession(revision, Details())));
        clock.Advance(2);
        var selected = bridge.Select()!;
        Assert.Equal(14, selected.PositionSeconds);
        Assert.Contains("signature=needed", selected.Details!.Artwork[0].Src);
        Assert.False(bridge.SendCommand(selected, "PreviousTrack"));
        Assert.True(bridge.Receive(client, DetailedSession(revision, Details(trackVersion: 2, position: null), 2)));
        clock.Advance(1);
        Assert.Null(bridge.Select()!.Details!.PositionSeconds);
        Assert.Equal(0, bridge.Select()!.PositionSeconds);
        Assert.False(bridge.Select()!.CanSeek);
        Assert.False(bridge.SendCommand(selected, "TogglePlayPause"));
        Assert.True(bridge.Receive(client, Session(revision, 3)));
        Assert.Null(bridge.Select()!.Details);
        Assert.Equal(10, bridge.Select()!.PositionSeconds);
    }

    [Fact]
    public void MalformedDetailsDoNotDisconnectOrReplaceAHealthyCandidate()
    {
        using var bridge = new BrowserSessionBridge();
        bridge.Configure(Settings()); var client = new BrowserSessionBridge.Client(); var revision = Handshake(bridge, client);
        Assert.True(bridge.Receive(client, DetailedSession(revision, Details())));
        foreach (var invalid in new[] { Details() with { Version = 2 }, Details() with { PlaybackRate = 17 },
            Details() with { SampleAgeSeconds = 7 }, Details() with { Artwork = [new("file:///secret", "", "")] } })
        {
            Assert.True(bridge.Receive(client, DetailedSession(revision, invalid, 2)));
            Assert.Equal(1, bridge.Select()!.Sequence);
            Assert.True(bridge.SuppressesSmtc("msedge"));
        }
        var node = System.Text.Json.Nodes.JsonNode.Parse(DetailedSession(revision, Details(), 3).GetRawText())!;
        node["payload"]!["details"]!["playbackRate"] = "2";
        Assert.True(bridge.Receive(client, JsonSerializer.SerializeToElement(node)));
        Assert.Equal(1, bridge.Select()!.Sequence);
    }

    [Fact]
    public void ControlResultsAreBoundToPendingRequestsAndNextTrackMayConfirmTheNewVersion()
    {
        var clock = new ManualClock(); using var bridge = new BrowserSessionBridge(clock);
        bridge.Configure(Settings()); var client = new BrowserSessionBridge.Client(); var revision = Handshake(bridge, client);
        bridge.Receive(client, DetailedSession(revision, Details()));
        var results = new List<BrowserSessionBridge.BrowserCommandResult>(); bridge.CommandCompleted += results.Add;
        Assert.True(bridge.SendCommand(bridge.Select()!, "NextTrack"));
        Assert.True(client.Outgoing.Reader.TryRead(out var command));
        var request = command.GetProperty("payload");
        Assert.Equal(1, request.GetProperty("trackVersion").GetInt64());
        bridge.Receive(client, DetailedSession(revision, Details(trackVersion: 2), 2));
        bridge.Receive(client, BrowserMessageProtocol.Message("result", new { requestId = request.GetProperty("requestId").GetString(), ok = true, status = "observed", reason = "" }));
        Assert.True(Assert.Single(results).Confirmed);
        bridge.Receive(client, BrowserMessageProtocol.Message("result", new { requestId = "unknown", ok = true, status = "observed" }));
        Assert.Single(results);
        Assert.True(bridge.SendCommand(bridge.Select()!, "TogglePlayPause"));
        clock.Advance(5);
        bridge.Receive(client, BrowserMessageProtocol.Message("heartbeat", new { }));
        Assert.Equal("timeout", results[1].Reason);
        Assert.False(results[1].Confirmed);
    }

    private static readonly string[] GrantedOrigins = ["https://music.test"];
    private static AppSettings Settings() => new()
    {
        EnableBrowser = true,
        BrowserWebsiteRecognitionEnabled = true,
        BrowserSites = [new("music", "Music", "https://music.test")]
    };

    private static long Handshake(BrowserSessionBridge bridge, BrowserSessionBridge.Client client, string browser = "Edge")
    {
        Assert.True(bridge.Receive(client, BrowserMessageProtocol.Message("hello", new { clientId = "profile", browser }), true));
        Assert.True(client.Outgoing.Reader.TryRead(out var message));
        var revision = message.GetProperty("payload").GetProperty("revision").GetInt64();
        Assert.True(bridge.Receive(client, BrowserMessageProtocol.Message("ready", new { revision, origins = GrantedOrigins })));
        return revision;
    }

    private static JsonElement Session(long revision, long sequence = 1, string url = "https://music.test/song?private=secret") =>
        BrowserMessageProtocol.Message("session", new
        {
            revision,
            sequence,
            documentId = "doc",
            mediaId = "media",
            url,
            title = "Song",
            artist = "Artist",
            album = "Album",
            isPlaying = true,
            positionSeconds = 10,
            durationSeconds = 100,
            canSeek = true
        });

    [Fact]
    public async Task LocalPipeRoundTripsConfigurationMediaAndControlsWithoutUsingTheRunningAppsPipe()
    {
        using var bridge = new BrowserSessionBridge();
        bridge.Configure(Settings());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "TaskbarLyrics.Browser.Test." + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var remote = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var accept = server.WaitForConnectionAsync(stop.Token);
        await remote.ConnectAsync(stop.Token);
        await accept;
        var client = new BrowserSessionBridge.Client { Stream = server };
        var serve = (Task)typeof(BrowserSessionBridge).GetMethod("ServeAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, [client, stop.Token])!;
        try
        {
            await BrowserMessageProtocol.WriteAsync(remote,
                BrowserMessageProtocol.Message("hello", new { clientId = "pipe-profile", browser = "Chrome" }), stop.Token);
            var configuration = await BrowserMessageProtocol.ReadAsync(remote, stop.Token);
            Assert.Equal("configuration", configuration!.Value.GetProperty("type").GetString());
            var revision = configuration.Value.GetProperty("payload").GetProperty("revision").GetInt64();
            await BrowserMessageProtocol.WriteAsync(remote,
                BrowserMessageProtocol.Message("ready", new { revision, origins = GrantedOrigins }), stop.Token);
            await BrowserMessageProtocol.WriteAsync(remote, Session(revision), stop.Token);
            BrowserMediaSession? selected;
            while ((selected = bridge.Select()) is null) await Task.Delay(10, stop.Token);
            Assert.True(bridge.SuppressesSmtc("chrome.exe"));
            Assert.Equal("Song", selected.Title);
            Assert.True(bridge.SendCommand(selected, "TogglePlayPause"));
            var command = await BrowserMessageProtocol.ReadAsync(remote, stop.Token);
            Assert.Equal("command", command!.Value.GetProperty("type").GetString());
            Assert.Equal("doc", command.Value.GetProperty("payload").GetProperty("documentId").GetString());
            Assert.Equal("TogglePlayPause", command.Value.GetProperty("payload").GetProperty("action").GetString());
        }
        finally
        {
            stop.Cancel();
            await serve.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void HealthyEmptyConnectionSuppressesOnlyItsBrowserAndDisconnectFallsBackAfterGrace()
    {
        var clock = new ManualClock();
        using var bridge = new BrowserSessionBridge(clock: clock);
        bridge.Configure(Settings());
        var client = new BrowserSessionBridge.Client();
        var revision = Handshake(bridge, client);
        Assert.True(bridge.SuppressesSmtc("msedge.exe"));
        Assert.False(bridge.SuppressesSmtc("chrome.exe"));
        Assert.Null(bridge.Select());
        Assert.True(bridge.Receive(client, Session(revision)));
        Assert.Equal("https://music.test", bridge.Select()!.Url);
        client.Connected = false;
        client.Disconnected = clock.GetUtcNow();
        clock.Advance(1);
        Assert.True(bridge.SuppressesSmtc("msedge.exe"));
        clock.Advance(2);
        Assert.False(bridge.SuppressesSmtc("msedge.exe"));
        Assert.Null(bridge.Select());
    }

    [Fact]
    public void ReconfigurationWaitsForMatchingAcknowledgementAndRejectsUnlistedSites()
    {
        using var bridge = new BrowserSessionBridge();
        var settings = Settings();
        bridge.Configure(settings);
        var client = new BrowserSessionBridge.Client();
        var revision = Handshake(bridge, client);
        Assert.True(bridge.Receive(client, Session(revision)));
        settings.BrowserSites.Clear();
        bridge.Configure(settings);
        Assert.False(bridge.SuppressesSmtc("msedge"));
        Assert.True(bridge.Receive(client, BrowserMessageProtocol.Message("ready", new { revision, origins = Array.Empty<string>() })));
        Assert.False(bridge.SuppressesSmtc("msedge"));
        Assert.True(client.Outgoing.Reader.TryRead(out var configuration));
        revision = configuration.GetProperty("payload").GetProperty("revision").GetInt64();
        Assert.True(bridge.Receive(client, BrowserMessageProtocol.Message("ready", new { revision, origins = Array.Empty<string>() })));
        Assert.True(bridge.SuppressesSmtc("msedge"));
        Assert.Null(bridge.Select());
        Assert.True(bridge.Receive(client, Session(revision)));
        Assert.Null(bridge.Select());
        Assert.True(bridge.SuppressesSmtc("msedge"));
        settings.BrowserWebsiteRecognitionEnabled = false;
        bridge.Configure(settings);
        Assert.False(bridge.SuppressesSmtc("msedge"));
    }

    [Fact]
    public void StaleSequencesDoNotReplaceCurrentSnapshotAndHeartbeatDoesNotKeepMediaAlive()
    {
        var clock = new ManualClock();
        using var bridge = new BrowserSessionBridge(clock: clock);
        bridge.Configure(Settings());
        var client = new BrowserSessionBridge.Client();
        var revision = Handshake(bridge, client);
        Assert.True(bridge.Receive(client, Session(revision, 2)));
        Assert.True(bridge.Receive(client, Session(revision, 1)));
        Assert.Equal(2, bridge.Select()!.Sequence);
        clock.Advance(7);
        Assert.False(bridge.SuppressesSmtc("msedge"));
        Assert.True(bridge.Receive(client, BrowserMessageProtocol.Message("heartbeat", new { })));
        Assert.True(bridge.SuppressesSmtc("msedge"));
        Assert.Null(bridge.Select());
    }

    [Fact]
    public void MediaBeforeConfigurationAcknowledgementIsIgnoredAndControlsNeverRedirectAfterDocumentRemoval()
    {
        using var bridge = new BrowserSessionBridge();
        bridge.Configure(Settings());
        var client = new BrowserSessionBridge.Client();
        Assert.True(bridge.Receive(client, BrowserMessageProtocol.Message("hello", new { clientId = "profile", browser = "Edge" }), true));
        Assert.True(client.Outgoing.Reader.TryRead(out var configuration));
        var revision = configuration.GetProperty("payload").GetProperty("revision").GetInt64();
        Assert.True(bridge.Receive(client, Session(revision)));
        Assert.Null(bridge.Select());
        Assert.True(bridge.Receive(client, BrowserMessageProtocol.Message("ready", new { revision, origins = GrantedOrigins })));
        Assert.True(bridge.Receive(client, Session(revision, 2)));
        var selected = bridge.Select()!;
        Assert.True(bridge.SendCommand(selected, "TogglePlayPause"));
        Assert.True(client.Outgoing.Reader.TryRead(out var command));
        Assert.Equal("doc", command.GetProperty("payload").GetProperty("documentId").GetString());
        Assert.Equal("media", command.GetProperty("payload").GetProperty("mediaId").GetString());
        Assert.True(bridge.Receive(client, BrowserMessageProtocol.Message("removed", new { documentId = "doc" })));
        Assert.False(bridge.SendCommand(selected, "TogglePlayPause"));
        Assert.False(client.Outgoing.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData("{\"version\":\"1\",\"type\":\"hello\",\"payload\":{}}")]
    [InlineData("{\"version\":2,\"type\":\"hello\",\"payload\":{}}")]
    [InlineData("{\"version\":1,\"type\":\"ready\",\"payload\":{\"revision\":\"1\",\"origins\":[]}}")]
    [InlineData("{\"version\":1,\"type\":\"unknown\",\"payload\":{}}")]
    public void InvalidMessagesFailSafely(string json)
    {
        using var bridge = new BrowserSessionBridge();
        bridge.Configure(Settings());
        var client = new BrowserSessionBridge.Client();
        Handshake(bridge, client);
        using var document = JsonDocument.Parse(json);
        Assert.False(bridge.Receive(client, document.RootElement));
    }

    [Fact]
    public async Task FramingRoundTripsAndRejectsOversizedOrTruncatedPackets()
    {
        using var stream = new MemoryStream();
        await BrowserMessageProtocol.WriteAsync(stream, BrowserMessageProtocol.Message("heartbeat", new { }), CancellationToken.None);
        stream.Position = 0;
        var message = await BrowserMessageProtocol.ReadAsync(stream, CancellationToken.None);
        Assert.True(BrowserMessageProtocol.TryReadEnvelope(message!.Value, out var type, out _));
        Assert.Equal("heartbeat", type);
        Assert.Null(await BrowserMessageProtocol.ReadAsync(stream, CancellationToken.None));
        using var oversized = new MemoryStream(BitConverter.GetBytes(BrowserMessageProtocol.MaximumBytes + 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => BrowserMessageProtocol.ReadAsync(oversized, CancellationToken.None));
        using var truncated = new MemoryStream(new byte[] { 10, 0 });
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrowserMessageProtocol.ReadAsync(truncated, CancellationToken.None));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
}
