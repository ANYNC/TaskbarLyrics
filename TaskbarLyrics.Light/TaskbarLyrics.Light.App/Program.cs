using System.Runtime.InteropServices;

namespace TaskbarLyrics.Light.App;

internal static class Program
{
    private static readonly IntPtr PerMonitorV2AwarenessContext = new(-4);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [STAThread]
    public static void Main()
    {
        SetProcessDpiAwarenessContext(PerMonitorV2AwarenessContext);

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
