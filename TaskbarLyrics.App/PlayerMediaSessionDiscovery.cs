using System.IO;
using Windows.Media.Control;

namespace TaskbarLyrics.App;

internal sealed record AvailablePlayerSession(
    string SourceAppUserModelId,
    string DisplayName,
    string TrackTitle,
    bool IsPlaying,
    bool IsBuiltIn);

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
            result.Add(new AvailablePlayerSession(
                id,
                name,
                title,
                session.GetPlaybackInfo()?.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                isBuiltIn));
        }

        return result.OrderByDescending(session => session.IsPlaying)
            .ThenBy(session => session.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
