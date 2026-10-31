using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Net;

namespace TaskbarLyrics.App;

internal static class UpdateChecker
{
    public const string RepositoryUrl = "https://github.com/ANYNC/TaskbarLyrics";
    public const string ReleasesUrl = RepositoryUrl + "/releases/latest";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _completedAt;
    private sealed record CachedResponse(string ETag, byte[] Body);
    private static readonly ConcurrentDictionary<string, CachedResponse> Responses = new(StringComparer.Ordinal);
    public static UpdateCheckResult? Current { get; private set; }
    public static event Action<UpdateCheckResult>? Changed;

    public static async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        var requestedAt = DateTimeOffset.UtcNow;
        await Gate.WaitAsync(cancellationToken);
        UpdateCheckResult result;
        try
        {
            if (Current is not null && _completedAt >= requestedAt) return Current;
            var release = await ReadAsync<GitHubRelease>("https://api.github.com/repos/ANYNC/TaskbarLyrics/releases/latest", cancellationToken);
            if (release is null || release.Draft || release.Prerelease || !TryParseVersion(release.TagName, out var latest) ||
                !TryParseVersion(GetCurrentVersion(), out var current))
                throw new JsonException("Invalid stable release version.");
            var url = IsReleaseUrl(release.HtmlUrl) ? release.HtmlUrl! : ReleasesUrl;
            result = new(latest > current ? UpdateCheckState.Available : UpdateCheckState.Latest,
                release.TagName!, GetCurrentVersion(), url, latest > current)
            { Release = release };
            cancellationToken.ThrowIfCancellationRequested();
            Current = result;
            _completedAt = DateTimeOffset.UtcNow;
        }
        finally { Gate.Release(); }
        Changed?.Invoke(result);
        return result;
    }

    internal static async Task<GitHubRelease> ReadReleaseForVersionAsync(string version, CancellationToken cancellationToken = default)
    {
        if (!TryParseVersion(version, out var expected)) throw new InvalidOperationException("Invalid current version.");
        foreach (var tag in new[] { "v" + expected, expected.ToString() })
        {
            GitHubRelease? release;
            try
            {
                release = await ReadAsync<GitHubRelease>(
                    $"https://api.github.com/repos/ANYNC/TaskbarLyrics/releases/tags/{Uri.EscapeDataString(tag)}", cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            if (release is not null && !release.Draft && !release.Prerelease &&
                TryParseVersion(release.TagName, out var actual) && actual == expected)
                return release;
        }

        throw new InvalidOperationException("Current version release is unavailable.");
    }

    internal static async Task<T?> ReadAsync<T>(string url, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        token = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("TaskbarLyrics");
        request.Headers.Accept.ParseAdd("application/vnd.github.full+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        Responses.TryGetValue(url, out var cached);
        if (cached is not null) request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            return JsonSerializer.Deserialize<T>(cached.Body);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (body.Length + read > 2 * 1024 * 1024) throw new JsonException("Release response exceeds limit.");
            await body.WriteAsync(buffer.AsMemory(0, read), token);
        }
        var bytes = body.ToArray();
        var result = JsonSerializer.Deserialize<T>(bytes);
        if (response.Headers.ETag is { } etag)
        {
            if (Responses.Count >= 16) Responses.Clear();
            Responses[url] = new(etag.ToString(), bytes);
        }
        return result;
    }

    public static string GetCurrentVersion() => (Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    internal static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        return tag is { Length: <= 64 } && Regex.IsMatch(tag, @"\A[vV]?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant) &&
            Version.TryParse(tag.TrimStart('v', 'V'), out version!);
    }

    internal static bool IsReleaseUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == "github.com" && uri.IsDefaultPort &&
        uri.AbsolutePath.StartsWith("/ANYNC/TaskbarLyrics/releases/", StringComparison.Ordinal);

    internal sealed class GitHubRelease
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("body_html")] public string? BodyHtml { get; set; }
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
    }
    internal sealed record GitHubAsset
    {
        [JsonPropertyName("id")] public long Id { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("state")] public string State { get; init; } = "";
        [JsonPropertyName("size")] public long Size { get; init; }
        [JsonPropertyName("digest")] public string? Digest { get; init; }
        [JsonPropertyName("browser_download_url")] public string Url { get; init; } = "";
    }
}
internal enum UpdateCheckState { Available, Latest, Error }
internal sealed record UpdateCheckResult(UpdateCheckState State, string Version, string CurrentVersion, string Url, bool HasUpdate)
{
    public UpdateChecker.GitHubRelease? Release { get; init; }
    public Task<UpdateNotes>? NotesTask { get; set; }
}
