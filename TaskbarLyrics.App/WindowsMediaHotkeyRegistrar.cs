using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal sealed class WindowsMediaHotkeyRegistrar(IntPtr windowHandle) : IMediaHotkeyRegistrar
{
    public bool TryRegister(int id, int modifiers, int virtualKey) =>
        RegisterHotKey(windowHandle, id, modifiers, virtualKey);

    public void Unregister(int id)
    {
        _ = UnregisterHotKey(windowHandle, id);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
