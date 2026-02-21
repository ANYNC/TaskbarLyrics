namespace TaskbarLyrics.App;

public enum LyricsHorizontalAnchor
{
    Left,
    Center,
    Right
}

public sealed class AppSettings
{
    public List<string> SourceRecognitionOrder { get; set; } = new()
    {
        "QQMusic",
        "Netease",
        "Spotify"
    };

    public bool EnableNetease { get; set; } = true;

    public bool EnableQQMusic { get; set; } = true;

    public bool ShowLyricsOnStartup { get; set; } = true;

    public double FontSize { get; set; } = 14;

    public string FontFamily { get; set; } = "SF Pro Display, SF Pro Text, Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei";

    public string FontWeight { get; set; } = "SemiBold";

    public string ForegroundColor { get; set; } = "#FFFFFFFF";

    public bool ShowBackground { get; set; } = false;

    public double BackgroundOpacity { get; set; } = 0.55;

    public bool ShowBorder { get; set; } = false;

    public double WindowWidth { get; set; } = 420;

    public LyricsHorizontalAnchor HorizontalAnchor { get; set; } = LyricsHorizontalAnchor.Left;

    public double XOffset { get; set; }

    public double YOffset { get; set; }

    // Reserved for future manual mismatch correction workflow.
    public bool EnableLyricMismatchResolver { get; set; } = false;

    // Debug only: show real-time SMTC timeline diagnostics window.
    public bool EnableSmtcTimelineMonitor { get; set; } = false;

    public AppSettings Clone()
    {
        var cloned = (AppSettings)MemberwiseClone();
        cloned.SourceRecognitionOrder = SourceRecognitionOrder.ToList();
        return cloned;
    }
}
