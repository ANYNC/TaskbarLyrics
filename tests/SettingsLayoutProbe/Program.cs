using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await RunAsync(Path.GetFullPath(args.Single())); }
            catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
            finally { app.Shutdown(); }
        });
        app.Run();
    }

    private static async Task RunAsync(string repository)
    {
        var output = Path.Combine(repository, "tmp", "settings-layout-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        Console.WriteLine($"Evidence: {output}");
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(output, "profile"))
            .WaitAsync(TimeSpan.FromSeconds(30));
        var window = new Window
        {
            Width = 1280, Height = 765, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false, Opacity = 0
        };
        CoreWebView2Controller? controller = null;
        try
        {
            window.Show();
            controller = await environment.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(window).Handle)
                .WaitAsync(TimeSpan.FromSeconds(30));
            var web = controller.CoreWebView2;
            var resources = new YamlDotNet.Serialization.DeserializerBuilder().Build()
                .Deserialize<Dictionary<string, Dictionary<string, string>>>(
                    await File.ReadAllTextAsync(Path.Combine(repository, "TaskbarLyrics.App", "text.yaml")));
            var locale = Environment.GetEnvironmentVariable("TASKBARLYRICS_PROBE_LANGUAGE") == "EN" ? "EN" : "Zh-CN";
            var configuration = JsonSerializer.Serialize(new { language = locale, strings = resources[locale] });
            await web.AddScriptToExecuteOnDocumentCreatedAsync(
                $"window.taskbarLyricsLocale = {configuration};\n" +
                await File.ReadAllTextAsync(Path.Combine(repository, "TaskbarLyrics.App", "text.js")));
            web.SetVirtualHostNameToFolderMapping("settings-layout.test",
                Path.Combine(repository, "TaskbarLyrics.App"), CoreWebView2HostResourceAccessKind.DenyCors);
            var loaded = new TaskCompletionSource();
            web.NavigationCompleted += (_, eventArgs) =>
            {
                if (eventArgs.IsSuccess) loaded.TrySetResult();
                else loaded.TrySetException(new InvalidOperationException(eventArgs.WebErrorStatus.ToString()));
            };
            web.Navigate("https://settings-layout.test/Web/Settings/settings.html");
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await web.ExecuteScriptAsync("window.settingsApp.receive({version:1,type:'settingsState',payload:{settings:{},fonts:[]}});");
            var script = await File.ReadAllTextAsync(Path.Combine(repository, "tests", "SettingsLayoutProbe", "assertions.js"));
            controller.ShouldDetectMonitorScaleChanges = false;
            foreach (var (width, height, textScale) in new[]
            {
                (1280, 765, 1d), (1280, 765, 1.4), (720, 560, 1.4),
                (1280, 765, 2.25), (720, 560, 2.25), (1280, 765, 1d)
            })
            {
                window.Width = width;
                window.Height = height;
                window.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(window).PixelsPerDip;
                controller.Bounds = new System.Drawing.Rectangle(0, 0, (int)Math.Round(width * dpi), (int)Math.Round(height * dpi));
                controller.RasterizationScale = dpi * textScale;
                var result = await MeasureAsync(web, script);
                var label = $"{width}x{height}-{textScale}";
                await File.WriteAllTextAsync(Path.Combine(output, label + ".json"), result.GetRawText());
                Console.WriteLine($"{label}: {result}");
                if (result.GetProperty("failures").GetArrayLength() != 0)
                    throw new InvalidOperationException($"Layout assertions failed: {label}");
            }

            // Prove this test detects the original missing grid/scroll constraints.
            controller.RasterizationScale = VisualTreeHelper.GetDpi(window).PixelsPerDip * 1.4;
            await web.ExecuteScriptAsync("const regressionStyle = document.createElement('style'); regressionStyle.textContent = '.app-shell{grid-template-rows:auto}.sidebar{min-height:auto;grid-template-rows:auto 1fr auto}.nav{overflow:visible;min-height:auto}'; document.head.append(regressionStyle);");
            var regression = await MeasureAsync(web, script);
            await File.WriteAllTextAsync(Path.Combine(output, "regression-control.json"), regression.GetRawText());
            if (regression.GetProperty("failures").GetArrayLength() == 0)
                throw new InvalidOperationException("Regression control unexpectedly passed: probe is not sensitive to clipping.");
            Console.WriteLine("Regression control: original clipping correctly rejected.");
        }
        finally
        {
            controller?.Close();
            window.Close();
        }
    }

    private static async Task<JsonElement> MeasureAsync(CoreWebView2 web, string script)
    {
        var completed = new TaskCompletionSource<JsonElement>();
        void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            if (message.RootElement.TryGetProperty("layoutProbe", out var result))
                completed.TrySetResult(result.Clone());
        }
        web.WebMessageReceived += Receive;
        try
        {
            await web.ExecuteScriptAsync(script);
            return await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { web.WebMessageReceived -= Receive; }
    }
}
