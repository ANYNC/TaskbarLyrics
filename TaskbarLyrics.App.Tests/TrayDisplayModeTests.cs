using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WpfMedia = System.Windows.Media;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrayDisplayModeOpensInsideWorkAreaWithoutTakingFocus(bool floating)
    {
        RunSta(() =>
        {
            var input = new TextBox();
            var foreground = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Content = input };
            var menu = CreateModeMenu(floating, _ => { });
            try
            {
                foreground.Show();
                foreground.Activate();
                input.Focus();
                PumpMessages();
                Assert.True(foreground.IsActive);
                Assert.Same(input, Keyboard.FocusedElement);
                var deactivations = 0;
                var menuActivations = 0;
                foreground.Deactivated += (_, _) => deactivations++;
                menu.Activated += (_, _) => menuActivations++;
                var work = DisplayMonitorService.GetDisplays().First(display => display.IsPrimary).WorkArea;
                menu.ShowAt(new System.Drawing.Point(work.Right - 8, work.Bottom - 8));
                PumpMessages();
                Assert.True(foreground.IsActive);
                Assert.False(menu.IsActive);
                Assert.Equal(0, deactivations);
                Assert.Equal(0, menuActivations);
                Assert.Same(input, Keyboard.FocusedElement);
                Assert.True(((RadioButton)menu.FindName(floating ? "FloatingModeButton" : "TaskbarModeButton")).IsChecked);
                var bounds = FloatingLyricsPanelController.GetBounds(menu);
                Assert.True(bounds.Left >= work.Left && bounds.Right <= work.Right);
                Assert.True(bounds.Top >= work.Top && bounds.Bottom <= work.Bottom);
            }
            finally { menu.Close(); foreground.Close(); }
        });
    }

    [Fact]
    public void TrayDisplayModeSupportsAccessibleSelectionWithoutRunningTheCommandTwice()
    {
        RunSta(() =>
        {
            var changes = new List<bool>();
            var menu = CreateModeMenu(false, changes.Add);
            try
            {
                var floating = (RadioButton)menu.FindName("FloatingModeButton");
                var peer = new RadioButtonAutomationPeer(floating);
                var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));
                selection.Select();
                floating.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(Assert.Single(changes));
            }
            finally { menu.Close(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TrayDisplayModeFitsEqualSegmentsAndUsesTheCurrentTheme(bool light, bool floating)
    {
        RunSta(() =>
        {
            NativeWindowTheme.SetMode(light ? ToolWindowTheme.Light : ToolWindowTheme.Dark);
            var menu = CreateModeMenu(floating, _ => { });
            try
            {
                var content = (FrameworkElement)menu.Content;
                content.Measure(new System.Windows.Size(menu.Width, menu.Height));
                content.Arrange(new Rect(0, 0, menu.Width, menu.Height));
                content.UpdateLayout();
                var taskbar = (RadioButton)menu.FindName("TaskbarModeButton");
                var floatingButton = (RadioButton)menu.FindName("FloatingModeButton");
                Assert.Equal(taskbar.ActualWidth, floatingButton.ActualWidth);
                Assert.InRange(taskbar.ActualWidth, 90, 100);
                Assert.True(taskbar.ActualHeight >= 28);
                Assert.True(taskbar.DesiredSize.Width <= taskbar.ActualWidth);
                var selected = floating ? floatingButton : taskbar;
                var background = Assert.IsType<WpfMedia.SolidColorBrush>(selected.Background);
                Assert.Equal(light, background.Color.R > 200);
                if (Environment.GetEnvironmentVariable("TASKBARLYRICS_TRAY_PREVIEW") is { Length: > 0 } previewDirectory)
                {
                    Directory.CreateDirectory(previewDirectory);
                    var bitmap = new RenderTargetBitmap((int)menu.Width, (int)menu.Height, 96, 96, WpfMedia.PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(previewDirectory, $"tray-{(light ? "light" : "dark")}-{(floating ? "floating" : "taskbar")}.png"));
                    encoder.Save(stream);
                }
            }
            finally { menu.Close(); NativeWindowTheme.SetMode(ToolWindowTheme.System); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TrayDisplayModeShowsCurrentSelectionAndCommitsOnlyChangedMode(bool current, bool selected)
    {
        RunSta(() =>
        {
            var changes = new List<bool>();
            var closed = false;
            var menu = CreateModeMenu(current, value => { Assert.True(closed); changes.Add(value); });
            menu.Closed += (_, _) => closed = true;
            try
            {
                var taskbar = (RadioButton)menu.FindName("TaskbarModeButton");
                var floating = (RadioButton)menu.FindName("FloatingModeButton");
                Assert.Equal(!current, taskbar.IsChecked);
                Assert.Equal(current, floating.IsChecked);
                Assert.Empty(changes);
                (selected ? floating : taskbar).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(closed);
                if (current == selected) Assert.Empty(changes);
                else Assert.Equal(selected, Assert.Single(changes));
            }
            finally { menu.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrayDisplayModeKeyboardConfirmsWithEnterAndCancelsWithEscape(bool cancel)
    {
        RunSta(() =>
        {
            var changes = new List<bool>();
            var menu = CreateModeMenu(false, changes.Add);
            try
            {
                var work = DisplayMonitorService.GetDisplays().First(display => display.IsPrimary).WorkArea;
                menu.ShowAt(new System.Drawing.Point(work.Right - 8, work.Bottom - 8));
                var source = HwndSource.FromHwnd(new WindowInteropHelper(menu).EnsureHandle());
                var taskbar = (RadioButton)menu.FindName("TaskbarModeButton");
                var floating = (RadioButton)menu.FindName("FloatingModeButton");
                menu.Activate();
                taskbar.Focus();
                var move = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Right) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                taskbar.RaiseEvent(move);
                Assert.True(move.Handled);
                Assert.Same(floating, Keyboard.FocusedElement);
                Assert.True(taskbar.IsChecked);
                Assert.Empty(changes);
                var commit = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, cancel ? Key.Escape : Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                floating.RaiseEvent(commit);
                Assert.True(commit.Handled);
                if (cancel) Assert.Empty(changes);
                else Assert.True(Assert.Single(changes));
            }
            finally { menu.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrayDisplayModeClonesSettingsAndPreservesBothModesPreferences(bool floating)
    {
        var current = new AppSettings
        {
            UseFloatingWindow = !floating,
            WindowWidth = 620,
            FloatingWindowWidth = 810,
            LyricsLayoutScalePercent = 110,
            FloatingLyricsLayoutScalePercent = 140,
            ShowLyricTranslation = true,
            LyricsDisplayMode = LyricsDisplayMode.Selected,
            SelectedDisplayIds = ["display-A"]
        };
        var before = JsonSerializer.SerializeToElement(current);
        var next = App.CreateDisplayModeSettings(current, floating, new TaskbarEmbeddingConstraints(1920, 96), out var message);
        Assert.Equal(floating, next.UseFloatingWindow);
        Assert.Empty(message);
        Assert.Equal(before.GetRawText(), JsonSerializer.SerializeToElement(current).GetRawText());
        foreach (var property in JsonSerializer.SerializeToElement(next).EnumerateObject())
        {
            if (property.Name != nameof(AppSettings.UseFloatingWindow))
                Assert.Equal(before.GetProperty(property.Name).GetRawText(), property.Value.GetRawText());
        }
        var directory = Path.Combine(AppContext.BaseDirectory, $"tray-mode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            Assert.True(store.Save(next));
            var restored = store.Load();
            Assert.Equal(floating, restored.UseFloatingWindow);
            Assert.Equal(next.FloatingWindowWidth, restored.FloatingWindowWidth);
            Assert.Equal(next.LyricsLayoutScalePercent, restored.LyricsLayoutScalePercent);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    [Fact]
    public void TrayDisplayModeRetainsFloatingWhenEmbeddingIsUnavailableAndNormalizesOversizedLayouts()
    {
        var settings = new AppSettings { UseFloatingWindow = true, WindowWidth = 2000, LyricsLayoutScalePercent = 500 };
        var rejected = App.CreateDisplayModeSettings(settings, false, new TaskbarEmbeddingConstraints(0, 0), out var error);
        Assert.True(rejected.UseFloatingWindow);
        Assert.NotEmpty(error);
        var constraints = new TaskbarEmbeddingConstraints(1920, 48);
        var normalized = App.CreateDisplayModeSettings(settings, false, constraints, out var message);
        Assert.False(normalized.UseFloatingWindow);
        Assert.NotEmpty(message);
        Assert.True(TaskbarEmbeddingLayoutPolicy.CanFit(normalized, constraints));
        Assert.Equal(2000, settings.WindowWidth);
        Assert.True(settings.UseFloatingWindow);
    }

    private static TrayMenuWindow CreateModeMenu(bool floating, Action<bool> setMode) => new(
        () => { }, "Alt+Shift+D", () => { }, "Alt+Shift+T", () => { }, "Alt+Shift+S",
        _ => { }, SpectrumDisplayMode.Disabled, () => { }, () => { }, () => { }, () => { }, () => { }, setMode, floating);
}
