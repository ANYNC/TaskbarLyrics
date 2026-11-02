using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed record BrowserConnectionStatus(string Browser, bool Connected, bool WebsiteMode,
    string State, IReadOnlyList<string> GrantedOrigins);

// Owns the local transport and its snapshots; never retains a browser COM object.
internal sealed class BrowserSessionBridge : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private readonly TimeProvider _clock;
    private Task _listen = Task.CompletedTask;
    private readonly ConcurrentDictionary<Guid, Task> _connections = new();
    private static readonly string[] BrowserKinds = ["Chrome", "Edge"];
    private int _started;
    private AppSettings _settings = new();
    private string _configuration = string.Empty;
    private long _revision;
    private string? _currentKey;
    private int _disposed;
    internal event Action<string>? SiteRequested;
    internal event Action<BrowserCommandResult>? CommandCompleted;

    internal static string PipeName => "TaskbarLyrics.Browser." + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..24];

    internal BrowserSessionBridge(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    internal void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0 || Volatile.Read(ref _disposed) != 0) return;
        _listen = ListenAsync(_stop.Token);
        TaskObserver.Observe(_listen, "browser bridge listener");
    }

    internal void Configure(AppSettings settings)
    {
        var snapshot = settings.Clone();
        snapshot.BrowserSites = BrowserSiteRule.Normalize(snapshot.BrowserSites);
        var fingerprint = JsonSerializer.Serialize(new
        {
            snapshot.EnableBrowser,
            snapshot.BrowserWebsiteRecognitionEnabled,
            snapshot.BrowserSites
        }, BrowserMessageProtocol.Json);
        lock (_gate)
        {
            _settings = snapshot;
            if (_configuration == fingerprint) return;
            _configuration = fingerprint;
            _revision++;
            foreach (var client in _clients.Values) SendConfiguration(client);
        }
    }

    internal IReadOnlyList<BrowserConnectionStatus> GetStatus()
    {
        lock (_gate)
        {
            return BrowserKinds.Select(browser =>
            {
                var clients = _clients.Values.Where(client => client.Browser == browser).ToArray();
                var connected = clients.Any(IsLive);
                var ready = _settings.EnableBrowser && _settings.BrowserWebsiteRecognitionEnabled && clients.Any(IsReady);
                var state = !_settings.BrowserWebsiteRecognitionEnabled ? "legacy" : ready ? "website" : "fallback";
                return new BrowserConnectionStatus(browser, connected, ready, state,
                    clients.Where(IsLive).SelectMany(client => client.Origins).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            }).ToArray();
        }
    }

    internal bool SuppressesSmtc(string sourceId)
    {
        var browser = GetBrowserKind(sourceId);
        lock (_gate) return browser is not null && _settings.EnableBrowser &&
            _settings.BrowserWebsiteRecognitionEnabled && _clients.Values.Any(client => client.Browser == browser && IsReady(client));
    }

    internal static string? GetBrowserKind(string sourceId) =>
        sourceId.Contains("msedge", StringComparison.OrdinalIgnoreCase) || sourceId.Contains("microsoftedge", StringComparison.OrdinalIgnoreCase)
            ? "Edge" : sourceId.Contains("chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome" : null;

    internal BrowserMediaSession? Select()
    {
        lock (_gate)
        {
            if (!_settings.EnableBrowser || !_settings.BrowserWebsiteRecognitionEnabled) return null;
            var selected = BrowserSessionSelection.Select(_clients.Values.Where(IsReady)
                .SelectMany(client => client.Sessions.Values.Where(item => _clock.GetUtcNow() - item.Received <= TimeSpan.FromSeconds(6))
                    .Select(CurrentSession)), _settings.BrowserSites, _currentKey);
            _currentKey = selected?.Key;
            return selected;
        }
    }

    internal BrowserMediaSession? Find(string key)
    {
        lock (_gate)
        {
            if (!_settings.EnableBrowser || !_settings.BrowserWebsiteRecognitionEnabled) return null;
            var item = _clients.Values.Where(IsReady).SelectMany(client => client.Sessions.Values)
                .FirstOrDefault(item => item.Session.Key == key && _clock.GetUtcNow() - item.Received <= TimeSpan.FromSeconds(6));
            return item is null ? null : CurrentSession(item);
        }
    }

    private BrowserMediaSession CurrentSession(ReceivedSession item)
    {
        var session = item.Session;
        var details = session.Details;
        var elapsed = Math.Max(0, (_clock.GetUtcNow() - item.Received).TotalSeconds) + (details?.SampleAgeSeconds ?? 0);
        var position = details is { PositionSeconds: null } ? 0 : Math.Clamp(
            session.PositionSeconds + (session.IsPlaying ? elapsed * (details?.PlaybackRate ?? 1) : 0),
            0, session.DurationSeconds > 0 ? session.DurationSeconds : 31536000);
        return session with { PositionSeconds = position,
            Details = details is null ? null : details with { PositionSeconds = details.PositionSeconds is null ? null : position } };
    }

    internal bool SendCommand(BrowserMediaSession session, string action, double? seconds = null)
    {
        lock (_gate)
        {
            var client = _clients.Values.FirstOrDefault(item => IsLive(item) && IsReady(item) && item.Id == session.ClientId);
            var current = Find(session.Key);
            if (client is null || current is null || session.Details?.TrackVersion != current.Details?.TrackVersion) return false;
            var details = current.Details;
            var mediaAction = action switch
            {
                "TogglePlayPause" => current.IsPlaying ? "pause" : "play",
                "PreviousTrack" => "previoustrack", "NextTrack" => "nexttrack",
                "SeekBackward" => "seekbackward", "SeekForward" => "seekforward", "SeekTo" => "seekto", _ => null
            };
            if (mediaAction is null || details is not null && (!details.Actions.Contains(mediaAction) ||
                action == "TogglePlayPause" && details.PlaybackState is "unknown" or "seeking")) return false;
            if (action == "SeekTo" && (seconds is null || !double.IsFinite(seconds.Value) || seconds < 0 || !current.CanSeek ||
                details is not null && (details.Seekable.Count > 0
                    ? !details.Seekable.Any(range => seconds >= range.Start && seconds <= range.End)
                    : details.DurationSeconds is null || seconds > details.DurationSeconds))) return false;
            foreach (var expired in client.Pending.Where(item => _clock.GetUtcNow() - item.Value.Sent > TimeSpan.FromSeconds(4)).Select(item => item.Key).ToArray())
                client.Pending.Remove(expired);
            if (client.Pending.Count >= 32) return false;
            var requestId = Guid.NewGuid().ToString("N");
            if (!client.Outgoing.Writer.TryWrite(BrowserMessageProtocol.Message("command", new
            {
                requestId,
                session.DocumentId,
                session.MediaId,
                trackVersion = details?.TrackVersion,
                action,
                seconds
            }))) return false;
            client.Pending[requestId] = new(session.Key, details?.TrackVersion, action, _clock.GetUtcNow());
            return true;
        }
    }

    private bool IsLive(Client client) => client.Connected && _clock.GetUtcNow() - client.Seen <= TimeSpan.FromSeconds(6);
    private bool IsReady(Client client) => client.Revision == _revision &&
        (IsLive(client) || !client.Connected && _clock.GetUtcNow() - client.Disconnected <= TimeSpan.FromSeconds(2));

    private void SendConfiguration(Client client)
    {
        if (!client.Outgoing.Writer.TryWrite(BrowserMessageProtocol.Message("configuration", new
        {
            revision = _revision,
            mediaDetailsVersion = 1,
            enabled = _settings.EnableBrowser && _settings.BrowserWebsiteRecognitionEnabled,
            sites = _settings.BrowserSites
        }))) client.Stream?.Dispose();
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? stream = null;
            try
            {
                stream = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 16,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await stream.WaitForConnectionAsync(token).ConfigureAwait(false);
                var client = new Client { Stream = stream };
                _clients[client.Connection] = client;
                var connection = ServeAsync(client, token);
                _connections[client.Connection] = connection;
                _ = connection.ContinueWith(completed => { _connections.TryRemove(client.Connection, out _); _ = completed.Exception; },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                TaskObserver.Observe(connection, "browser bridge connection");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { stream?.Dispose(); break; }
            catch (IOException exception)
            {
                stream?.Dispose();
                Log.Diagnostic("BROWSER", $"ListenerUnavailable Error={exception.GetType().Name}");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    private async Task ServeAsync(Client client, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var write = WriteOutgoingAsync(client, lifetime.Token);
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var first = await BrowserMessageProtocol.ReadAsync(client.Stream!, handshake.Token).ConfigureAwait(false);
            if (first is null || !Receive(client, first.Value, requireHello: true)) return;
            while (await BrowserMessageProtocol.ReadAsync(client.Stream!, token).ConfigureAwait(false) is { } message)
                if (!Receive(client, message)) break;
        }
        catch (Exception exception) when (exception is IOException or JsonException or OperationCanceledException or ObjectDisposedException)
        {
            if (!token.IsCancellationRequested) Log.Diagnostic("BROWSER", $"ConnectionEnded Error={exception.GetType().Name}");
        }
        finally
        {
            lock (_gate) { client.Connected = false; client.Disconnected = _clock.GetUtcNow(); }
            lifetime.Cancel();
            client.Outgoing.Writer.TryComplete();
            client.Stream?.Dispose();
            await write.ConfigureAwait(false);
            // A disconnected client remains briefly for reconnection grace, then is removed.
            try { await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            _clients.TryRemove(client.Connection, out _);
        }
    }

    private static async Task WriteOutgoingAsync(Client client, CancellationToken token)
    {
        try
        {
            await foreach (var message in client.Outgoing.Reader.ReadAllAsync(token).ConfigureAwait(false))
                await BrowserMessageProtocol.WriteAsync(client.Stream!, message, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        { client.Stream?.Dispose(); }
    }

    internal bool Receive(Client client, JsonElement message, bool requireHello = false)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (!BrowserMessageProtocol.TryReadEnvelope(message, out var type, out var payload)) return false;
        lock (_gate)
        {
            if (requireHello && type != "hello") return false;
            if (type == "hello")
            {
                if (!ReadText(payload, "clientId", 64, out var id) ||
                    !ReadText(payload, "browser", 16, out var browser) || browser is not ("Chrome" or "Edge")) return false;
                client.Id = browser + ":" + id;
                client.Browser = browser;
                client.Connected = true;
                client.Seen = _clock.GetUtcNow();
                foreach (var previous in _clients.Values.Where(item => item != client && item.Id == client.Id))
                { previous.Sessions.Clear(); previous.Connected = false; previous.Disconnected = DateTimeOffset.MinValue; previous.Stream?.Dispose(); }
                _clients[client.Connection] = client;
                SendConfiguration(client);
                Log.Diagnostic("BROWSER", $"Connected Browser={browser}");
                return true;
            }
            if (!client.Connected) return false;
            client.Seen = _clock.GetUtcNow();
            foreach (var expired in client.Pending.Where(item => _clock.GetUtcNow() - item.Value.Sent > TimeSpan.FromSeconds(4)).Select(item => item.Key).ToArray())
            {
                client.Pending.Remove(expired);
                Log.Diagnostic("BROWSER", "MediaCommandResult Status=unconfirmed Confirmed=False Reason=timeout");
                CommandCompleted?.Invoke(new(expired, "unconfirmed", "timeout", false));
            }
            if (type == "heartbeat") return true;
            if (type == "suggestSite")
            {
                if (!ReadText(payload, "origin", 2048, out var origin) || !BrowserSiteRule.TryCreate(origin, null, out var rule)) return false;
                SiteRequested?.Invoke(rule.Origin);
                return true;
            }
            if (type == "ready")
            {
                if (!payload.TryGetProperty("revision", out var revision) || revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt64(out var value) ||
                    !payload.TryGetProperty("origins", out var origins) || origins.ValueKind != JsonValueKind.Array || origins.GetArrayLength() > 100) return false;
                if (value != _revision) return true;
                client.Revision = value;
                client.Origins = origins.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!).Where(origin => origin.Length <= 2048).ToArray();
                client.Sessions.Clear();
                Log.Diagnostic("BROWSER", $"ConfigurationApplied Browser={client.Browser} Revision={value} GrantedOrigins={client.Origins.Length}");
                return true;
            }
            if (type == "removed")
            {
                if (ReadText(payload, "documentId", 160, out var documentId))
                    foreach (var removedKey in client.Sessions.Where(item => item.Value.Session.DocumentId == documentId).Select(item => item.Key).ToArray())
                        client.Sessions.Remove(removedKey);
                return true;
            }
            if (type == "result")
            {
                if (!ReadText(payload, "requestId", 64, out var requestId) || !client.Pending.Remove(requestId, out var pending) ||
                    _clock.GetUtcNow() - pending.Sent > TimeSpan.FromSeconds(4) || Find(pending.Key) is not { } target) return true;
                var status = ReadText(payload, "status", 32, out var suppliedStatus) && suppliedStatus is "observed" or "unconfirmed" or "rejected"
                    ? suppliedStatus : "unconfirmed";
                var reason = ReadText(payload, "reason", 64, out var suppliedReason, true) && suppliedReason.All(character => character is >= 'a' and <= 'z' or '-')
                    ? suppliedReason : string.Empty;
                var confirmed = status == "observed" && payload.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
                if (target.Details?.TrackVersion != pending.TrackVersion &&
                    !(confirmed && pending.Action is "NextTrack" or "PreviousTrack")) return true;
                Log.Diagnostic("BROWSER", $"MediaCommandResult Status={status} Confirmed={confirmed} Reason={reason}");
                CommandCompleted?.Invoke(new(requestId, status, reason, confirmed));
                return true;
            }
            if (type == "session" && payload.TryGetProperty("revision", out var staleRevision) &&
                staleRevision.ValueKind == JsonValueKind.Number && staleRevision.TryGetInt64(out var oldRevision) && oldRevision != _revision) return true;
            if (type == "session" && client.Revision != _revision) return true;
            if (type != "session" || !ReadText(payload, "documentId", 160, out var document) ||
                !ReadText(payload, "mediaId", 64, out var media) || !ReadText(payload, "url", 2048, out var url) ||
                !ReadText(payload, "title", 512, out var title) || !ReadText(payload, "artist", 512, out var artist, allowEmpty: true) ||
                !ReadText(payload, "album", 512, out var album, allowEmpty: true) ||
                !payload.TryGetProperty("revision", out var reportedRevision) || reportedRevision.ValueKind != JsonValueKind.Number || !reportedRevision.TryGetInt64(out var reportRevision) || reportRevision != _revision ||
                !payload.TryGetProperty("sequence", out var sequence) || sequence.ValueKind != JsonValueKind.Number || !sequence.TryGetInt64(out var serial) || serial < 0 ||
                !payload.TryGetProperty("isPlaying", out var playing) || playing.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !ReadSeconds(payload, "positionSeconds", out var position) || !ReadSeconds(payload, "durationSeconds", out var duration) ||
                !payload.TryGetProperty("canSeek", out var seek) || seek.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) return false;
            // Policy rejection is not a transport failure: a missing site grant must not enable unrestricted SMTC.
            if (!client.Origins.Any(origin => string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) ||
                !_settings.BrowserSites.Any(rule => rule.Enabled && rule.Matches(url))) return true;
            var key = client.Id + ":" + document + ":" + media;
            BrowserMediaDetails? details = null;
            if (payload.TryGetProperty("details", out var detailJson) && !BrowserMediaDetailsParser.TryRead(detailJson, out details)) return true;
            if (details is not null)
            {
                position = details.PositionSeconds ?? 0;
                duration = details.DurationSeconds ?? 0;
            }
            foreach (var expired in client.Sessions.Where(item => _clock.GetUtcNow() - item.Value.Received > TimeSpan.FromSeconds(6))
                .Select(item => item.Key).ToArray())
            {
                var mediaId = client.Sessions[expired].Session.MediaId;
                client.Sessions.Remove(expired);
                Log.Diagnostic("BROWSER", $"SessionExpired Media={mediaId}");
            }
            if (client.Sessions.TryGetValue(key, out var prior) && prior.Session.Sequence >= serial) return true;
            if (prior?.Session.Details is { } previousDetails && details is not null && details.TrackVersion < previousDetails.TrackVersion) return true;
            if (!client.Sessions.ContainsKey(key) && client.Sessions.Count >= 128) return true;
            var session = new BrowserMediaSession(key, client.Id, client.Browser, document, media,
                uri.GetLeftPart(UriPartial.Authority), title, artist, album, playing.GetBoolean(), position,
                duration, details is null ? seek.GetBoolean() : details.Actions.Contains("seekto") &&
                    (details.Seekable.Count > 0 || details.PositionSeconds is not null && details.DurationSeconds > 0), serial)
            { Details = details };
            if (details is not null) session = session with { IsPlaying = details.PlaybackState == "playing" };
            var oldDetails = prior?.Session.Details;
            if (details is not null && (prior is null || oldDetails is null || oldDetails.TrackVersion != details.TrackVersion ||
                oldDetails.Source != details.Source ||
                oldDetails.PositionSeconds.HasValue != details.PositionSeconds.HasValue ||
                details.PositionSeconds is not null && Math.Abs(CurrentSession(prior).PositionSeconds - position) > 2))
                Log.Diagnostic("BROWSER", $"MediaTimeline Source={details.Source} TrackVersion={details.TrackVersion} " +
                    $"TimeKnown={details.PositionSeconds.HasValue} Position={position:F3} Duration={duration:F3} " +
                    $"Rate={details.PlaybackRate:F3} SampleAge={details.SampleAgeSeconds:F3} AnchorAge={details.AnchorAgeSeconds:F3}");
            client.Sessions[key] = new ReceivedSession(session, _clock.GetUtcNow());
            if (prior is null || _clock.GetUtcNow() - client.LastSessionLog >= TimeSpan.FromSeconds(5))
            {
                client.LastSessionLog = _clock.GetUtcNow();
                Log.Diagnostic("BROWSER", $"SessionAccepted Media={media} Sequence={serial} Details={details is not null} Playing={session.IsPlaying}");
            }
            return true;
        }
    }

    private static bool ReadSeconds(JsonElement payload, string key, out double value)
    {
        value = 0;
        return payload.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out value) && double.IsFinite(value) && value is >= 0 and <= 31536000;
    }
    private static bool ReadText(JsonElement payload, string key, int maximum, out string value, bool allowEmpty = false)
    {
        value = string.Empty;
        if (!payload.TryGetProperty(key, out var item) || item.ValueKind != JsonValueKind.String) return false;
        value = item.GetString()!;
        return value.Length <= maximum && (allowEmpty || value.Length > 0) && !value.Any(char.IsControl);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        foreach (var client in _clients.Values) client.Stream?.Dispose();
        _clients.Clear();
        TaskObserver.Observe(CompleteShutdownAsync(), "browser bridge shutdown");
    }

    private async Task CompleteShutdownAsync()
    {
        try { await _listen.ConfigureAwait(false); }
        finally
        {
            // Wait for the accept loop first so no connection can appear after this snapshot.
            try { await Task.WhenAll(_connections.Values).ConfigureAwait(false); }
            finally { _stop.Dispose(); }
        }
    }

    internal sealed class Client
    {
        internal Guid Connection { get; } = Guid.NewGuid();
        internal string Id = string.Empty, Browser = string.Empty;
        internal bool Connected;
        internal long Revision = -1;
        internal DateTimeOffset Seen, Disconnected, LastSessionLog;
        internal string[] Origins = [];
        internal NamedPipeServerStream? Stream;
        internal Channel<JsonElement> Outgoing { get; } = Channel.CreateBounded<JsonElement>(64);
        internal Dictionary<string, ReceivedSession> Sessions { get; } = [];
        internal Dictionary<string, PendingCommand> Pending { get; } = [];
    }
    internal sealed record PendingCommand(string Key, long? TrackVersion, string Action, DateTimeOffset Sent);
    internal sealed record BrowserCommandResult(string RequestId, string Status, string Reason, bool Confirmed);
    internal sealed record ReceivedSession(BrowserMediaSession Session, DateTimeOffset Received);
}
