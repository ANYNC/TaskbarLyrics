using System.IO;
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
        var extracted = await Task.Run(() => PlayerIconResolver.ResolveDataUrl(appUserModelId), cancellationToken);
        return extracted.Length > 0 ? extracted : await TryReadPackagedLogoAsync(appUserModelId, cancellationToken);
    }

    private static async Task<string> TryReadPackagedLogoAsync(string appUserModelId, CancellationToken cancellationToken)
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

        return string.Empty;
    }
}
