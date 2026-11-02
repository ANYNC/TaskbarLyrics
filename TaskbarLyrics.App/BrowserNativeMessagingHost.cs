using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;

namespace TaskbarLyrics.App;

// Runs before WPF/single-instance initialization. stdout belongs exclusively to Chrome/Edge.
internal static class BrowserNativeMessagingHost
{
    internal static async Task RunAsync(string origin)
    {
        try
        {
            if (!BrowserExtensionInstallation.IsAllowedOrigin(origin)) return;
            var browser = ParentBrowser();
            if (browser is null) return;
            using var pipe = new NamedPipeClientStream(".", BrowserSessionBridge.PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var stop = new CancellationTokenSource();
            await pipe.ConnectAsync(3000, stop.Token).ConfigureAwait(false);
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            var incoming = ForwardAsync(input, pipe, browser, stop.Token);
            var outgoing = ForwardAsync(pipe, output, null, stop.Token);
            await Task.WhenAny(incoming, outgoing).ConfigureAwait(false);
            stop.Cancel();
            pipe.Dispose();
            Observe(incoming);
            Observe(outgoing);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or JsonException or
            InvalidOperationException or FormatException or UnauthorizedAccessException or TimeoutException or ArgumentException or System.ComponentModel.Win32Exception)
        { await Console.Error.WriteLineAsync("TaskbarLyrics bridge: " + exception.GetType().Name).ConfigureAwait(false); }
    }

    private static async Task ForwardAsync(Stream input, Stream output, string? browser, CancellationToken token)
    {
        while (await BrowserMessageProtocol.ReadAsync(input, token).ConfigureAwait(false) is { } packet)
        {
            if (!BrowserMessageProtocol.TryReadEnvelope(packet, out var type, out _)) return;
            if (browser is not null && type == "hello")
            {
                var node = JsonNode.Parse(packet.GetRawText())!;
                node["payload"]!["browser"] = browser;
                packet = JsonSerializer.SerializeToElement(node);
            }
            await BrowserMessageProtocol.WriteAsync(output, packet, token).ConfigureAwait(false);
        }
    }

    private static void Observe(Task task) => _ = task.ContinueWith(completed => { _ = completed.Exception; },
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static string? ParentBrowser()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) return null;
        var parents = new Dictionary<uint, (uint Parent, string Executable)>();
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32First(snapshot, ref entry)) return null;
        do { parents[entry.ProcessId] = (entry.ParentProcessId, entry.Executable); }
        while (Process32Next(snapshot, ref entry));
        var processId = (uint)Environment.ProcessId;
        // Chromium may launch native hosts through cmd.exe on Windows.
        for (var depth = 0; depth < 5 && parents.TryGetValue(processId, out var current); depth++)
        {
            processId = current.Parent;
            if (!parents.TryGetValue(processId, out var parent)) return null;
            var executable = Path.GetFileNameWithoutExtension(parent.Executable);
            if (executable.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
            if (executable.Equals("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
            if (!executable.Equals("cmd", StringComparison.OrdinalIgnoreCase)) return null;
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        internal uint Size, Usage, ProcessId;
        internal UIntPtr DefaultHeap;
        internal uint ModuleId, Threads, ParentProcessId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Executable;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint process);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}
