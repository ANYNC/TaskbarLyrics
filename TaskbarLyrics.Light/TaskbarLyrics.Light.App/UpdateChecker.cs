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

        return BuildVersionResult(
            latestVersion,
            release?.TagName,
            release?.HtmlUrl,
            SelectUpdateAsset(release?.Assets, latestVersion));
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

    private static UpdateCheckResult BuildVersionResult(
        string latestVersion,
        string? displayVersion,
        string? url,
        UpdateReleaseAsset? asset = null)
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
            string.Empty,
            hasUpdate ? asset : null);
    }

    private static UpdateCheckResult BuildErrorResult(string message)
    {
        return new UpdateCheckResult(
            UpdateCheckState.Error,
            string.Empty,
            GetCurrentVersion(),
            ReleasesUrl,
            false,
            message,
            null);
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

    private static UpdateReleaseAsset? SelectUpdateAsset(IReadOnlyList<GitHubReleaseAsset>? assets, string latestVersion)
    {
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        return assets
            .Where(IsSupportedLightPackageAsset)
            .Select(asset => new
            {
                Asset = new UpdateReleaseAsset(
                    asset.Name ?? string.Empty,
                    asset.BrowserDownloadUrl ?? string.Empty,
                    asset.Size,
                    asset.ContentType ?? string.Empty),
                Score = ScoreAssetName(asset.Name ?? string.Empty, latestVersion)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Asset.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Asset)
            .FirstOrDefault();
    }

    private static bool IsSupportedLightPackageAsset(GitHubReleaseAsset asset)
    {
        var name = asset.Name ?? string.Empty;
        var downloadUrl = asset.BrowserDownloadUrl ?? string.Empty;
        var contentType = asset.ContentType ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
        {
            return false;
        }

        return IsLightAssetName(name) && IsZipLikeAsset(name, downloadUrl, contentType);
    }

    private static bool IsLightAssetName(string name)
    {
        return Regex.IsMatch(
                name,
                @"(^|[._\-\s])light([._\-\s]|v?\d|$)",
                RegexOptions.IgnoreCase) ||
            name.StartsWith("TaskbarLyrics_light_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZipLikeAsset(string name, string downloadUrl, string contentType)
    {
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            downloadUrl.Contains(".zip", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("zip", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(
                name,
                @"^TaskbarLyrics[_\-. ]light[_\-. ]v?\d+(?:\.\d+){1,3}$",
                RegexOptions.IgnoreCase);
    }

    private static int ScoreAssetName(string name, string latestVersion)
    {
        var score = 0;
        if (Regex.IsMatch(
                name,
                @"^TaskbarLyrics[_\-. ]light[_\-. ]v?\d+(?:\.\d+){1,3}(?:\.zip)?$",
                RegexOptions.IgnoreCase))
        {
            score += 180;
        }

        if (name.Contains("TaskbarLyrics_light", StringComparison.OrdinalIgnoreCase))
        {
            score += 140;
        }

        if (name.Contains("TaskbarLyrics.Light", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TaskbarLyrics-Light", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(latestVersion) &&
            (name.Contains($"v{latestVersion}", StringComparison.OrdinalIgnoreCase) ||
                name.Contains(latestVersion, StringComparison.OrdinalIgnoreCase)))
        {
            score += 40;
        }

        if (name.Contains("win", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (name.Contains("x64", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (name.Contains("portable", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        return score;
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

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset>? Assets { get; set; }
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }
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
    string Message,
    UpdateReleaseAsset? Asset = null);

internal sealed record UpdateReleaseAsset(
    string Name,
    string DownloadUrl,
    long Size,
    string ContentType);
