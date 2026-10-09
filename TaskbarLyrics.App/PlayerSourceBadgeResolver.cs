using System.Collections.Concurrent;
using System.IO;

namespace TaskbarLyrics.App;

internal readonly record struct PlayerSourceBadge(string DisplayName, string IconDataUri, bool UsesDefaultIcon);

internal static class PlayerSourceBadgeResolver
{
    private static readonly ConcurrentDictionary<string, string> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string Name, string FileName)> BuiltInSources =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["QQMusic"] = ("QQ 音乐", "QQ音乐.png"),
            ["Netease"] = ("网易云音乐", "网易云音乐.png"),
            ["Kugou"] = ("酷狗音乐", "酷狗音乐.png"),
            ["Spotify"] = ("Spotify", "spotify.png"),
            ["Browser"] = ("浏览器", "Browser.png")
        };

    // 与设置页播放器卡片的两个预设路径保持一致。
    private const string NotePath = "M16 3.3 8 5v10.1a4.7 4.7 0 0 0-2-.4c-2.5 0-4.5 1.5-4.5 3.4s2 3.4 4.5 3.4 4.5-1.5 4.5-3.4V8.2l9-1.9v6.8a4.7 4.7 0 0 0-2-.4c-2.5 0-4.5 1.5-4.5 3.4s2 3.4 4.5 3.4 4.5-1.5 4.5-3.4V2.1L16 3.3Z";
    private const string DiscPath = "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm0 7a3 3 0 1 0 0 6 3 3 0 0 0 0-6Z";

    internal static PlayerSourceBadge Resolve(string? sourceApp, AppSettings settings)
    {
        var source = sourceApp?.Trim() ?? string.Empty;
        if (source.Length == 0)
        {
            return default;
        }

        if (BuiltInSources.TryGetValue(source, out var builtIn))
        {
            return new PlayerSourceBadge(builtIn.Name, LoadAsset(builtIn.FileName), false);
        }

        var custom = settings.CustomPlayerSources.FirstOrDefault(item =>
            string.Equals(item.SourceAppUserModelId, source, StringComparison.OrdinalIgnoreCase));
        if (custom is null)
        {
            return new PlayerSourceBadge(source, LoadAsset("Presets/music-player.svg"), true);
        }

        if (custom.IconDataUrl.Length > 0)
        {
            return new PlayerSourceBadge(custom.DisplayName, custom.IconDataUrl, false);
        }

        if (PresetPlayerIcon.TryNormalize(custom.PresetIconId, custom.PresetIconColor,
                out var presetId, out var presetColor) && presetId.Length > 0)
        {
            return new PlayerSourceBadge(custom.DisplayName, BuildPresetIcon(presetId, presetColor), false);
        }

        return new PlayerSourceBadge(custom.DisplayName, LoadAsset("Presets/music-player.svg"), true);
    }

    private static string BuildPresetIcon(string id, string color) => IconCache.GetOrAdd(
        $"preset:{id}:{color}", _ =>
        {
            var path = id == "disc" ? DiscPath : NotePath;
            var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path fill=\"{color}\" fill-rule=\"evenodd\" d=\"{path}\"/></svg>";
            return "data:image/svg+xml," + Uri.EscapeDataString(svg);
        });

    private static string LoadAsset(string fileName) => IconCache.GetOrAdd(
        $"asset:{fileName}", _ =>
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "PlayerIcons", fileName);
                var bytes = File.ReadAllBytes(path);
                var mime = fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                    ? "image/svg+xml"
                    : "image/png";
                return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return string.Empty;
            }
        });
}
