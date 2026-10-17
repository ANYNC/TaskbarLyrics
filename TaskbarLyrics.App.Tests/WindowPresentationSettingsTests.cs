using System.IO;
using System.Text.Json;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class WindowPresentationSettingsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExistingScaleAndControlPreferenceSeedFloatingOnlyWhenFieldsAreAbsent(bool floating, bool explicitFields)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"mode-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, $"{{\"UseFloatingWindow\":{floating.ToString().ToLowerInvariant()},\"LyricsLayoutScalePercent\":125,\"EnableControlPanel\":false" +
                (explicitFields ? ",\"FloatingLyricsLayoutScalePercent\":160,\"EnableFloatingControlPanel\":true}" : "}"));
            var store = new SettingsStore(path);
            var loaded = store.Load();
            Assert.Equal(125, loaded.LyricsLayoutScalePercent);
            Assert.False(loaded.EnableControlPanel);
            Assert.Equal(explicitFields ? 160 : 125, loaded.FloatingLyricsLayoutScalePercent);
            Assert.Equal(explicitFields, loaded.EnableFloatingControlPanel);
            loaded.LyricsLayoutScalePercent = 75;
            loaded.EnableControlPanel = true;
            Assert.True(store.Save(loaded));
            var reloaded = store.Load();
            Assert.Equal(75, reloaded.LyricsLayoutScalePercent);
            Assert.Equal(explicitFields ? 160 : 125, reloaded.FloatingLyricsLayoutScalePercent);
            Assert.Equal(explicitFields, reloaded.EnableFloatingControlPanel);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetRestoresOnlyTheRequestedModeAndKeepsSharedAppearance(bool floating)
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = !floating,
            WindowWidth = 620,
            FloatingWindowWidth = 800,
            LyricsLayoutScalePercent = 75,
            FloatingLyricsLayoutScalePercent = 160,
            EnableControlPanel = false,
            EnableFloatingControlPanel = false,
            ForceAlwaysOnTop = false,
            HorizontalAnchor = LyricsHorizontalAnchor.Right,
            XOffset = -42,
            YOffset = 2,
            ShowBackground = true,
            BackgroundOpacity = 0.8,
            ShowBorder = true,
            FloatingPanelBlurAmount = 8,
            FloatingPanelBrightness = 0.2,
            FloatingPanelTintOpacity = 0.3,
            FontSize = 20,
            CoverSize = 44,
            CoverGap = 12,
            CoverCornerRadius = 9,
            ShowCover = false,
            ShowTextShadow = true,
            LyricsDisplayMode = LyricsDisplayMode.Selected,
            SelectedDisplayIds = ["monitor-A"]
        };
        var before = JsonSerializer.SerializeToElement(settings);
        var defaults = JsonSerializer.SerializeToElement(new AppSettings());
        string[] resetKeys = floating
            ? ["FloatingWindowWidth", "FloatingLyricsLayoutScalePercent", "EnableFloatingControlPanel", "ForceAlwaysOnTop",
                "FloatingPanelBlurAmount", "FloatingPanelBrightness", "FloatingPanelTintOpacity"]
            : ["WindowWidth", "LyricsLayoutScalePercent", "EnableControlPanel", "HorizontalAnchor", "XOffset", "YOffset",
                "ShowBackground", "BackgroundOpacity", "ShowBorder"];
        settings.ResetWindowPresentation(floating);
        foreach (var property in JsonSerializer.SerializeToElement(settings).EnumerateObject())
        {
            var expected = resetKeys.Contains(property.Name) ? defaults : before;
            Assert.Equal(expected.GetProperty(property.Name).GetRawText(), property.Value.GetRawText());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnlyActiveScaleAndPlaybackPreferenceApplyToLyrics(bool floating, bool editFloating)
    {
        var current = new AppSettings { UseFloatingWindow = floating };
        var next = current.Clone();
        if (editFloating)
        {
            next.FloatingLyricsLayoutScalePercent = 150;
            next.EnableFloatingControlPanel = false;
        }
        else
        {
            next.LyricsLayoutScalePercent = 75;
            next.EnableControlPanel = false;
        }
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.Equal(floating == editFloating, changes.RequiresLyricsWindowApply);
        Assert.Equal(floating == editFloating, changes.LyricsLayoutChanged);
        Assert.Equal(floating == editFloating ? (editFloating ? 150 : 75) : 100,
            LyricsLayoutMetrics.Create(next).ScalePercent);
    }

    [Theory]
    [InlineData("\"embedded\"", true, false)]
    [InlineData("\"floating\"", true, true)]
    [InlineData("\"all\"", false, false)]
    [InlineData("{}", false, false)]
    [InlineData("null", false, false)]
    [InlineData("true", false, false)]
    public void ResetMessageValidatesModeBeforeTouchingSettings(string payload, bool valid, bool floating)
    {
        var message = SettingsWebMessageRouter.Parse($"{{\"version\":1,\"type\":\"resetWindowPresentation\",\"payload\":{payload}}}");
        Assert.NotNull(message);
        Assert.Equal(valid, SettingsWebMessageRouter.TryParseWindowPresentation(message.Value, out var actual));
        Assert.Equal(floating, actual);
    }

    [Fact]
    public void EnteringEmbeddedModeMeasuresItsScaleWithoutChangingFloatingProfile()
    {
        var settings = new AppSettings { UseFloatingWindow = true, FloatingLyricsLayoutScalePercent = 300 };
        var result = TaskbarEmbeddingLayoutPolicy.NormalizeForEmbedding(settings, new TaskbarEmbeddingConstraints(1000, 48));
        Assert.True(result.CanEmbed);
        Assert.False(result.Changed);
        Assert.Equal(100, settings.LyricsLayoutScalePercent);
        Assert.Equal(300, settings.FloatingLyricsLayoutScalePercent);
    }
}
