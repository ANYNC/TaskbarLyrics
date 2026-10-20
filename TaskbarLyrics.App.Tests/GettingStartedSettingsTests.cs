using System.IO;
using System.Reflection;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class GettingStartedSettingsTests
{
    [Fact]
    public void OnlyANewSettingsFileRequestsAutomaticGettingStarted()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"getting-started-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            var settings = store.Load();
            Assert.False(settings.HasSeenGettingStarted);
            Assert.True(store.Save(settings));
            Assert.False(store.Load().HasSeenGettingStarted);
            settings.HasSeenGettingStarted = true;
            Assert.True(store.Save(settings));
            Assert.True(store.Load().HasSeenGettingStarted);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"WindowWidth\":620}", true)]
    [InlineData("{\"HasSeenGettingStarted\":true}", true)]
    [InlineData("{\"HasSeenGettingStarted\":false}", false)]
    [InlineData("{invalid", true)]
    public void ExistingSettingsDoNotForceOnboardingUnlessExplicitlyPending(string json, bool expected)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"getting-started-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, json);
            var store = new SettingsStore(path);
            var settings = store.Load();
            Assert.Equal(expected, settings.HasSeenGettingStarted);
            Assert.Equal(expected, settings.Clone().HasSeenGettingStarted);
            Assert.Equal(json, File.ReadAllText(path));
            Assert.True(store.Save(settings));
            Assert.Equal(expected, store.Load().HasSeenGettingStarted);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsCopiesPreserveOnboardingStateWithoutReconfiguringPlayback(bool seen)
    {
        var current = new AppSettings { HasSeenGettingStarted = !seen };
        var next = current.Clone();
        next.HasSeenGettingStarted = seen;
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.False(changes.RequiresLyricsWindowApply);
        Assert.False(changes.GlobalMediaHotkeysChanged);
        var target = current.Clone();
        typeof(SettingsWindow).GetMethod("CopySettings", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [next, target]);
        Assert.Equal(seen, target.HasSeenGettingStarted);
    }
}
