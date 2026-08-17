namespace TaskbarLyrics.App;

internal enum LyricsPresentationScene
{
    Searching,
    Lyrics,
    Spectrum,
    NoPlayback,
    Message
}

internal static class LyricsPresentationSceneExtensions
{
    public static string ToWireValue(this LyricsPresentationScene scene)
    {
        return scene switch
        {
            LyricsPresentationScene.Searching => "searching",
            LyricsPresentationScene.Lyrics => "lyrics",
            LyricsPresentationScene.Spectrum => "spectrum",
            LyricsPresentationScene.NoPlayback => "noPlayback",
            _ => "message"
        };
    }
}
