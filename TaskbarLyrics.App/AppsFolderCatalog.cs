using System.Collections;
using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal sealed record RegisteredAppEntry(string DisplayName, string UserModelId);

internal sealed class AppsFolderCatalog
{
    private readonly IReadOnlyList<RegisteredAppEntry> _entries;

    internal AppsFolderCatalog(IReadOnlyList<RegisteredAppEntry> entries)
    {
        _entries = entries;
    }

    public static AppsFolderCatalog Load()
    {
        try
        {
            return new AppsFolderCatalog(EnumerateEntries());
        }
        catch (Exception)
        {
            return new AppsFolderCatalog([]);
        }
    }

    public string? TryResolveUserModelId(string? userModelIdOrDisplayName)
    {
        var value = userModelIdOrDisplayName?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        foreach (var entry in _entries)
        {
            if (string.Equals(entry.UserModelId, value, StringComparison.OrdinalIgnoreCase))
            {
                return entry.UserModelId;
            }
        }

        foreach (var entry in _entries)
        {
            if (string.Equals(entry.DisplayName, value, StringComparison.OrdinalIgnoreCase))
            {
                return entry.UserModelId;
            }
        }

        return null;
    }

    private static List<RegisteredAppEntry> EnumerateEntries()
    {
        var entries = new List<RegisteredAppEntry>();
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return entries;
        }

        var shell = Activator.CreateInstance(shellType);
        if (shell is null)
        {
            return entries;
        }

        try
        {
            var folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            if (folder is null || ((dynamic)folder).Items() is not IEnumerable items)
            {
                return entries;
            }

            foreach (var item in items)
            {
                if (item is not null && TryReadEntry(item) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }
        finally
        {
            if (Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }

        return entries;
    }

    private static RegisteredAppEntry? TryReadEntry(object item)
    {
        try
        {
            var displayName = ((dynamic)item).Name as string;
            var userModelId = ((dynamic)item).Path as string;
            return string.IsNullOrEmpty(userModelId) ? null : new RegisteredAppEntry(displayName ?? string.Empty, userModelId);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
