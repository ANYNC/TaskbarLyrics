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

public sealed class AppSettings
{
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

    public bool EnableLocalLyrics { get; set; } = true;

    public List<string> LocalMusicFolders { get; set; } = new();

    public bool ShowLyricsOnStartup { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;

    public bool AutoCheckUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public string LastNotifiedUpdateVersion { get; set; } = "";

    public bool ShowLyricTranslation { get; set; } = false;

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
        var cloned = (AppSettings)MemberwiseClone();
        cloned.SourceRecognitionOrder = SourceRecognitionOrder.ToList();
        cloned.LocalMusicFolders = LocalMusicFolders.ToList();
        cloned.SpectrumTuning = SpectrumTuning.Clone();
        return cloned;
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
