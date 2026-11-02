namespace TaskbarLyrics.Core.Models;

public sealed record BrowserSiteRule(string Id, string Name, string Origin, bool Enabled = true)
{
    public static bool TryCreate(string? address, string? name, out BrowserSiteRule rule)
    {
        rule = null!;
        var input = address?.Trim() ?? string.Empty;
        if (input.Length is 0 or > 2048 || input.Any(char.IsControl)) return false;
        if (!input.Contains("://", StringComparison.Ordinal)) input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length > 0) return false;
        var label = string.IsNullOrWhiteSpace(name) ? uri.Authority : name.Trim();
        if (label.Length > 80 || label.Any(char.IsControl)) return false;
        rule = new(Guid.NewGuid().ToString("N"), label, CanonicalOrigin(uri));
        return true;
    }

    public bool Matches(string? address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 &&
        string.Equals(CanonicalOrigin(uri), Origin, StringComparison.OrdinalIgnoreCase);

    private static string CanonicalOrigin(Uri uri) =>
        new UriBuilder(uri.Scheme, uri.IdnHost, uri.IsDefaultPort ? -1 : uri.Port).Uri.GetLeftPart(UriPartial.Authority);

    public static List<BrowserSiteRule> Normalize(IEnumerable<BrowserSiteRule>? rules)
    {
        var result = new List<BrowserSiteRule>();
        foreach (var item in (rules ?? []).Take(100))
        {
            if (item is null || !TryCreate(item.Origin, item.Name, out var normalized) ||
                result.Any(existing => string.Equals(existing.Origin, normalized.Origin, StringComparison.OrdinalIgnoreCase))) continue;
            var id = item.Id is { Length: > 0 and <= 64 } && !item.Id.Any(char.IsControl) &&
                result.All(existing => existing.Id != item.Id) ? item.Id : normalized.Id;
            result.Add(normalized with { Id = id, Enabled = item.Enabled });
        }
        return result;
    }
}
