namespace TaskbarLyrics.App;

internal static class PresetPlayerIcon
{
    public const string DefaultId = "note";
    public const string DefaultColor = "#EAB308";

    public static bool TryNormalize(string? id, string? color, out string normalizedId, out string normalizedColor)
    {
        normalizedId = string.Empty;
        normalizedColor = string.Empty;
        if (string.IsNullOrEmpty(id)) return string.IsNullOrEmpty(color);
        if (id is not ("note" or "disc") || color is null || color.Length != 7 || color[0] != '#' ||
            !color.AsSpan(1).ToArray().All(Uri.IsHexDigit)) return false;

        normalizedId = id;
        normalizedColor = color.ToUpperInvariant();
        return true;
    }
}
