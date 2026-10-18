using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal static class FloatingPanelWindowOrder
{
    internal static bool EnsureAbove(IntPtr window, IntPtr anchor)
    {
        if (window == IntPtr.Zero || anchor == IntPtr.Zero || window == anchor) return false;
        const uint flags = TaskbarNativeMethods.SWP_NOMOVE | TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOACTIVATE;
        var anchorTopmost = (TaskbarNativeMethods.GetWindowLongPtr(anchor, TaskbarNativeMethods.GWL_EXSTYLE).ToInt64() & 8) != 0;
        var windowTopmost = (TaskbarNativeMethods.GetWindowLongPtr(window, TaskbarNativeMethods.GWL_EXSTYLE).ToInt64() & 8) != 0;
        if (anchorTopmost != windowTopmost && !TaskbarNativeMethods.SetWindowPos(window,
            anchorTopmost ? TaskbarNativeMethods.HWND_TOPMOST : TaskbarNativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags)) return false;
        var above = GetWindow(anchor, 3); // GW_HWNDPREV
        if (above == window) return true;
        if (TaskbarNativeMethods.SetWindowPos(window, above, 0, 0, 0, 0, flags)) return true;
        // A protected external predecessor can reject relative insertion (ERROR_ACCESS_DENIED).
        // Move using an absolute band, then order our own HWNDs without that external handle.
        const uint fallbackFlags = flags | TaskbarNativeMethods.SWP_NOOWNERZORDER | TaskbarNativeMethods.SWP_NOSENDCHANGING;
        return TaskbarNativeMethods.SetWindowPos(window,
            anchorTopmost ? TaskbarNativeMethods.HWND_TOPMOST : TaskbarNativeMethods.HWND_BOTTOM,
            0, 0, 0, 0, fallbackFlags) &&
            TaskbarNativeMethods.SetWindowPos(anchor, window, 0, 0, 0, 0, fallbackFlags);
    }

    internal static bool EnsureBelow(IntPtr material, IntPtr lyrics)
    {
        if (material == IntPtr.Zero || lyrics == IntPtr.Zero || material == lyrics) return false;
        const uint flags = TaskbarNativeMethods.SWP_NOMOVE | TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOACTIVATE;
        // Inserting after a topmost HWND does not itself grant WS_EX_TOPMOST to an independent window.
        // Mirror the owner's actual native group, then repair the relative order without changing geometry.
        var lyricsTopmost = (TaskbarNativeMethods.GetWindowLongPtr(lyrics, TaskbarNativeMethods.GWL_EXSTYLE).ToInt64() & 8) != 0;
        var materialTopmost = (TaskbarNativeMethods.GetWindowLongPtr(material, TaskbarNativeMethods.GWL_EXSTYLE).ToInt64() & 8) != 0;
        if (lyricsTopmost != materialTopmost && !TaskbarNativeMethods.SetWindowPos(material,
            lyricsTopmost ? TaskbarNativeMethods.HWND_TOPMOST : TaskbarNativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags)) return false;
        // Hidden IME windows owned by the material must remain above their owner. They do not
        // obscure the card, so requiring direct adjacency would reorder it on every timer tick.
        var below = GetWindow(lyrics, 2); // GW_HWNDNEXT
        while (below != IntPtr.Zero && below != material && !IsWindowVisible(below))
            below = GetWindow(below, 2);
        if (below == material) return true;
        return TaskbarNativeMethods.SetWindowPos(material, lyrics, 0, 0, 0, 0, flags);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
}
