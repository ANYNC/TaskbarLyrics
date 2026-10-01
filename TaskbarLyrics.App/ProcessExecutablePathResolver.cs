using System.Diagnostics;
using System.IO;

namespace TaskbarLyrics.App;

internal static class ProcessExecutablePathResolver
{
    public static string? TryResolveByProcessName(string processName)
    {
        foreach (var process in EnumerateProcesses(() => Process.GetProcessesByName(processName)))
        {
            using (process)
            {
                if (TryGetExecutablePath(process) is { } path)
                {
                    return path;
                }
            }
        }

        return null;
    }

    public static string? TryResolveByProcessId(int processId) => QueryImagePath(processId);

    public static string? TryResolveByWindowTitle(string userModelId)
    {
        foreach (var process in EnumerateProcesses(Process.GetProcesses))
        {
            using (process)
            {
                if (!WindowTitleMatches(userModelId, GetWindowTitle(process)))
                {
                    continue;
                }

                if (TryGetExecutablePath(process) is { } path)
                {
                    return path;
                }
            }
        }

        return null;
    }

    internal static string? GetProcessNameCandidate(string? userModelId)
    {
        var value = userModelId?.Trim();
        if (string.IsNullOrEmpty(value) || !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(value);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    internal static bool WindowTitleMatches(string? userModelId, string? windowTitle)
    {
        var value = userModelId?.Trim();
        var title = windowTitle?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length < 2 || string.IsNullOrEmpty(title))
        {
            return false;
        }

        return title.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            title.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static Process[] EnumerateProcesses(Func<Process[]> enumerate)
    {
        try
        {
            return enumerate();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string GetWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string? TryGetExecutablePath(Process process) =>
        QueryImagePath(process.Id) ?? QueryMainModulePath(process);

    private static string? QueryImagePath(int processId)
    {
        try
        {
            var handle = NativeIconInterop.OpenProcess(NativeIconInterop.ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var buffer = new char[1024];
                var length = buffer.Length;
                return NativeIconInterop.QueryFullProcessImageName(handle, 0, buffer, ref length) && length > 0
                    ? new string(buffer, 0, length)
                    : null;
            }
            finally
            {
                NativeIconInterop.CloseHandle(handle);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? QueryMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
