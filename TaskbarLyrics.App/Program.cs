using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal static class Program
{
    private static readonly IntPtr PerMonitorV2AwarenessContext = new(-4);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("chrome-extension://", StringComparison.Ordinal))
        {
            BrowserNativeMessagingHost.RunAsync(args[0]).GetAwaiter().GetResult();
            return;
        }
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
