using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

public enum LyricsHorizontalAnchor
{
    Left,
    Center,
    Right
}

public enum LyricsTextAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}

public enum CoverPosition
{
    Left = 0,
    Right = 1
}

public enum ForegroundColorMode
{
    Dark = 0,
    Light = 1,
    Custom = 2,
    System = 3
}

public enum SpectrumDisplayMode
{
    Disabled = -1,
    PureMusicOrNoLyrics,
    PureMusicOnly,
    Always
}

public enum ToolWindowTheme
{
    System,
    Light,
    Dark
}

public enum LyricsDisplayMode
{
    All,
    Selected
}

public sealed class AppSettings
{
    public const int MinimumPlayerLyricOffsetMilliseconds = -5000;
    public const int MaximumPlayerLyricOffsetMilliseconds = 5000;
    public const double ExtendedFontSizeMin = 6;
    public const double ExtendedFontSizeMax = 96;
    public const double DefaultFontSize = 14;
    public const double DefaultCoverSize = 34;
    public const double ExtendedCoverSizeMin = 12;
    public const double ExtendedCoverSizeMax = 200;
    public const double DefaultCoverGap = 8;
    public const double CoverGapMin = 0;
    public const double CoverGapMax = 240;
    public const double DefaultCoverCornerRadius = 6;
    public const double DefaultLyricsLayoutScalePercent = 100;
    public const double MinimumLyricsLayoutScalePercent = 25;
    public const double MaximumLyricsLayoutScalePercent = 300;

    public const double DefaultWindowWidth = 420;
    public const double MinimumWindowWidth = 100;
    public const double MaximumWindowWidth = 1400;

    // The default presentation is embedded in the taskbar. Users can opt into
    // the unconstrained, top-level window through the settings page.
    public bool UseFloatingWindow { get; set; }

    public const double MinimumWindowOffset = -2000;
    public const double MaximumWindowOffset = 2000;

    public const string BundledFontFamily = "Source Han Sans SC";

    public const string DefaultFontFamily = BundledFontFamily;

    private const string LegacyDefaultFontFamily = "Source Han Sans SC, Source Han Sans CN, 思源黑体 CN, Microsoft YaHei UI, Microsoft YaHei";

    public const string DefaultFontWeight = "Bold";

    public const string DarkForegroundColor = "#FF111827";

    public const string LightForegroundColor = "#FFFFFFFF";

    public List<string> SourceRecognitionOrder { get; set; } = new()
    {
        "QQMusic",
        "Netease",
        "Kugou",
        "Spotify",
        "Browser"
    };

    public bool EnableNetease { get; set; } = true;

    public bool EnableQQMusic { get; set; } = true;

    public bool EnableKugou { get; set; } = true;

    public bool EnableSpotify { get; set; } = true;
    public bool EnableBrowser { get; set; }

    public Dictionary<string, PlayerSourceSettings> PlayerSources { get; set; } = CreateDefaultPlayerSources();
    public List<CustomPlayerSource> CustomPlayerSources { get; set; } = [];

    public bool EnableLocalLyrics { get; set; } = true;

    public List<string> LocalMusicFolders { get; set; } = new();

    public bool ShowLyricsOnStartup { get; set; } = true;

    public bool AutoHideWhenNoPlayback { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool AutoCheckUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public string LastNotifiedUpdateVersion { get; set; } = "";

    public bool ShowLyricTranslation { get; set; }

    public bool EnableWordScanning { get; set; } = true;

    public ToolWindowTheme ToolWindowTheme { get; set; } = ToolWindowTheme.System;

    public SpectrumDisplayMode SpectrumDisplayMode { get; set; } = SpectrumDisplayMode.Disabled;

    public bool SpectrumAudioAccessGranted { get; set; }

    public SpectrumTuningSettings SpectrumTuning { get; set; } = SpectrumTuningSettings.CreateDefault();

    // Retained so existing settings.json files continue to round-trip without data loss.
    public bool UseSafeFontSizeRange { get; set; } = true;

    public double FontSize { get; set; } = DefaultFontSize;

    // Retained so existing settings.json files continue to round-trip without data loss.
    public bool UseSafeCoverSizeRange { get; set; } = true;

    public double CoverSize { get; set; } = DefaultCoverSize;

    public double CoverGap { get; set; } = DefaultCoverGap;

    public double CoverCornerRadius { get; set; } = DefaultCoverCornerRadius;

    public bool ShowCover { get; set; } = true;

    public bool EnableControlPanel { get; set; } = true;

    public double LyricsLayoutScalePercent { get; set; } = DefaultLyricsLayoutScalePercent;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public string FontWeight { get; set; } = DefaultFontWeight;

    public ForegroundColorMode ForegroundColorMode { get; set; } = ForegroundColorMode.System;

    public string ForegroundColor { get; set; } = LightForegroundColor;

    public bool ShowBackground { get; set; }

    public double BackgroundOpacity { get; set; } = 0.55;

    public bool ShowBorder { get; set; }

    public bool ShowTextShadow { get; set; }

    public double WindowWidth { get; set; } = DefaultWindowWidth;

    public LyricsHorizontalAnchor HorizontalAnchor { get; set; } = LyricsHorizontalAnchor.Left;

    public LyricsTextAlignment LyricsTextAlignment { get; set; } = LyricsTextAlignment.Left;

    public CoverPosition CoverPosition { get; set; } = CoverPosition.Left;

    public double XOffset { get; set; }

    public double YOffset { get; set; }

    public bool ForceAlwaysOnTop { get; set; } = true;

    public LyricsDisplayMode LyricsDisplayMode { get; set; } = LyricsDisplayMode.All;

    public List<string> SelectedDisplayIds { get; set; } = new();

    public GlobalMediaHotkeySettings GlobalMediaHotkeys { get; set; } = new();

    public static string NormalizeFontFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return DefaultFontFamily;
        }

