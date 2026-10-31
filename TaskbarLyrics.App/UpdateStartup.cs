using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using TaskbarLyrics.Updates;

namespace TaskbarLyrics.App;

internal static class UpdateStartup
{
    public static bool CanStart(string[] args)
    {
        var root = AppContext.BaseDirectory;
        try
        {
            var legacyDirectory = UpdatePackage.Contained(root, UpdatePackage.LegacyTransactionDirectory);
            var cacheDirectory = UpdatePackage.GetTransactionRoot(root);
            if (HasPendingRecovery(root, legacyDirectory) || HasPendingRecovery(root, cacheDirectory)) return false;
            return true;
        }
        catch (IOException error)
        {
            // Only the session installed by the active updater may bypass its startup guard.
            if (args.Length == 2 && args[0] == "--updated" && Guid.TryParseExact(args[1], "N", out _))
            {
                try
                {
                    var journal = FindJournal(root, args[1]);
                    var data = JsonSerializer.Deserialize<UpdateTransaction.Journal>(File.ReadAllText(journal), UpdatePackage.JsonOptions);
                    return data?.Phase == "Installed";
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return false; }
            }
            if ((error.HResult & 0xffff) is not (32 or 33))
                System.Windows.MessageBox.Show(Text.T("Update_RecoveryRequired"), "TaskbarLyrics");
            return false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidDataException or JsonException or System.ComponentModel.Win32Exception)
        {
            System.Windows.MessageBox.Show(Text.T("Update_RecoveryRequired"), "TaskbarLyrics");
            return false;
        }
    }
    private static bool HasPendingRecovery(string root, string directory)
    {
        if (!Directory.Exists(directory)) return false;
        using var active = new FileStream(UpdatePackage.Contained(directory, "active.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        foreach (var session in Directory.EnumerateDirectories(directory))
        {
            var id = Path.GetFileName(session);
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Unknown recovery directory.");
            _ = UpdatePackage.Contained(directory, id);
            var journal = Path.Combine(session, "journal.json");
            if (!File.Exists(journal)) continue;
            if (new FileInfo(journal).Length > 4 * 1024 * 1024) throw new JsonException("Recovery journal exceeds limit.");
            var data = JsonSerializer.Deserialize<UpdateTransaction.Journal>(File.ReadAllText(journal), UpdatePackage.JsonOptions);
            if (data?.Phase is "Committed" or "Recovered")
            {
                // The cache lock proves no updater is committing this session now. Remove
                // leftover recovery executables/backups from a cleanup that previously failed.
                try { Directory.Delete(session, true); }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                { /* A running recovery.exe may keep its own session directory until the next launch. */ }
                continue;
            }
            var recovery = Path.Combine(session, "recovery.exe");
            if (!File.Exists(recovery)) throw new IOException("Recovery executable is missing.");
            var start = new ProcessStartInfo(recovery) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--recover"); start.ArgumentList.Add(root);
            // Release this directory's guard before the recovery process acquires the cache lock.
            active.Dispose();
            Process.Start(start)?.Dispose();
            return true;
        }
        return false;
    }
    private static string FindJournal(string root, string session)
    {
        var cache = UpdatePackage.Contained(UpdatePackage.GetTransactionRoot(root), session + "/journal.json");
        if (File.Exists(cache)) return cache;
        return UpdatePackage.Contained(root, UpdatePackage.LegacyTransactionDirectory + "/" + session + "/journal.json");
    }
    public static async Task ConfirmAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "--updated" || !Guid.TryParseExact(args[1], "N", out _)) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(UpdatePackage.StartupConfirmationTimeoutSeconds));
        using var pipe = new NamedPipeClientStream(".", "TaskbarLyrics.Update." + args[1] + ".confirm", PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(UpdatePackage.StartupConfirmationTimeoutSeconds * 1000, timeout.Token);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        await writer.WriteLineAsync(args[1] + " " + UpdateChecker.GetCurrentVersion());
    }
}
