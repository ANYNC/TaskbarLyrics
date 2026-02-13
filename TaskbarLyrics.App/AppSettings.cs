namespace TaskbarLyrics.App;

public enum LyricsHorizontalAnchor
{
    Left,
    Center,
    Right
}

public sealed class AppSettings
{
    public bool EnableNetease { get; set; } = true;

    public bool EnableQQMusic { get; set; } = true;

    public bool ShowLyricsOnStartup { get; set; } = true;

    public double FontSize { get; set; } = 14;

    public string ForegroundColor { get; set; } = "#FFFFFFFF";

    public bool ShowBackground { get; set; } = false;

    public double BackgroundOpacity { get; set; } = 0.55;

    public bool ShowBorder { get; set; } = false;

    public double WindowWidth { get; set; } = 420;

    public LyricsHorizontalAnchor HorizontalAnchor { get; set; } = LyricsHorizontalAnchor.Left;

    public double XOffset { get; set; }

    public double YOffset { get; set; }

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }
}
