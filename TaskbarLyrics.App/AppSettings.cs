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
    public const double MinimumWindowWidth = 320;
    public const double MaximumWindowWidth = 1400;

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
        "Spotify"
    };

    public bool EnableNetease { get; set; } = true;

    public bool EnableQQMusic { get; set; } = true;

    public bool EnableKugou { get; set; } = true;

    public bool EnableSpotify { get; set; } = true;

    public Dictionary<string, PlayerSourceSettings> PlayerSources { get; set; } = CreateDefaultPlayerSources();

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
        var cloned = (AppSettings)MemberwiseClone();
        cloned.SourceRecognitionOrder = SourceRecognitionOrder.ToList();
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
        var current = PlayerSources ?? new Dictionary<string, PlayerSourceSettings>();
        var normalized = CreateDefaultPlayerSources();
        foreach (var source in normalized.Keys.ToList())
        {
            if (current.TryGetValue(source, out var sourceSettings) && sourceSettings is not null)
            {
                normalized[source] = new PlayerSourceSettings
                {
                    LyricOffsetMilliseconds = ClampPlayerLyricOffset(sourceSettings.LyricOffsetMilliseconds)
                };
            }
        }

        PlayerSources = normalized;
    }

    public void NormalizeLyricsLayout()
    {
        FontSize = ClampFontSize(FontSize);
        CoverSize = ClampCoverSize(CoverSize);
        CoverGap = ClampCoverGap(CoverGap);
        CoverCornerRadius = ClampCoverCornerRadius(CoverCornerRadius, CoverSize);
        LyricsLayoutScalePercent = ClampLyricsLayoutScalePercent(LyricsLayoutScalePercent);
        NormalizeLyricsTextAlignment();
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

    public int GetPlayerLyricOffsetMilliseconds(string? sourceApp)
    {
        var source = NormalizePlayerSourceName(sourceApp);
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
        var source = NormalizePlayerSourceName(sourceApp);
        if (source is null)
        {
            return;
        }

        NormalizePlayerSources();
        PlayerSources[source].LyricOffsetMilliseconds = ClampPlayerLyricOffset(value);
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
            ["Spotify"] = new() { LyricOffsetMilliseconds = GetDefaultPlayerLyricOffsetMilliseconds("Spotify") }
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
            _ => null
        };
    }

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
        var scale = ClampLyricsLayoutScalePercent(scalePercent) / 100.0;
        var baseWidth = Math.Clamp(baseWindowWidth, MinimumWindowWidth, MaximumWindowWidth);
        return Math.Clamp(baseWidth * scale, MinimumWindowWidth, maxWidth);
    }
}

public sealed class PlayerSourceSettings
{
    public int LyricOffsetMilliseconds { get; set; }

    public PlayerSourceSettings Clone()
    {
        return (PlayerSourceSettings)MemberwiseClone();
    }
}
