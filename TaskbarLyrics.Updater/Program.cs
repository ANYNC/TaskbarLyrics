using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace TaskbarLyrics.Updates;

public sealed record UpdateRequest(int ProtocolVersion, string Root, string Package, string Version, string Digest,
    int MainPid, long MainStartedTicks, string SessionId, string PipeName, string OperationKind,
    string FailureMessage, string RecoveryInstructions);

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--validate-package")
        {
            var temporary = Path.Combine(Path.GetTempPath(), "TaskbarLyrics-package-validation-" + Guid.NewGuid().ToString("N"));
            try
            {
                UpdatePackage.Extract(args[1], args[2], temporary);
                UpdatePackage.ValidateExecutable(Path.Combine(temporary, UpdatePackage.EntryPoint), args[2]);
                return 0;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            { Console.Error.WriteLine(error.Message); return 1; }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }
        string? transaction = null;
        string? root = null;
        var mainExited = false;
        var newProcessStarted = false;
        var stage = "initialize";
        var failureMessage = "TaskbarLyrics";
        try
        {
            if (args.Length == 2 && args[0] == "--recover")
            {
                var messageFile = Path.Combine(AppContext.BaseDirectory, "failure-message.txt");
                if (File.Exists(messageFile)) failureMessage = File.ReadAllText(messageFile);
                root = Path.GetFullPath(args[1]);
                using var recoveryLock = Lock(root);
                var instancePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarLyrics", "TaskbarLyrics.lock");
                using var instanceLock = new FileStream(instancePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                RecoverPending(root);
                instanceLock.Dispose();
                recoveryLock.Dispose();
                Start(root, null);
                return 0;
            }
            if (args.Length != 1 || new FileInfo(args[0]).Length > 16384) return 1;
            stage = "read request";
            var request = JsonSerializer.Deserialize<UpdateRequest>(File.ReadAllText(args[0]), UpdatePackage.JsonOptions)
                ?? throw new InvalidDataException("Invalid request.");
            failureMessage = request.FailureMessage;
            if (request.ProtocolVersion != 1 || !Guid.TryParseExact(request.SessionId, "N", out _) ||
                request.OperationKind is not ("upgrade" or "reinstall") || request.PipeName != "TaskbarLyrics.Update." + request.SessionId)
                throw new InvalidDataException("Invalid update session.");
            root = Path.GetFullPath(request.Root);
            stage = "acquire update lock";
            using var directoryLock = Lock(root);
            RecoverPending(root);
            var current = UpdatePackage.ReadVersion(root);
            if (!Version.TryParse(current, out var oldVersion) || !Version.TryParse(request.Version, out var newVersion) ||
                (request.OperationKind == "reinstall" ? newVersion != oldVersion : newVersion <= oldVersion))
                throw new InvalidDataException("Invalid target version.");
            using var main = Process.GetProcessById(request.MainPid);
            if (main.StartTime.ToUniversalTime().Ticks != request.MainStartedTicks ||
                !string.Equals(main.MainModule?.FileName, Path.Combine(root, UpdatePackage.EntryPoint), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid main process identity.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            stage = "verify update package";
            using var package = File.OpenRead(request.Package);
            await UpdatePackage.VerifyDigestAsync(package, request.Digest, timeout.Token);
            var transactionRoot = UpdatePackage.GetTransactionRoot(root);
            transaction = UpdatePackage.Contained(transactionRoot, request.SessionId);
            Directory.CreateDirectory(transaction);
            var staging = Path.Combine(transaction, "staging");
            var files = UpdatePackage.Extract(request.Package, request.Version, staging, timeout.Token);
            using (var archive = System.IO.Compression.ZipFile.OpenRead(request.Package))
            {
                var required = checked(UpdateTransaction.InstalledSize(root, files) + archive.Entries.Sum(entry => entry.Length) + 64L * 1024 * 1024);
                if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < required)
                    throw new IOException("Insufficient space for staging and rollback.");
            }
            UpdatePackage.ValidateExecutable(Path.Combine(staging, UpdatePackage.EntryPoint), request.Version);
            stage = "prepare recovery files";
            File.Copy(Environment.ProcessPath!, Path.Combine(transaction, "recovery.exe"));
            await File.WriteAllTextAsync(Path.Combine(transaction, "failure-message.txt"), request.FailureMessage);
            await File.WriteAllTextAsync(Path.Combine(transaction, "RECOVERY.txt"),
                request.RecoveryInstructions);
            using var pipe = new NamedPipeClientStream(".", request.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(30000, timeout.Token);
            UpdatePipeIdentity.RequireServer(pipe, request.MainPid);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("READY " + request.SessionId);
            if (await reader.ReadLineAsync(timeout.Token) != "APPLY " + request.SessionId) throw new IOException("Update handoff canceled.");
            stage = "wait for previous process exit";
            using (var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                await main.WaitForExitAsync(exitTimeout.Token);
            mainExited = true;
            // Holding this handle verifies no other normal instance owns the application lock.
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarLyrics", "TaskbarLyrics.lock");
            stage = "replace package files";
            using (var instanceLock = new FileStream(local, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                UpdateTransaction.Apply(root, transaction, staging, files, current, request.Version, request.OperationKind);
            using var confirmation = new NamedPipeServerStream(request.PipeName + ".confirm", PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            stage = "start updated process";
            using var updated = Start(root, request.SessionId);
            newProcessStarted = updated is not null;
            try
            {
                stage = "confirm updated process startup";
                using var confirmTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(UpdatePackage.StartupConfirmationTimeoutSeconds));
                await confirmation.WaitForConnectionAsync(confirmTimeout.Token);
                if (updated is null) throw new IOException("New process did not start.");
                UpdatePipeIdentity.RequireClient(confirmation, updated.Id);
                using var confirmReader = new StreamReader(confirmation);
                if (await confirmReader.ReadLineAsync(confirmTimeout.Token) != request.SessionId + " " + request.Version)
                    throw new IOException("Invalid startup confirmation.");
            }
            catch (Exception confirmationError) when (confirmationError is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                // Never replace files underneath a running, unconfirmed process.
                if (updated is { HasExited: false })
                    throw new IOException("The updated process is running, but startup confirmation failed. Keep it open and inspect the update log.", confirmationError);
                UpdateTransaction.Recover(root, transaction);
                directoryLock.Dispose();
                Start(root, null)?.Dispose();
                throw;
            }
            stage = "commit update journal";
            UpdateTransaction.Commit(transaction);
            // Only the exact validated session is eligible for cleanup.
            stage = "clean up committed transaction";
            try { Directory.Delete(transaction, true); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            { /* The committed journal allows safe cleanup on a later run. */ }
            // Release the per-installation lock before reporting a completed handoff.
            directoryLock.Dispose();
            package.Dispose();
            var cacheRoot = Path.Combine(Path.GetTempPath(), "TaskbarLyrics", "Updates", request.SessionId);
            if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(request.Package)!), Path.GetFullPath(cacheRoot), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(Path.Combine(cacheRoot, "package.zip"));
                    var verified = UpdatePackage.Contained(cacheRoot, "verified");
                    if (Directory.Exists(verified)) Directory.Delete(verified, true);
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                { /* Cache cleanup cannot undo a committed update. */ }
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (mainExited && !newProcessStarted && root is not null && transaction is not null && File.Exists(Path.Combine(transaction, "journal.json")))
            {
                try { UpdateTransaction.Recover(root, transaction); Start(root, null)?.Dispose(); }
                catch (Exception recoveryError) { ex = new AggregateException(ex, recoveryError); }
            }
            var logs = Path.Combine(Path.GetTempPath(), "TaskbarLyrics", "Updates");
            Directory.CreateDirectory(logs);
            var log = Path.Combine(logs, "failure-" + Guid.NewGuid().ToString("N") + ".log");
            await File.WriteAllTextAsync(log, "Stage: " + stage + "\n" + ex + "\nRecovery: " + transaction);
            if (OperatingSystem.IsWindows()) ShowFailure(failureMessage, log);
            return 1;
        }
    }
    private static FileStream Lock(string root)
    {
        var directory = UpdatePackage.GetTransactionRoot(root);
        Directory.CreateDirectory(directory);
        return new FileStream(UpdatePackage.Contained(directory, "active.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static void RecoverPending(string root)
    {
        var legacyDirectory = UpdatePackage.Contained(root, UpdatePackage.LegacyTransactionDirectory);
        if (Directory.Exists(legacyDirectory)) RecoverPendingIn(root, legacyDirectory);
        var cacheDirectory = UpdatePackage.GetTransactionRoot(root);
        if (Directory.Exists(cacheDirectory)) RecoverPendingIn(root, cacheDirectory);
    }
    private static void RecoverPendingIn(string root, string directory)
    {
        foreach (var session in Directory.EnumerateDirectories(directory))
        {
            if (!Guid.TryParseExact(Path.GetFileName(session), "N", out _)) throw new InvalidDataException("Unknown recovery directory.");
            _ = UpdatePackage.Contained(directory, Path.GetFileName(session));
            if (File.Exists(Path.Combine(session, "journal.json"))) UpdateTransaction.Recover(root, session);
        }
    }
    private static Process? Start(string root, string? session)
    {
        var start = new ProcessStartInfo(Path.Combine(root, UpdatePackage.EntryPoint)) { WorkingDirectory = root, UseShellExecute = false };
        if (session is not null) { start.ArgumentList.Add("--updated"); start.ArgumentList.Add(session); }
        return Process.Start(start);
    }
    private static void ShowFailure(string message, string log) => _ = MessageBox(IntPtr.Zero,
        message + "\n\n" + log, "TaskbarLyrics", 0x10);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
