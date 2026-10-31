using TaskbarLyrics.Core.Utilities;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using TaskbarLyrics.Updates;

namespace TaskbarLyrics.App;

internal sealed record UpdateActionSnapshot(string State, double? Progress = null, string? ErrorCode = null, string OperationKind = "upgrade");
internal sealed class AppUpdateService : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _download;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _directory;
    private UpdateCheckResult? _candidate;
    private bool? _supported;
    public bool Supported => _supported ??= IsSupported;
    public UpdateActionSnapshot? Current { get; private set; }
    public event Action? Changed;
    public bool IsBusy => Current?.State is "downloading" or "verifying" or "preparing" or "applying";
    public static bool IsSupported
    {
        get
        {
            try
            {
                return !new Uri(AppContext.BaseDirectory).IsUnc &&
                new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory)!).DriveType is DriveType.Fixed or DriveType.Removable &&
                Path.GetFileName(Environment.ProcessPath) == UpdatePackage.EntryPoint &&
                UpdatePackage.ReadVersion(AppContext.BaseDirectory) == UpdateChecker.GetCurrentVersion() &&
                Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("TaskbarLyrics.Updater.Payload");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return false; }
        }
    }
    internal static UpdateChecker.GitHubAsset? SelectAsset(UpdateCheckResult result)
    {
        if (result.Release?.Assets is null || !UpdateChecker.TryParseVersion(result.Version, out var target) ||
            !UpdateChecker.TryParseVersion(result.CurrentVersion, out var current) || target < current) return null;
        var matches = result.Release.Assets.Where(a => a.Name == $"TaskbarLyrics-{target}.zip").ToArray();
        return matches.Length == 1 && matches[0].State == "uploaded" && matches[0].Id > 0 &&
            matches[0].Size is > 0 and <= UpdatePackage.MaximumPackageSize && UpdatePackage.IsDigest(matches[0].Digest) &&
            IsDownloadUrl(matches[0].Url, true) ? matches[0] : null;
    }
    internal static bool IsDownloadUrl(string url, bool initial)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
        return initial ? uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/ANYNC/TaskbarLyrics/releases/download/", StringComparison.Ordinal)
            : uri.Host is "github.com" or "release-assets.githubusercontent.com";
    }
    private void Publish(UpdateActionSnapshot snapshot) { Current = snapshot; Changed?.Invoke(); }
    public void Cancel() => _download?.Cancel();
    public void ResetForCheck()
    {
        if (IsBusy) return;
        Cleanup();
        Current = null;
    }
    public async Task DownloadAsync(bool reinstall)
    {
        if (!await _gate.WaitAsync(0, _lifetime.Token)) return;
        var kind = reinstall ? "reinstall" : "upgrade";
        try
        {
            if (!IsSupported) throw new InvalidOperationException("Unsupported");
            var candidate = reinstall
                ? await CreateReinstallCandidateAsync(_lifetime.Token)
                : UpdateChecker.Current ?? throw new InvalidOperationException("CandidateUnavailable");
            var asset = SelectAsset(candidate) ?? throw new InvalidOperationException("AssetUnavailable");
            if (reinstall == candidate.HasUpdate) throw new InvalidOperationException("CandidateChanged");
            Cleanup();
            _download = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _download.CancelAfter(TimeSpan.FromMinutes(30));
            var token = _download.Token;
            _directory = Path.Combine(Path.GetTempPath(), "TaskbarLyrics", "Updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var part = Path.Combine(_directory, "package.part");
            Publish(new("downloading", 0, OperationKind: kind));
            await DownloadFileAsync(asset, part, kind, token);
            Publish(new("verifying", OperationKind: kind));
            await using (var input = File.OpenRead(part)) await UpdatePackage.VerifyDigestAsync(input, asset.Digest!, token);
            var version = candidate.Version.TrimStart('v', 'V');
            var verified = Path.Combine(_directory, "verified");
            await Task.Run(() =>
            {
                UpdatePackage.Extract(part, version, verified, token);
                UpdatePackage.ValidateExecutable(Path.Combine(verified, UpdatePackage.EntryPoint), version);
            }, token);
            File.Move(part, Path.Combine(_directory, "package.zip"));
            _candidate = candidate;
            Publish(new("readyToRestart", OperationKind: kind));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) { Publish(new("canceled", OperationKind: kind)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or InvalidOperationException or UnauthorizedAccessException or JsonException)
        { Log.Warn($"Update download failed: {ex}"); Publish(new("error", ErrorCode: "DownloadFailed", OperationKind: kind)); }
        finally
        {
            _download?.Dispose(); _download = null;
            if (Current?.State != "readyToRestart") Cleanup();
            _gate.Release();
        }
    }
    private async Task DownloadFileAsync(UpdateChecker.GitHubAsset asset, string path, string kind, CancellationToken token)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TaskbarLyrics");
        var url = asset.Url;
        HttpResponseMessage? response = null;
        try
        {
            for (var redirect = 0; redirect <= 5; redirect++)
            {
                using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                headersTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
                if ((int)response.StatusCode is < 300 or >= 400) break;
                var location = response.Headers.Location;
                response.Dispose(); response = null;
                if (location is null) throw new HttpRequestException("Invalid redirect.");
                url = new Uri(new Uri(url), location).AbsoluteUri;
                if (!IsDownloadUrl(url, false)) throw new HttpRequestException("Untrusted redirect.");
            }
            if (response is null) throw new HttpRequestException("Too many redirects.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != asset.Size) throw new InvalidDataException("Size mismatch.");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long total = 0;
            var report = Stopwatch.StartNew();
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                idle.CancelAfter(TimeSpan.FromSeconds(60));
                var read = await stream.ReadAsync(buffer, idle.Token);
                if (read == 0) break;
                total += read;
                if (total > asset.Size) throw new InvalidDataException("Size mismatch.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                if (report.ElapsedMilliseconds >= 200) { Publish(new("downloading", total * 100d / asset.Size, OperationKind: kind)); report.Restart(); }
            }
            if (total != asset.Size) throw new InvalidDataException("Size mismatch.");
            await output.FlushAsync(token);
        }
        finally { response?.Dispose(); }
    }
    public async Task ApplyAsync(Func<Task> exit)
    {
        if (!await _gate.WaitAsync(0, _lifetime.Token)) return;
        try
        {
            if (Current?.State != "readyToRestart" || _candidate?.Release is not { } release || _directory is null || !IsSupported) return;
            var kind = Current.OperationKind;
            Publish(new("preparing", OperationKind: kind));
            var latest = kind == "reinstall"
                ? await CreateReinstallCandidateAsync(_lifetime.Token)
                : await UpdateChecker.CheckLatestAsync(_lifetime.Token);
            var asset = SelectAsset(latest);
            if (latest.Release?.Id != release.Id || asset is null || asset != SelectAsset(_candidate)) throw new InvalidOperationException("CandidateChanged");
            var session = Path.GetFileName(_directory);
            var pipeName = "TaskbarLyrics.Update." + session;
            var updater = Path.Combine(_directory, "updater.exe");
            await using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("TaskbarLyrics.Updater.Payload")!)
            await using (var output = File.Create(updater)) await payload.CopyToAsync(output, _lifetime.Token);
            using var process = Process.GetCurrentProcess();
            var request = new UpdateRequest(1, AppContext.BaseDirectory, Path.Combine(_directory, "package.zip"),
                latest.Version.TrimStart('v', 'V'), asset.Digest!, Environment.ProcessId, process.StartTime.ToUniversalTime().Ticks, session, pipeName, kind, Text.T("Update_UpdaterFailed"), Text.T("Update_RecoveryInstructions"));
            var descriptor = Path.Combine(_directory, "request.json");
            await File.WriteAllTextAsync(descriptor, JsonSerializer.Serialize(request, UpdatePackage.JsonOptions), _lifetime.Token);
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _directory };
            start.ArgumentList.Add(descriptor);
            using var child = Process.Start(start) ?? throw new IOException("Updater did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await pipe.WaitForConnectionAsync(timeout.Token);
            UpdatePipeIdentity.RequireClient(pipe, child.Id);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            if (await reader.ReadLineAsync(timeout.Token) != "READY " + session) throw new IOException("Invalid updater handshake.");
            await writer.WriteLineAsync("APPLY " + session);
            Publish(new("applying", OperationKind: kind));
            await exit();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or InvalidOperationException or UnauthorizedAccessException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception or TimeoutException)
        { Log.Warn($"Update handoff failed: {ex}"); Publish(new("error", ErrorCode: "ApplyFailed", OperationKind: Current?.OperationKind ?? "upgrade")); }
        finally { _gate.Release(); }
    }
    private void Cleanup()
    {
        if (_directory is null) return;
        try { Directory.Delete(_directory, true); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { Log.Debug($"Update cache cleanup failed: {ex.GetType().Name}"); }
        _directory = null;
    }

    private static async Task<UpdateCheckResult> CreateReinstallCandidateAsync(CancellationToken cancellationToken)
    {
        var currentVersion = UpdateChecker.GetCurrentVersion();
        var release = await UpdateChecker.ReadReleaseForVersionAsync(currentVersion, cancellationToken);
        var url = UpdateChecker.IsReleaseUrl(release.HtmlUrl) ? release.HtmlUrl! : UpdateChecker.ReleasesUrl;
        return new(UpdateCheckState.Latest, release.TagName!, currentVersion, url, false) { Release = release };
    }
    public void Dispose()
    {
        _lifetime.Cancel();
        if (!IsBusy) Cleanup();
        _lifetime.Dispose();
    }
}
