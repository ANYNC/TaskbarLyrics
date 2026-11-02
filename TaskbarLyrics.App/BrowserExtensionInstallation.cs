using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace TaskbarLyrics.App;

internal static class BrowserExtensionInstallation
{
    internal const string HostName = "com.taskbarlyrics.browser";
    internal static string PackageDirectory => Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
    internal static string DevelopmentId
    {
        get
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(PackageDirectory, "manifest.json")));
            var key = Convert.FromBase64String(manifest.RootElement.GetProperty("key").GetString()!);
            var hash = SHA256.HashData(key);
            return string.Concat(hash.Take(16).SelectMany(value => new[] { (char)('a' + (value >> 4)), (char)('a' + (value & 15)) }));
        }
    }

    internal static string? StoreId(string browser)
    {
        using var distribution = JsonDocument.Parse(File.ReadAllText(Path.Combine(PackageDirectory, "distribution.json")));
        if (!distribution.RootElement.TryGetProperty(browser == "Edge" ? "edge" : "chrome", out var value)) return null;
        var id = value.GetString();
        return id is { Length: 32 } && id.All(character => character is >= 'a' and <= 'p') ? id : null;
    }

    internal static bool IsAllowedOrigin(string origin) => new[] { DevelopmentId, StoreId("Chrome"), StoreId("Edge") }
        .Where(id => id is not null).Any(id => origin == $"chrome-extension://{id}/");

    internal static string Prepare(string browser)
    {
        if (browser is not ("Chrome" or "Edge")) throw new ArgumentException("Unsupported browser.", nameof(browser));
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application path unavailable.");
        if (!Path.GetFileNameWithoutExtension(executable).StartsWith("TaskbarLyrics", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native messaging must be configured by the desktop application.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarLyrics", "BrowserBridge");
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, browser.ToLowerInvariant() + "-host.json");
        var ids = new[] { DevelopmentId, StoreId(browser) }.Where(id => id is not null).Distinct();
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            name = HostName,
            description = "TaskbarLyrics website media bridge",
            path = executable,
            type = "stdio",
            allowed_origins = ids.Select(id => $"chrome-extension://{id}/").ToArray()
        }, BrowserMessageProtocol.Json));
        var vendor = browser == "Edge" ? "Microsoft\\Edge" : "Google\\Chrome";
        using var key = Registry.CurrentUser.CreateSubKey($"Software\\{vendor}\\NativeMessagingHosts\\{HostName}");
        key.SetValue(string.Empty, manifestPath);
        return manifestPath;
    }

    internal static void OpenInstallation(string browser)
    {
        Prepare(browser);
        var id = StoreId(browser);
        var target = id is null ? PackageDirectory : browser == "Edge"
            ? "https://microsoftedge.microsoft.com/addons/detail/" + id
            : "https://chromewebstore.google.com/detail/" + id;
        if (id is null) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        else
        {
            var start = new ProcessStartInfo(browser == "Edge" ? "msedge.exe" : "chrome.exe") { UseShellExecute = true };
            start.ArgumentList.Add(target);
            Process.Start(start);
        }
    }
}
