using System.IO;
using System.Diagnostics;
using System.Drawing.Imaging;
using Windows.ApplicationModel;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TaskbarLyrics.App;

internal sealed record AvailablePlayerSession(
    string SourceAppUserModelId,
    string DisplayName,
    string TrackTitle,
    bool IsPlaying,
    bool IsBuiltIn,
    string IconDataUrl);

internal static class PlayerMediaSessionDiscovery
{
    public static async Task<IReadOnlyList<AvailablePlayerSession>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager
            .RequestAsync().AsTask(cancellationToken);
        var result = new List<AvailablePlayerSession>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in manager.GetSessions())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = session.SourceAppUserModelId?.Trim();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl) || !seen.Add(id) ||
                SmtcMusicSessionProvider.IsBlockedSystemSource(id)) continue;

            string title;
            try
            {
                var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
                title = media?.Title?.Trim() ?? string.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                title = string.Empty;
            }

            var normalized = SmtcMusicSessionProvider.NormalizeSource(id);
            var isBuiltIn = normalized is "QQMusic" or "Netease" or "Kugou" or "Spotify";
            var name = id.Contains('!') ? id[(id.LastIndexOf('!') + 1)..] : id;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name);
            }
            if (name.Length > 80) name = name[..80];
            var iconDataUrl = isBuiltIn ? string.Empty : await TryReadAppIconAsync(id, cancellationToken);
            result.Add(new AvailablePlayerSession(
                id,
                name,
                title,
                session.GetPlaybackInfo()?.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                isBuiltIn,
                iconDataUrl));
        }

        return result.OrderByDescending(session => session.IsPlaying)
            .ThenBy(session => session.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static async Task<string> TryReadAppIconAsync(string appUserModelId, CancellationToken cancellationToken)
    {
        try
        {
            var logo = AppInfo.GetFromAppUserModelId(appUserModelId).DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
            using var stream = await logo.OpenReadAsync().AsTask(cancellationToken);
            if (stream.Size is > 0 and <= 128 * 1024 &&
                stream.ContentType is "image/png" or "image/jpeg" or "image/webp")
            {
                using var input = stream.GetInputStreamAt(0);
                using var reader = new DataReader(input);
                var length = (uint)stream.Size;
                if (await reader.LoadAsync(length).AsTask(cancellationToken) == length)
                {
                    var bytes = new byte[length];
                    reader.ReadBytes(bytes);
                    var candidate = $"data:{stream.ContentType};base64,{Convert.ToBase64String(bytes)}";
                    if (CustomPlayerIcon.TryNormalize(candidate, out var normalized)) return normalized;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Unregistered desktop AppUserModelIds have no AppInfo logo.
        }

        return await Task.Run(() => TryReadExecutableIcon(appUserModelId), cancellationToken);
    }

    private static string TryReadExecutableIcon(string appUserModelId)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception)
        {
            return string.Empty;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!MatchesProcessName(appUserModelId, process.ProcessName)) continue;
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path)) continue;
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon is null) continue;
                    using var bitmap = icon.ToBitmap();
                    using var output = new MemoryStream();
                    bitmap.Save(output, ImageFormat.Png);
                    var candidate = $"data:image/png;base64,{Convert.ToBase64String(output.ToArray())}";
                    if (CustomPlayerIcon.TryNormalize(candidate, out var normalized)) return normalized;
                }
                catch (Exception)
                {
                    // A protected or exiting process can deny its executable path.
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        return string.Empty;
    }

    internal static bool MatchesProcessName(string appUserModelId, string processName)
    {
        var isExecutableId = appUserModelId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        var segments = appUserModelId.Split('.');
        if (!isExecutableId && segments.Length < 3) return false;
        var candidate = isExecutableId
            ? Path.GetFileNameWithoutExtension(appUserModelId)
            : string.Concat(segments.Skip(1));
        var normalizedCandidate = new string(candidate.Where(char.IsLetterOrDigit).ToArray());
        var normalizedProcessName = new string(processName.Where(char.IsLetterOrDigit).ToArray());
        return normalizedCandidate.Length >= 6 &&
            string.Equals(normalizedCandidate, normalizedProcessName, StringComparison.OrdinalIgnoreCase);
    }
}
