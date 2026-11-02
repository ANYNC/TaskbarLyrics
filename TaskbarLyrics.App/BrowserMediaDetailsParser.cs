using System.Text.Json;
using System.Text.Json.Serialization;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

internal static class BrowserMediaDetailsParser
{
    private static readonly JsonSerializerOptions StrictJson = new(BrowserMessageProtocol.Json)
    { NumberHandling = JsonNumberHandling.Strict };
    internal static readonly string[] Actions = ["play", "pause", "previoustrack", "nexttrack", "seekbackward", "seekforward", "seekto"];

    internal static bool TryRead(JsonElement json, out BrowserMediaDetails? details)
    {
        details = null;
        if (json.ValueKind != JsonValueKind.Object) return false;
        foreach (var name in new[] { "version", "trackVersion", "playbackRate", "sampleAgeSeconds" })
            if (!json.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Number) return false;
        try
        {
            var value = json.Deserialize<BrowserMediaDetails>(StrictJson);
            if (value is null || value.Version != 1 || value.TrackVersion is < 0 or > 9007199254740991 ||
                value.Source is not ("element" or "media-session" or "associated-element") ||
                value.TitleSource is not ("document" or "media-session") ||
                value.PlaybackState is not ("playing" or "paused" or "ended" or "seeking" or "unknown") ||
                !Seconds(value.PositionSeconds) || !Seconds(value.DurationSeconds) || !Seconds(value.AnchorAgeSeconds) ||
                !double.IsFinite(value.PlaybackRate) || Math.Abs(value.PlaybackRate) > 16 ||
                !double.IsFinite(value.SampleAgeSeconds) || value.SampleAgeSeconds is < 0 or > 6 ||
                value.Actions is null || value.Actions.Count > 7 || value.Actions.Any(action => !Actions.Contains(action)) ||
                value.Seekable is null || value.Seekable.Count > 8 || value.Seekable.Any(range => range is null ||
                    !Seconds(range.Start) || !Seconds(range.End) || range.End <= range.Start) ||
                value.Artwork is null || value.Artwork.Count > 8 || value.Artwork.Any(image => image is null ||
                    !ArtworkAddress(image.Src) || !Text(image.Sizes, 80) || !Text(image.Type, 80))) return false;
            details = value;
            return true;
        }
        catch (JsonException) { return false; }
    }

    internal static bool ArtworkAddress(string? address) => Text(address, 4096) &&
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0;

    private static bool Text(string? value, int maximum) => value is not null && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Seconds(double? value) => value is null || double.IsFinite(value.Value) && value.Value is >= 0 and <= 31536000;
}
