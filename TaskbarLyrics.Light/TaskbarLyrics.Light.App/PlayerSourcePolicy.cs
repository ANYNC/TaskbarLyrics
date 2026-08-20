namespace TaskbarLyrics.Light.App;

internal static class PlayerSourcePolicy
{
    public static string[] BuildEnabledSources(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var sources = new List<string>(4);
        if (settings.EnableQQMusic) sources.Add("QQMusic");
        if (settings.EnableNetease) sources.Add("Netease");
        if (settings.EnableKugou) sources.Add("Kugou");
        if (settings.EnableSpotify) sources.Add("Spotify");
        return sources.ToArray();
    }
}
