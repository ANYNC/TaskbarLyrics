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

public enum CoverTransitionStyle
{
    SlideLeft,
    Fade,
    None
}

public enum LyricTransitionStyle
{
    Slide,
    Fade,
    CompactSlide,
    None
}

public enum SongProgressDisplayStyle
{
    Off,
    BottomLine,
    LyricUnderline,
    CoverRing,
    CoverBottomBar,
    SpectrumBaseline,
    TimePill,
    Dots,
    BorderRing,
    BackgroundFill
}

public enum SongProgressColorMode
{
    Text = 0,
    CoverAccent = 1,
    White = 2,
    Blue = 3,
    Cyan = 4,
    Green = 5,
    Orange = 6,
    Pink = 7,
    Purple = 8,
    Custom = 9
}

public enum SongProgressAnchor
{
    Left,
    Center,
    Right
}

public enum LyricTranslationLayout
{
    Inline,
    NewLine
}

public enum TextEffectStyle
{
    None,
    Shadow,
    Outline,
    Glow
}

public enum SpectrumColorMode
{
    Text,
    CoverAccent,
    Gradient
}

public enum AnimationIntensity
{
    Reduced,
    Standard,
    Smooth
}

public enum TargetScreenMode
{
    Primary,
    Cursor,
    ScreenIndex
}

public enum DisabledSettingDisplayMode
{
    Hide,
    Disable
}

public enum LyricsBackgroundMaterial
{
    Dim,
    CoverTint,
    Solid
}

public sealed class PlayerVisualProfile
{
    public bool Enabled { get; set; }

    public SongProgressDisplayStyle SongProgressStyle { get; set; } = SongProgressDisplayStyle.Off;

    public SpectrumDisplayStyle SpectrumStyle { get; set; } = SpectrumDisplayStyle.Center;

    public bool ShowLyricTranslation { get; set; }

    public PlayerVisualProfile Clone() => (PlayerVisualProfile)MemberwiseClone();
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

    public bool ShowLyricTranslation { get; set; } = true;

    public LyricTranslationLayout TranslationLayout { get; set; } = LyricTranslationLayout.NewLine;

    public double TranslationFontScale { get; set; } = 1;

    public double TranslationOpacity { get; set; } = 1;

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

    public double FontSize { get; set; } = 21;

    public bool AutoAdjustLineGap { get; set; } = false;

    public double LineGap { get; set; } = 0;

    public double LineGapOffset { get; set; } = -1;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public string FontWeight { get; set; } = DefaultFontWeight;

    public ForegroundColorMode ForegroundColorMode { get; set; } = ForegroundColorMode.Custom;

    public string ForegroundColor { get; set; } = "#FFFF8000";

    public bool AutoForegroundColorByBackground { get; set; } = false;

    public bool UseCoverAccentColor { get; set; } = true;

    public CoverDisplayStyle CoverStyle { get; set; } = CoverDisplayStyle.RoundedSquare;

    public double CoverSize { get; set; } = 34;

    public bool ShowCoverGlow { get; set; } = false;

    public double CoverGlowOpacity { get; set; } = 0.5;

    public CoverLayoutMode CoverLayoutMode { get; set; } = CoverLayoutMode.Inline;

    public CoverTransitionStyle CoverTransitionStyle { get; set; } = CoverTransitionStyle.SlideLeft;

    public double StackedCoverLyricsGap { get; set; } = 4;

    public double StackedCoverXOffset { get; set; }

    public double StackedCoverYOffset { get; set; }

    public bool ShowStackedTrackInfo { get; set; } = true;

    public double StackedTrackInfoGap { get; set; } = 8;

    public double StackedContentXOffset { get; set; }

    public double StackedContentYOffset { get; set; }

    public LyricTransitionStyle TransitionStyle { get; set; } = LyricTransitionStyle.Slide;

    public SongProgressDisplayStyle SongProgressStyle { get; set; } = SongProgressDisplayStyle.CoverRing;

    public double SongProgressThickness { get; set; } = 2;

