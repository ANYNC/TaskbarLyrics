namespace TaskbarLyrics.App;

internal static class CustomPlayerIcon
{
    private const int MaximumBytes = 128 * 1024;

    public static string Normalize(string? dataUrl) =>
        TryNormalize(dataUrl, out var normalized) ? normalized : string.Empty;

    public static bool TryNormalize(string? dataUrl, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(dataUrl)) return true;
        if (dataUrl.Length > 180_000) return false;

        var separator = dataUrl.IndexOf(',');
        if (separator < 0) return false;
        var header = dataUrl[..separator];
        if (header is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64"))
        {
            return false;
        }

        byte[] image;
        try
        {
            image = Convert.FromBase64String(dataUrl[(separator + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (image.Length == 0 || image.Length > MaximumBytes) return false;
        var valid = header switch
        {
            "data:image/png;base64" => image.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "data:image/jpeg;base64" => image.AsSpan().StartsWith(new byte[] { 255, 216, 255 }),
            "data:image/webp;base64" => image.Length >= 12 && image.AsSpan().StartsWith("RIFF"u8) &&
                                        image.AsSpan(8).StartsWith("WEBP"u8),
            _ => false
        };
        if (!valid) return false;
        normalized = $"{header},{Convert.ToBase64String(image)}";
        return true;
    }
}
