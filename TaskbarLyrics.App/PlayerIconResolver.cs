using System.Collections.Concurrent;

namespace TaskbarLyrics.App;

internal static class PlayerIconResolver
{
    private const string ShellAppsFolderPrefix = "shell:AppsFolder\\";

    private static readonly ConcurrentDictionary<string, string> ResolvedIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<AppsFolderCatalog> RegisteredApps = new(AppsFolderCatalog.Load);

    public static string ResolveDataUrl(string? userModelId)
    {
        var value = userModelId?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (ResolvedIcons.TryGetValue(value, out var cached))
        {
            return cached;
        }

        var dataUrl = TryResolveDataUrl(value);
        if (dataUrl.Length > 0)
        {
            ResolvedIcons[value] = dataUrl;
            return dataUrl;
        }

        return TryResolveActiveAudioSessionIcon() ?? string.Empty;
    }

    internal static bool IsPackagedUserModelId(string? userModelId) => userModelId?.Contains('!') == true;

    internal static string ToShellItemPath(string userModelId) => ShellAppsFolderPrefix + userModelId.Trim();

    private static string TryResolveDataUrl(string userModelId) =>
        TryResolvePackagedIcon(userModelId)
        ?? TryResolveRunningProcessIcon(userModelId)
        ?? TryResolveRegisteredAppIcon(userModelId)
        ?? TryResolveWindowTitleIcon(userModelId)
        ?? string.Empty;

    private static string? TryResolveActiveAudioSessionIcon()
    {
        if (AudioSessionProcessResolver.TryResolveSingleActiveSessionExecutablePath() is not { } executablePath)
        {
            return null;
        }

        return AppIconExtractor.ExtractExecutablePng(executablePath) is { } png ? CustomPlayerIcon.ToPngDataUrl(png) : null;
    }

    private static string? TryResolvePackagedIcon(string userModelId)
    {
        if (!IsPackagedUserModelId(userModelId))
        {
            return null;
        }

        return AppIconExtractor.ExtractShellItemPng(ToShellItemPath(userModelId)) is { } png ? CustomPlayerIcon.ToPngDataUrl(png) : null;
    }

    private static string? TryResolveRunningProcessIcon(string userModelId)
    {
        if (ProcessExecutablePathResolver.GetProcessNameCandidate(userModelId) is not { } processName ||
            ProcessExecutablePathResolver.TryResolveByProcessName(processName) is not { } executablePath)
        {
            return null;
        }

        return AppIconExtractor.ExtractExecutablePng(executablePath) is { } png ? CustomPlayerIcon.ToPngDataUrl(png) : null;
    }

    private static string? TryResolveRegisteredAppIcon(string userModelId)
    {
        if (RegisteredApps.Value.TryResolveUserModelId(userModelId) is not { } registeredUserModelId)
        {
            return null;
        }

        return AppIconExtractor.ExtractShellItemPng(ToShellItemPath(registeredUserModelId)) is { } png ? CustomPlayerIcon.ToPngDataUrl(png) : null;
    }

    private static string? TryResolveWindowTitleIcon(string userModelId)
    {
        if (ProcessExecutablePathResolver.TryResolveByWindowTitle(userModelId) is not { } executablePath)
        {
            return null;
        }

        return AppIconExtractor.ExtractExecutablePng(executablePath) is { } png ? CustomPlayerIcon.ToPngDataUrl(png) : null;
    }
}
