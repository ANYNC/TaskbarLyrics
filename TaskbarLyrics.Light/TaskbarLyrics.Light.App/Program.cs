namespace TaskbarLyrics.Light.App;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        if (!SingleInstanceService.TryClaimCurrentProcess())
        {
            Environment.Exit(0);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
