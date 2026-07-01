using System.Diagnostics;
using Microsoft.Win32;

namespace TaskbarLyrics.Light.App;

internal static class StartupRunRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskbarLyrics.Light";

    public static void Apply(bool enabled)
    {
        var exePath = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? ResolveExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        return Process.GetCurrentProcess().MainModule?.FileName;
    }
}
