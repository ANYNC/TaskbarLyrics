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

public sealed class AppSettings
{
    public const string BundledFontFamily = "Source Han Sans SC";

    public const string DefaultFontFamily = "Source Han Sans SC, Source Han Sans CN, 思源黑体 CN, Microsoft YaHei UI, Microsoft YaHei";

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

    public bool EnablePureMusicSpectrum { get; set; } = true;

    public bool ShowSpectrumWhenLyricsNotFound { get; set; } = false;

    public double FontSize { get; set; } = 14;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public string FontWeight { get; set; } = DefaultFontWeight;

    public ForegroundColorMode ForegroundColorMode { get; set; } = ForegroundColorMode.Light;

    public string ForegroundColor { get; set; } = LightForegroundColor;

    public bool ShowCover { get; set; } = true;

    public bool ShowBackground { get; set; } = false;

    public double BackgroundOpacity { get; set; } = 0.55;

    public bool ShowBorder { get; set; } = false;

    public bool ShowTextShadow { get; set; } = false;

    public double WindowWidth { get; set; } = 420;

    public LyricsHorizontalAnchor HorizontalAnchor { get; set; } = LyricsHorizontalAnchor.Left;

    public double XOffset { get; set; }

    public double YOffset { get; set; }

    public bool ForceAlwaysOnTop { get; set; } = true;

    // Debug only: show real-time SMTC timeline diagnostics window.
    public bool EnableSmtcTimelineMonitor { get; set; } = false;

    public AppSettings Clone()
    {
        var cloned = (AppSettings)MemberwiseClone();
        cloned.SourceRecognitionOrder = SourceRecognitionOrder.ToList();
        cloned.LocalMusicFolders = LocalMusicFolders.ToList();
        return cloned;
    }
}
