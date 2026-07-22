namespace TaskbarLyrics.App;

public enum LyricsHorizontalAnchor
{
    Left,
    Center,
    Right
}

public enum ForegroundColorMode
{
    Dark,
    Light,
    Custom
}

public enum SpectrumDisplayMode
{
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

public sealed class AppSettings
{
    public const int MinimumPlayerLyricOffsetMilliseconds = -5000;
    public const int MaximumPlayerLyricOffsetMilliseconds = 5000;
    public const double SafeFontSizeMin = 10;
    public const double SafeFontSizeMax = 24;
    public const double ExtendedFontSizeMin = 6;
    public const double ExtendedFontSizeMax = 96;
    public const double DefaultCoverSize = 34;
    public const double SafeCoverSizeMin = 20;
    public const double SafeCoverSizeMax = 40;
    public const double ExtendedCoverSizeMin = 12;
    public const double ExtendedCoverSizeMax = 200;
    public const double DefaultCoverGap = 8;
    public const double CoverGapMin = 0;
    public const double CoverGapMax = 240;
    public const double DefaultCoverCornerRadius = 6;

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

    public bool StartWithWindows { get; set; } = false;

    public bool AutoCheckUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public string LastNotifiedUpdateVersion { get; set; } = "";

    public bool ShowLyricTranslation { get; set; } = false;

    public ToolWindowTheme ToolWindowTheme { get; set; } = ToolWindowTheme.System;

    public bool EnableSpectrum { get; set; } = true;

    public SpectrumDisplayMode SpectrumDisplayMode { get; set; } = SpectrumDisplayMode.PureMusicOrNoLyrics;

    public bool EnablePureMusicSpectrum { get; set; } = true;

    public bool ShowSpectrumWhenLyricsNotFound { get; set; } = false;

    public SpectrumTuningSettings SpectrumTuning { get; set; } = SpectrumTuningSettings.CreateDefault();

    public bool UseSafeFontSizeRange { get; set; } = true;

    public double FontSize { get; set; } = 14;

    public bool UseSafeCoverSizeRange { get; set; } = true;

    public double CoverSize { get; set; } = DefaultCoverSize;

    public double CoverGap { get; set; } = DefaultCoverGap;

    public double CoverCornerRadius { get; set; } = DefaultCoverCornerRadius;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public string FontWeight { get; set; } = DefaultFontWeight;

    public ForegroundColorMode ForegroundColorMode { get; set; } = ForegroundColorMode.Light;

    public string ForegroundColor { get; set; } = LightForegroundColor;

    public bool ShowBackground { get; set; } = false;

    public double BackgroundOpacity { get; set; } = 0.55;

    public bool ShowBorder { get; set; } = false;

    public bool ShowTextShadow { get; set; } = false;

    public double WindowWidth { get; set; } = 420;

    public LyricsHorizontalAnchor HorizontalAnchor { get; set; } = LyricsHorizontalAnchor.Left;

    public double XOffset { get; set; }

    public double YOffset { get; set; }

    public bool ForceAlwaysOnTop { get; set; } = true;

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
        var cloned = (AppSettings)MemberwiseClone();
        cloned.SourceRecognitionOrder = SourceRecognitionOrder.ToList();
        cloned.PlayerSources = PlayerSources.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        cloned.LocalMusicFolders = LocalMusicFolders.ToList();
        cloned.SpectrumTuning = SpectrumTuning.Clone();
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
            "QQMusic" => 350,
            "Netease" => 100,
            "Kugou" => 100,
            "Spotify" => 300,
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

    public static double ClampFontSize(double value, bool useSafeRange)
    {
        return useSafeRange
            ? Math.Clamp(value, SafeFontSizeMin, SafeFontSizeMax)
            : Math.Clamp(value, ExtendedFontSizeMin, ExtendedFontSizeMax);
    }

    public static double ClampCoverSize(double value, bool useSafeRange)
    {
        return useSafeRange
            ? Math.Clamp(value, SafeCoverSizeMin, SafeCoverSizeMax)
            : Math.Clamp(value, ExtendedCoverSizeMin, ExtendedCoverSizeMax);
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
}

public sealed class PlayerSourceSettings
{
    public int LyricOffsetMilliseconds { get; set; }

    public PlayerSourceSettings Clone()
    {
        return (PlayerSourceSettings)MemberwiseClone();
    }
}
