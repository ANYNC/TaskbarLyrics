using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarLyrics.Light.App;

internal static class UpdateChecker
{
    public const string RepositoryUrl = "https://github.com/sorawithcat/TaskbarLyrics";
    public const string ReleasesUrl = "https://github.com/sorawithcat/TaskbarLyrics/releases/latest";

    private const string LatestReleaseApiUrl = "https://api.github.com/repos/sorawithcat/TaskbarLyrics/releases/latest";
    private static readonly HttpClient HttpClient = new();

    public static async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd("TaskbarLyrics.Light");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
        var latestVersion = NormalizeVersionTag(release?.TagName);
        var currentVersion = NormalizeVersionTag(GetCurrentVersion());

        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            return new UpdateCheckResult(
                UpdateCheckState.Error,
                "",
                GetCurrentVersion(),
                ReleasesUrl,
                false);
        }

        var hasUpdate = IsVersionGreater(latestVersion, currentVersion);
        return new UpdateCheckResult(
            hasUpdate ? UpdateCheckState.Available : UpdateCheckState.Latest,
            release?.TagName ?? latestVersion,
            GetCurrentVersion(),
            string.IsNullOrWhiteSpace(release?.HtmlUrl) ? ReleasesUrl : release.HtmlUrl,
            hasUpdate);
    }

    public static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return (version ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0")
            .Split('+')[0];
    }

    private static string NormalizeVersionTag(string? version)
    {
        return (version ?? "")
            .Trim()
            .TrimStart('v', 'V');
    }

    private static bool IsVersionGreater(string latestVersion, string currentVersion)
    {
        return Version.TryParse(latestVersion, out var latest) &&
            Version.TryParse(currentVersion, out var current)
            ? latest > current
            : string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}

internal enum UpdateCheckState
{
    Available,
    Latest,
    Error
}

internal sealed record UpdateCheckResult(
    UpdateCheckState State,
    string Version,
    string CurrentVersion,
    string Url,
    bool HasUpdate);