        var trimmed = fontFamily.Trim();
        if (string.Equals(trimmed, LegacyDefaultFontFamily, StringComparison.OrdinalIgnoreCase))
        {
            return BundledFontFamily;
        }

        var firstFamily = trimmed
            .Split(',', 2, StringSplitOptions.TrimEntries)[0]
            .Trim('"', '\'');
        return string.Equals(firstFamily, BundledFontFamily, StringComparison.OrdinalIgnoreCase)
            ? BundledFontFamily
            : trimmed;
    }

    public AppSettings Clone()
    {
        NormalizePlayerSources();
        NormalizeLyricsTextAlignment();
        NormalizeCoverPosition();
        var cloned = (AppSettings)MemberwiseClone();
        cloned.SourceRecognitionOrder = SourceRecognitionOrder.ToList();
        cloned.CustomPlayerSources = CustomPlayerSources.ToList();
        cloned.PlayerSources = PlayerSources.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        cloned.LocalMusicFolders = LocalMusicFolders.ToList();
        cloned.SelectedDisplayIds = (SelectedDisplayIds ?? []).ToList();
        cloned.SpectrumTuning = SpectrumTuning.Clone();
        cloned.GlobalMediaHotkeys = (GlobalMediaHotkeys ?? new GlobalMediaHotkeySettings()).Clone();
        return cloned;
    }

    public void NormalizePlayerSources()
    {
        CustomPlayerSources = (CustomPlayerSources ?? [])
            .Where(source => source is not null && IsValidCustomSourceId(source.SourceAppUserModelId))
            .Select(source =>
            {
                var icon = CustomPlayerIcon.Normalize(source.IconDataUrl);
                var presetId = string.Empty;
                var presetColor = string.Empty;
                var hasPreset = icon.Length == 0 && PresetPlayerIcon.TryNormalize(
                    source.PresetIconId, source.PresetIconColor, out presetId, out presetColor);
                return new CustomPlayerSource(
                    source.SourceAppUserModelId.Trim(),
                    NormalizeCustomDisplayName(source.DisplayName, source.SourceAppUserModelId),
                    source.Enabled)
                {
                    IconDataUrl = icon,
                    PresetIconId = hasPreset ? presetId : string.Empty,
                    PresetIconColor = hasPreset ? presetColor : string.Empty
                };
            })
            .DistinctBy(source => source.SourceAppUserModelId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var current = PlayerSources ?? new Dictionary<string, PlayerSourceSettings>();
        var normalized = CreateDefaultPlayerSources();
        foreach (var custom in CustomPlayerSources)
        {
            normalized[custom.SourceAppUserModelId] = new PlayerSourceSettings();
        }
        foreach (var source in normalized.Keys.ToList())
        {
            if (current.TryGetValue(source, out var sourceSettings) && sourceSettings is not null)
            {
                normalized[source] = new PlayerSourceSettings
                {
                    LyricOffsetMilliseconds = ClampPlayerLyricOffset(sourceSettings.LyricOffsetMilliseconds),
                    LyricProviders = PlayerSourceSettings.NormalizeLyricProviders(sourceSettings.LyricProviders)
                };
            }
        }

        PlayerSources = normalized;
    }

    public bool AddCustomPlayerSource(string? sourceId, string? displayName, string? iconDataUrl = null,
        string? presetIconId = null, string? presetIconColor = null, bool enabled = true,
        IReadOnlyList<LyricProviderPreference>? lyricProviders = null)
    {
        if (!IsValidCustomSourceId(sourceId)) return false;
        if (!CustomPlayerIcon.TryNormalize(iconDataUrl, out var normalizedIcon) ||
            !PresetPlayerIcon.TryNormalize(presetIconId, presetIconColor, out var normalizedPresetId, out var normalizedPresetColor) ||
            (normalizedIcon.Length > 0 && normalizedPresetId.Length > 0) ||
            (lyricProviders is not null && !PlayerSourceSettings.IsValidLyricProviders(lyricProviders))) return false;
        NormalizePlayerSources();
        var id = sourceId!.Trim();
        if (CustomPlayerSources.Any(source => string.Equals(source.SourceAppUserModelId, id, StringComparison.OrdinalIgnoreCase))) return false;
        CustomPlayerSources.Add(new CustomPlayerSource(id, NormalizeCustomDisplayName(displayName, id), enabled)
        {
            IconDataUrl = normalizedIcon,
            PresetIconId = normalizedPresetId,
            PresetIconColor = normalizedPresetColor
        });
        PlayerSources[id] = new PlayerSourceSettings
        {
            LyricProviders = PlayerSourceSettings.NormalizeLyricProviders(lyricProviders)
        };
        SourceRecognitionOrder.Add(id);
        return true;
    }

    public bool RemoveCustomPlayerSource(string? sourceId)
    {
        var removed = CustomPlayerSources.RemoveAll(source => string.Equals(source.SourceAppUserModelId, sourceId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed) return false;
        PlayerSources.Remove(sourceId!);
        SourceRecognitionOrder.RemoveAll(source => string.Equals(source, sourceId, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public bool UpdateCustomPlayerSource(string? sourceId, string? displayName, string? iconDataUrl,
        string? presetIconId, string? presetIconColor)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(displayName) ||
            displayName.Length > 80 || displayName.Any(char.IsControl) ||
            !CustomPlayerIcon.TryNormalize(iconDataUrl, out var normalizedIcon) ||
            !PresetPlayerIcon.TryNormalize(presetIconId, presetIconColor, out var normalizedPresetId, out var normalizedPresetColor) ||
            (normalizedIcon.Length > 0 && normalizedPresetId.Length > 0)) return false;

        NormalizePlayerSources();
        var index = CustomPlayerSources.FindIndex(source =>
            string.Equals(source.SourceAppUserModelId, sourceId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        CustomPlayerSources[index] = CustomPlayerSources[index] with
        {
            DisplayName = displayName.Trim(),
            IconDataUrl = normalizedIcon,
            PresetIconId = normalizedPresetId,
            PresetIconColor = normalizedPresetColor
        };
        return true;
    }

    public bool SetCustomPlayerSourceEnabled(string? sourceId, bool enabled)
    {
        var index = CustomPlayerSources.FindIndex(source => string.Equals(source.SourceAppUserModelId, sourceId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        CustomPlayerSources[index] = CustomPlayerSources[index] with { Enabled = enabled };
        return true;
    }

    private static bool IsValidCustomSourceId(string? sourceId) =>
        !string.IsNullOrWhiteSpace(sourceId) && sourceId.Length <= 256 &&
        !sourceId.Any(char.IsControl) && NormalizePlayerSourceName(sourceId.Trim()) is null;

    private static string NormalizeCustomDisplayName(string? name, string sourceId)
    {
        var value = name?.Trim();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && !value.Any(char.IsControl)
            ? value
            : sourceId;
    }

    public void NormalizeLyricsLayout()
    {
        FontSize = ClampFontSize(FontSize);
        CoverSize = ClampCoverSize(CoverSize);
        CoverGap = ClampCoverGap(CoverGap);
        CoverCornerRadius = ClampCoverCornerRadius(CoverCornerRadius, CoverSize);
        LyricsLayoutScalePercent = ClampLyricsLayoutScalePercent(LyricsLayoutScalePercent);
        NormalizeLyricsTextAlignment();
        NormalizeCoverPosition();
    }

    public void NormalizeCoverPosition()
    {
        if (!Enum.IsDefined(CoverPosition))
        {
            CoverPosition = CoverPosition.Left;
        }
    }

    public void NormalizeLyricsTextAlignment()
    {
        if (!Enum.IsDefined(LyricsTextAlignment))
        {
            LyricsTextAlignment = LyricsTextAlignment.Left;
        }
    }

    public void NormalizeDisplaySelection()
    {
        if (!Enum.IsDefined(LyricsDisplayMode))
        {
            LyricsDisplayMode = LyricsDisplayMode.All;
        }

        SelectedDisplayIds = (SelectedDisplayIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void NormalizeWindowLayout()
    {
        WindowWidth = double.IsFinite(WindowWidth)
            ? Math.Clamp(WindowWidth, MinimumWindowWidth, MaximumWindowWidth)
            : DefaultWindowWidth;
        XOffset = double.IsFinite(XOffset)
            ? Math.Clamp(XOffset, MinimumWindowOffset, MaximumWindowOffset)
            : 0;
        YOffset = double.IsFinite(YOffset)
            ? Math.Clamp(YOffset, MinimumWindowOffset, MaximumWindowOffset)
            : 0;
    }

    public int GetPlayerLyricOffsetMilliseconds(string? sourceApp)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        if (source is null)
        {
            return 0;
        }

        return PlayerSources is not null &&
            PlayerSources.TryGetValue(source, out var sourceSettings) &&
            sourceSettings is not null
                ? ClampPlayerLyricOffset(sourceSettings.LyricOffsetMilliseconds)
                : GetDefaultPlayerLyricOffsetMilliseconds(source);
    }

    public void SetPlayerLyricOffsetMilliseconds(string? sourceApp, int value)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        if (source is null)
        {
            return;
        }

        NormalizePlayerSources();
        PlayerSources[source].LyricOffsetMilliseconds = ClampPlayerLyricOffset(value);
    }

    public IReadOnlyList<LyricProviderPreference> GetPlayerLyricProviders(string? sourceApp)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        return source is not null && PlayerSources is not null &&
            PlayerSources.TryGetValue(source, out var settings) && settings is not null
                ? PlayerSourceSettings.NormalizeLyricProviders(settings.LyricProviders)
                : PlayerSourceSettings.CreateDefaultLyricProviders();
    }

    public bool SetPlayerLyricProviders(string? sourceApp, IReadOnlyList<LyricProviderPreference>? providers)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        if (source is null || !PlayerSourceSettings.IsValidLyricProviders(providers))
        {
            return false;
        }

        NormalizePlayerSources();
        PlayerSources[source].LyricProviders = providers!.Select(item => item with { }).ToList();
        return true;
    }

    public LyricSourceSelection GetLyricSourceSelection(string? sourceApp)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        var providers = GetPlayerLyricProviders(sourceApp);
        var enabledOrder = providers.Where(item => item.Enabled)
            .Select(item => new LyricProviderId(item.ProviderId)).ToArray();
        var defaults = PlayerSourceSettings.CreateDefaultLyricProviders();
        var isDefault = source is null || providers.SequenceEqual(defaults);
        var context = isDefault ? null : $"{source}:{string.Join(',', providers.Select(item => $"{item.ProviderId}:{(item.Enabled ? 1 : 0)}"))}";
        return new LyricSourceSelection(enabledOrder, context);
    }

    public static int GetDefaultPlayerLyricOffsetMilliseconds(string? sourceApp)
    {
        return NormalizePlayerSourceName(sourceApp) switch
        {
            "QQMusic" => 0,
            "Netease" => 0,
            "Kugou" => 0,
            "Spotify" => 0,
            _ => 0
        };
    }

    public static int ClampPlayerLyricOffset(int value)
    {
        return Math.Clamp(value, MinimumPlayerLyricOffsetMilliseconds, MaximumPlayerLyricOffsetMilliseconds);
    }

    private static Dictionary<string, PlayerSourceSettings> CreateDefaultPlayerSources()
    {
        return new Dictionary<string, PlayerSourceSettings>(StringComparer.OrdinalIgnoreCase)
        {
            ["QQMusic"] = new() { LyricOffsetMilliseconds = GetDefaultPlayerLyricOffsetMilliseconds("QQMusic") },
            ["Netease"] = new() { LyricOffsetMilliseconds = GetDefaultPlayerLyricOffsetMilliseconds("Netease") },
            ["Kugou"] = new() { LyricOffsetMilliseconds = GetDefaultPlayerLyricOffsetMilliseconds("Kugou") },
            ["Spotify"] = new() { LyricOffsetMilliseconds = GetDefaultPlayerLyricOffsetMilliseconds("Spotify") },
            ["Browser"] = new() { LyricOffsetMilliseconds = GetDefaultPlayerLyricOffsetMilliseconds("Browser") }
        };
    }

    private static string? NormalizePlayerSourceName(string? sourceApp)
    {
        return sourceApp?.Trim().ToLowerInvariant() switch
        {
            "qqmusic" => "QQMusic",
            "netease" or "neteasemusic" => "Netease",
            "kugou" => "Kugou",
            "spotify" => "Spotify",
            "browser" => "Browser",
            _ => null
        };
    }

    private string? ResolvePlayerSourceName(string? sourceApp) =>
        NormalizePlayerSourceName(sourceApp) ??
        CustomPlayerSources.FirstOrDefault(source => string.Equals(source.SourceAppUserModelId, sourceApp, StringComparison.OrdinalIgnoreCase))?.SourceAppUserModelId;

    public static double ClampFontSize(double value)
    {
        return Math.Clamp(value, ExtendedFontSizeMin, ExtendedFontSizeMax);
    }

    public static double ClampCoverSize(double value)
    {
        return Math.Clamp(value, ExtendedCoverSizeMin, ExtendedCoverSizeMax);
    }

    public static double ClampCoverGap(double value)
    {
        return Math.Clamp(value, CoverGapMin, CoverGapMax);
    }

    public static double ClampCoverCornerRadius(double value, double coverSize)
    {
        var maxRadius = Math.Max(0, coverSize / 2);
        return Math.Clamp(value, 0, maxRadius);
    }

    public static double ClampLyricsLayoutScalePercent(double value)
    {
        return Math.Clamp(
            value,
            MinimumLyricsLayoutScalePercent,
            MaximumLyricsLayoutScalePercent);
    }

    public static double ClampEffectiveWindowWidth(double baseWindowWidth, double scalePercent, double maxWidth)
    {
        var scale = double.IsFinite(scalePercent)
            ? ClampLyricsLayoutScalePercent(scalePercent) / 100.0
            : DefaultLyricsLayoutScalePercent / 100.0;
        var baseWidth = double.IsFinite(baseWindowWidth)
            ? Math.Clamp(baseWindowWidth, MinimumWindowWidth, MaximumWindowWidth)
            : DefaultWindowWidth;
        if (!double.IsFinite(maxWidth) || maxWidth <= 0)
        {
            maxWidth = MaximumWindowWidth;
        }

        if (maxWidth < MinimumWindowWidth)
        {
            return maxWidth;
        }

        return Math.Clamp(baseWidth * scale, MinimumWindowWidth, maxWidth);
    }

    public static double CalculateEffectiveWindowWidth(double baseWindowWidth, double scalePercent)
    {
        var scale = double.IsFinite(scalePercent)
            ? ClampLyricsLayoutScalePercent(scalePercent) / 100.0
            : DefaultLyricsLayoutScalePercent / 100.0;
        var baseWidth = double.IsFinite(baseWindowWidth)
            ? Math.Clamp(baseWindowWidth, MinimumWindowWidth, MaximumWindowWidth)
            : DefaultWindowWidth;
        return Math.Max(MinimumWindowWidth, baseWidth * scale);
    }

}

public sealed class PlayerSourceSettings
{
    public int LyricOffsetMilliseconds { get; set; }

    public List<LyricProviderPreference> LyricProviders { get; set; } = CreateDefaultLyricProviders();

    public static List<LyricProviderPreference> CreateDefaultLyricProviders() =>
        KnownLyricProviders.OnlineTrustOrder
            .Select(provider => new LyricProviderPreference(provider.Value, true)).ToList();

    public static bool IsValidLyricProviders(IReadOnlyList<LyricProviderPreference>? providers) =>
        providers is not null && providers.Count == KnownLyricProviders.OnlineTrustOrder.Count &&
        providers.All(item => item is not null && KnownLyricProviders.OnlineTrustOrder.Any(
            known => string.Equals(known.Value, item.ProviderId, StringComparison.Ordinal))) &&
        providers.Select(item => item.ProviderId).Distinct(StringComparer.Ordinal).Count() == providers.Count;

    public static List<LyricProviderPreference> NormalizeLyricProviders(IReadOnlyList<LyricProviderPreference>? providers) =>
        IsValidLyricProviders(providers)
            ? providers!.Select(item => item with { }).ToList()
            : CreateDefaultLyricProviders();

    public PlayerSourceSettings Clone()
    {
        return new PlayerSourceSettings
        {
            LyricOffsetMilliseconds = LyricOffsetMilliseconds,
            LyricProviders = NormalizeLyricProviders(LyricProviders)
        };
    }
}

public sealed record LyricProviderPreference(string ProviderId, bool Enabled);

public sealed record CustomPlayerSource(string SourceAppUserModelId, string DisplayName, bool Enabled)
{
    public string IconDataUrl { get; init; } = string.Empty;
    public string PresetIconId { get; init; } = string.Empty;
    public string PresetIconColor { get; init; } = string.Empty;
}
