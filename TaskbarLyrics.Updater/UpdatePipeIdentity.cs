using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TaskbarLyrics.Updates;

public static class UpdatePipeIdentity
{
    public static void RequireClient(NamedPipeServerStream pipe, int expectedPid)
    {
        if (!OperatingSystem.IsWindows() || !GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid != expectedPid)
            throw new IOException("Unexpected update pipe client.", new Win32Exception(Marshal.GetLastWin32Error()));
    }
    public static void RequireServer(NamedPipeClientStream pipe, int expectedPid)
    {
        if (!OperatingSystem.IsWindows() || !GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) || pid != expectedPid)
            throw new IOException("Unexpected update pipe server.", new Win32Exception(Marshal.GetLastWin32Error()));
    }
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