    public double SongProgressOpacity { get; set; } = 0.9;

    public double SongProgressYOffset { get; set; } = 2;

    public SongProgressColorMode SongProgressColorMode { get; set; } = SongProgressColorMode.Text;

    public string SongProgressColor { get; set; } = "#FFFFFFFF";

    public SpectrumColorMode SpectrumColorMode { get; set; } = SpectrumColorMode.Text;

    public AnimationIntensity AnimationIntensity { get; set; } = AnimationIntensity.Smooth;

    public TextEffectStyle TextEffectStyle { get; set; } = TextEffectStyle.Glow;

    public double TextGlowOpacity { get; set; } = 0.5;

    public bool UseFixedSongProgressWidth { get; set; } = false;

    public double SongProgressWidth { get; set; } = 180;

    public SongProgressAnchor SongProgressAnchor { get; set; } = SongProgressAnchor.Left;

    public DisabledSettingDisplayMode DisabledSettingDisplayMode { get; set; } = DisabledSettingDisplayMode.Hide;

    public bool ShowBackground { get; set; } = false;

    public LyricsBackgroundMaterial BackgroundMaterial { get; set; } = LyricsBackgroundMaterial.CoverTint;

    public double BackgroundOpacity { get; set; } = 0.55;

    public bool ShowBorder { get; set; } = false;

    public bool ShowTextShadow { get; set; } = true;

    public double WindowWidth { get; set; } = 320;

    public bool AutoAdjustWindowWidth { get; set; } = false;

    public double WindowWidthOffset { get; set; }

    public double WindowHeight { get; set; } = 44;

    public bool AutoAdjustWindowHeight { get; set; } = false;

    public double WindowHeightOffset { get; set; }

    public LyricsHorizontalAnchor HorizontalAnchor { get; set; } = LyricsHorizontalAnchor.Left;

    public TargetScreenMode TargetScreenMode { get; set; } = TargetScreenMode.Primary;

    public int TargetScreenIndex { get; set; }

    public double XOffset { get; set; }

    public double YOffset { get; set; } = 0;

    public bool ForceAlwaysOnTop { get; set; } = true;

    public bool EnablePlayerVisualProfiles { get; set; } = false;

    public Dictionary<string, PlayerVisualProfile> PlayerVisualProfiles { get; set; } = CreateDefaultPlayerVisualProfiles();

    // Debug only: show real-time SMTC timeline diagnostics window.
    public bool EnableSmtcTimelineMonitor { get; set; } = false;

    public AppSettings Clone()
    {
        var cloned = (AppSettings)MemberwiseClone();
        cloned.SourceRecognitionOrder = SourceRecognitionOrder?.ToList() ?? new List<string>();
        cloned.LocalMusicFolders = LocalMusicFolders?.ToList() ?? new List<string>();
        cloned.PlayerVisualProfiles = (PlayerVisualProfiles ?? CreateDefaultPlayerVisualProfiles()).ToDictionary(
            entry => entry.Key,
            entry => entry.Value?.Clone() ?? new PlayerVisualProfile(),
            StringComparer.OrdinalIgnoreCase);
        return cloned;
    }

    public static LyricTransitionStyle NormalizeTransitionStyle(LyricTransitionStyle style) =>
        style == LyricTransitionStyle.None ? LyricTransitionStyle.None : LyricTransitionStyle.Slide;

    public static SongProgressDisplayStyle NormalizeSongProgressStyle(SongProgressDisplayStyle style) =>
        style is SongProgressDisplayStyle.LyricUnderline or SongProgressDisplayStyle.SpectrumBaseline
            ? SongProgressDisplayStyle.BottomLine
            : Enum.IsDefined(style)
                ? style
                : SongProgressDisplayStyle.Off;

    private static Dictionary<string, PlayerVisualProfile> CreateDefaultPlayerVisualProfiles() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["QQMusic"] = new(),
        ["Netease"] = new(),
        ["Kugou"] = new(),
        ["Spotify"] = new()
    };
}
