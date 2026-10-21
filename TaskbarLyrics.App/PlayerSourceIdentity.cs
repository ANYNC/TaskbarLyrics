namespace TaskbarLyrics.App;

internal static class PlayerSourceIdentity
{
    internal static IReadOnlyList<string> BuiltInOrder { get; } =
        ["QQMusic", "Netease", "Kugou", "Spotify", "AppleMusic", "SodaMusic", "Foobar2000", "Browser"];

    internal static string? NormalizeAdditional(string? source) => source?.Trim().ToLowerInvariant() switch
    {
        "applemusic" or "appleinc.applemusicwin_nzyj5cx40ttqa!app" => "AppleMusic",
        "sodamusic" or "汽水音乐" or "com.soda.music" => "SodaMusic",
        "foobar2000" or "foobar2000.exe" => "Foobar2000",
        _ => null
    };

    internal static List<string> NormalizeOrder(IEnumerable<string>? order, IEnumerable<string> customSources)
    {
        var custom = customSources.ToArray();
        var overridden = custom.Select(NormalizeAdditional).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = BuiltInOrder.Where(source => !overridden.Contains(source)).Concat(custom).ToArray();
        var result = new List<string>();
        foreach (var source in order ?? [])
        {
            var match = known.FirstOrDefault(item => string.Equals(item, source, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !result.Contains(match, StringComparer.OrdinalIgnoreCase)) result.Add(match);
        }
        foreach (var source in known)
        {
            if (result.Contains(source, StringComparer.OrdinalIgnoreCase)) continue;
            var browser = result.IndexOf("Browser");
            if (browser >= 0 && BuiltInOrder.Contains(source) && source != "Browser") result.Insert(browser, source);
            else result.Add(source);
        }
        return result;
    }
}
