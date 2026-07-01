using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TaskbarLyrics.Light.App;

internal static class UpdateChecker
{
    public const string RepositoryUrl = "https://github.com/sorawithcat/TaskbarLyrics";
    public const string ReleasesUrl = "https://github.com/sorawithcat/TaskbarLyrics/releases/latest";

    private const string LatestReleaseApiUrl = "https://api.github.com/repos/sorawithcat/TaskbarLyrics/releases/latest";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = RequestTimeout
    };

    public static async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await CheckLatestFromApiAsync(cancellationToken);
        }
        catch (Exception ex) when (IsRecoverableNetworkException(ex))
        {
            try
            {
                return await CheckLatestFromReleaseRedirectAsync(cancellationToken);
            }
            catch (Exception fallbackEx) when (IsRecoverableNetworkException(fallbackEx))
            {
                return BuildErrorResult($"无法连接到 GitHub 更新服务：{GetFriendlyError(fallbackEx)}");
            }
        }
    }

    private static async Task<UpdateCheckResult> CheckLatestFromApiAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd("TaskbarLyrics-Light");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
        var latestVersion = NormalizeVersionTag(release?.TagName);
        var currentVersion = NormalizeVersionTag(GetCurrentVersion());

        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            return BuildErrorResult("GitHub 返回的版本信息为空。");
        }

        return BuildVersionResult(latestVersion, release?.TagName, release?.HtmlUrl);
    }

    private static async Task<UpdateCheckResult> CheckLatestFromReleaseRedirectAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.UserAgent.ParseAdd("TaskbarLyrics-Light");
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
        var latestVersion = ExtractVersionFromReleaseUrl(finalUrl);
        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            latestVersion = ExtractVersionFromReleaseUrl(html);
        }

        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            return BuildErrorResult("GitHub 发布页可访问，但未能解析最新版本号。");
        }

        return BuildVersionResult(latestVersion, latestVersion, finalUrl);
    }

    private static UpdateCheckResult BuildVersionResult(string latestVersion, string? displayVersion, string? url)
    {
        latestVersion = NormalizeVersionTag(latestVersion);
        var currentVersion = NormalizeVersionTag(GetCurrentVersion());
        var hasUpdate = IsVersionGreater(latestVersion, currentVersion);
        return new UpdateCheckResult(
            hasUpdate ? UpdateCheckState.Available : UpdateCheckState.Latest,
            string.IsNullOrWhiteSpace(displayVersion) ? latestVersion : displayVersion,
            GetCurrentVersion(),
            string.IsNullOrWhiteSpace(url) ? ReleasesUrl : url,
            hasUpdate,
            string.Empty);
    }

    private static UpdateCheckResult BuildErrorResult(string message)
    {
        return new UpdateCheckResult(
            UpdateCheckState.Error,
            string.Empty,
            GetCurrentVersion(),
            ReleasesUrl,
            false,
            message);
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

    private static string ExtractVersionFromReleaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = Regex.Match(value, @"/releases/tag/(?<tag>[^/?#""'\s<>]+)", RegexOptions.IgnoreCase);
        return match.Success ? NormalizeVersionTag(Uri.UnescapeDataString(match.Groups["tag"].Value)) : string.Empty;
    }

    private static bool IsVersionGreater(string latestVersion, string currentVersion)
    {
        return Version.TryParse(latestVersion, out var latest) &&
            Version.TryParse(currentVersion, out var current)
            ? latest > current
            : string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static bool IsRecoverableNetworkException(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException;
    }

    private static string GetFriendlyError(Exception ex)
    {
        return ex switch
        {
            TaskCanceledException => "请求超时",
            HttpRequestException http when !string.IsNullOrWhiteSpace(http.Message) => http.Message,
            JsonException => "响应格式异常",
            NotSupportedException => "响应格式不受支持",
            _ => ex.Message
        };
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
    bool HasUpdate,
    string Message);
