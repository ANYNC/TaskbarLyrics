namespace TaskbarLyrics.Light.App;

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

public enum SpectrumDisplayStyle
{
    Center,
    Bottom,
    Mirror,
    Thin,
    Dots,
    Pulse
}

public enum LocalLyricsSearchMode
{
    PreferLocal,
    OnlineFallback
}

public enum LocalCoverSearchMode
{
    OnlineFirst,
    LocalFirst,
    OnlineOnly,
    LocalOnly
}

public enum CoverDisplayStyle
{
    RoundedSquare,
    Circle,
    Hidden,
    Large,
    Square
}

public enum CoverLayoutMode
{
    Inline,
    Stacked
}

public enum LyricTransitionStyle
{
    Slide,
    Fade,
    CompactSlide,
    None
}

public enum LyricsBackgroundMaterial
{
    Dim,
    CoverTint,
    Solid
}

public sealed class AppSettings
{
    public const string BundledFontFamily = "Source Han Sans SC";

    public const string DefaultFontFamily = BundledFontFamily;

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

    public bool ShowLyricTranslation { get; set; } = false;

    public LocalLyricsSearchMode LocalLyricsSearchMode { get; set; } = LocalLyricsSearchMode.PreferLocal;

    public LocalCoverSearchMode LocalCoverSearchMode { get; set; } = LocalCoverSearchMode.OnlineFirst;

    public List<string> LocalMusicFolders { get; set; } = new();

    public bool ShowCoverImage { get; set; } = true;

    public bool ShowLyricsOnStartup { get; set; } = true;

    public bool StartWithWindows { get; set; } = true;

    public bool AutoShowLyricsWhenPlayerOpens { get; set; } = true;

    public bool AutoHideLyricsWhenPlayerCloses { get; set; } = true;

    public bool AutoCheckUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public string LastNotifiedUpdateVersion { get; set; } = "";

    public bool EnableSpectrum { get; set; } = true;

    public bool EnablePureMusicSpectrum { get; set; } = true;

    public bool ShowSpectrumWhenLyricsNotFound { get; set; } = true;

    public bool ShowSpectrumWhenLyricsAvailable { get; set; } = false;

    public SpectrumDisplayStyle SpectrumStyle { get; set; } = SpectrumDisplayStyle.Center;

    public int LyricOffsetMs { get; set; }

    public int QqMusicLyricOffsetMs { get; set; }

    public int NeteaseLyricOffsetMs { get; set; }

    public int KugouLyricOffsetMs { get; set; }

    public int SpotifyLyricOffsetMs { get; set; }

    public double FontSize { get; set; } = 14;

    public bool AutoAdjustLineGap { get; set; } = true;

    public double LineGap { get; set; } = 2;

    public double LineGapOffset { get; set; } = -1;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public string FontWeight { get; set; } = DefaultFontWeight;

    public ForegroundColorMode ForegroundColorMode { get; set; } = ForegroundColorMode.Custom;

    public string ForegroundColor { get; set; } = "#FFFF8000";

    public bool UseCoverAccentColor { get; set; } = true;

    public CoverDisplayStyle CoverStyle { get; set; } = CoverDisplayStyle.RoundedSquare;

    public double CoverSize { get; set; } = 34;

    public CoverLayoutMode CoverLayoutMode { get; set; } = CoverLayoutMode.Inline;

    public double StackedCoverLyricsGap { get; set; } = 4;

    public double StackedCoverXOffset { get; set; }

    public double StackedCoverYOffset { get; set; }

    public bool ShowStackedTrackInfo { get; set; } = true;

    public double StackedTrackInfoGap { get; set; } = 8;

    public double StackedContentXOffset { get; set; }

    public double StackedContentYOffset { get; set; }

    public LyricTransitionStyle TransitionStyle { get; set; } = LyricTransitionStyle.Slide;

    public bool ShowBackground { get; set; } = false;

    public LyricsBackgroundMaterial BackgroundMaterial { get; set; } = LyricsBackgroundMaterial.CoverTint;

    public double BackgroundOpacity { get; set; } = 0.55;

    public bool ShowBorder { get; set; } = false;

    public bool ShowTextShadow { get; set; } = true;

    public double WindowWidth { get; set; } = 420;

    public bool AutoAdjustWindowWidth { get; set; } = true;

    public double WindowWidthOffset { get; set; }

    public double WindowHeight { get; set; } = 44;

    public bool AutoAdjustWindowHeight { get; set; } = true;

    public double WindowHeightOffset { get; set; }

    public LyricsHorizontalAnchor HorizontalAnchor { get; set; } = LyricsHorizontalAnchor.Left;

    public double XOffset { get; set; }

    public double YOffset { get; set; } = -40;

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
