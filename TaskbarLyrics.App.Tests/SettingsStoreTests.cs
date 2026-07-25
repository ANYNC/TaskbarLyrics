using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void Save_ReplacesTheSettingsFileWithoutLeavingTemporaryFiles()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            var store = new SettingsStore(filePath);
            var settings = new AppSettings
            {
                FontSize = 28,
                ForegroundColor = "#FF336699"
            };

            store.Save(settings);

            var loaded = store.Load();
            Assert.Equal(28, loaded.FontSize);
            Assert.Equal("#FF336699", loaded.ForegroundColor);
            Assert.Empty(Directory.EnumerateFiles(directory, ".settings.json.*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
