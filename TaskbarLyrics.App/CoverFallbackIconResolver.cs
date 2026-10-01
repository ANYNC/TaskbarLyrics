namespace TaskbarLyrics.App;

/// <summary>
/// Resolves the app icon shown in the cover slot when no album art is available. Results are
/// cached per source so icon extraction and process lookups do not repeat on the frequent
/// cover update path; failures retry after a bounded interval.
/// </summary>
internal sealed class CoverFallbackIconResolver
{
    internal static readonly TimeSpan FailureRetryInterval = TimeSpan.FromSeconds(60);

    private readonly Func<string, string> _resolveIcon;
    private readonly Func<DateTimeOffset> _utcNow;
    private string _sourceKey = string.Empty;
    private string _iconDataUri = string.Empty;
    private DateTimeOffset _nextLookupUtc;

    public CoverFallbackIconResolver()
        : this(PlayerIconResolver.ResolveDataUrl, () => DateTimeOffset.UtcNow)
    {
    }

    internal CoverFallbackIconResolver(Func<string, string> resolveIcon, Func<DateTimeOffset> utcNow)
    {
        _resolveIcon = resolveIcon ?? throw new ArgumentNullException(nameof(resolveIcon));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public string Resolve(string? sourceAppUserModelId, string? sourceApp)
    {
        var sourceKey = CreateSourceKey(sourceAppUserModelId, sourceApp);
        if (sourceKey.Length == 0)
        {
            return string.Empty;
        }

        var nowUtc = _utcNow();
        if (string.Equals(sourceKey, _sourceKey, StringComparison.OrdinalIgnoreCase) &&
            (_iconDataUri.Length > 0 || nowUtc < _nextLookupUtc))
        {
            return _iconDataUri;
        }

        _sourceKey = sourceKey;
        _iconDataUri = (_resolveIcon(sourceKey) ?? string.Empty).Trim();
        _nextLookupUtc = _iconDataUri.Length > 0
            ? DateTimeOffset.MaxValue
            : nowUtc + FailureRetryInterval;
        return _iconDataUri;
    }

    internal static string CreateSourceKey(string? sourceAppUserModelId, string? sourceApp)
    {
        return !string.IsNullOrWhiteSpace(sourceAppUserModelId)
            ? sourceAppUserModelId.Trim()
            : sourceApp?.Trim() ?? string.Empty;
    }
}
