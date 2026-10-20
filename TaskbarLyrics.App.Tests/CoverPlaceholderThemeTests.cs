using System.Reflection;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoverPlaceholderUsesTheEffectiveThemeInInitialHtmlAndLiveUpdates(bool light)
    {
        RunSta(() =>
        {
            NativeWindowTheme.SetMode(light ? ToolWindowTheme.Light : ToolWindowTheme.Dark);
            using var composition = new AppCompositionRoot();
            using var offsets = new TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            var commands = new List<LyricsPresentationCommand>();
            main.PresentationCommandCreated += commands.Add;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainWindow).GetField("_isWebViewReady", flags)!.SetValue(main, true);
            typeof(MainWindow).GetField("_isWebDocumentReady", flags)!.SetValue(main, true);
            var theme = light ? "light" : "dark";
            var nextTheme = light ? "dark" : "light";
            try
            {
                var html = MainWindow.GetLyricsWebUiHtml();
                Assert.Contains($"data-cover-theme=\"{theme}\"", html);
                Assert.Contains("class=\"cover-fallback\">TL</span>", html);
                Assert.DoesNotContain("{{COVER_THEME}}", html);
                main.ReplayPresentationState();
                Assert.Contains($"\"coverTheme\":\"{theme}\"", commands.Last(command => command.Slot == "style").Script);
                var cover = commands.Single(command => command.Slot == "cover").Script;
                Assert.Contains("\"fallbackText\":\"TL\"", cover);
                Assert.Contains("\"fallbackColor\":\"\"", cover);

                NativeWindowTheme.SetMode(light ? ToolWindowTheme.Dark : ToolWindowTheme.Light);
                PumpMessages();
                Assert.Contains($"\"coverTheme\":\"{nextTheme}\"", commands.Last(command => command.Slot == "style").Script);
                Assert.Single(commands, command => command.Slot == "cover");
                main.Close();
                var count = commands.Count;
                NativeWindowTheme.SetMode(light ? ToolWindowTheme.Light : ToolWindowTheme.Dark);
                PumpMessages();
                Assert.Equal(count, commands.Count);
            }
            finally { main.Close(); NativeWindowTheme.SetMode(ToolWindowTheme.System); }
        });
    }
}
