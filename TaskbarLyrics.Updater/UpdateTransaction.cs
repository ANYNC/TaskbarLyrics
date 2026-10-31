using System.Text.Json;

namespace TaskbarLyrics.Updates;

public static class UpdateTransaction
{
    public sealed class Journal
    {
        public int SchemaVersion { get; set; } = 1;
        public string Product { get; set; } = "TaskbarLyrics";
        public string TransactionId { get; set; } = "";
        public string Root { get; set; } = "";
        public string PreviousVersion { get; set; } = "";
        public string TargetVersion { get; set; } = "";
        public string OperationKind { get; set; } = "upgrade";
        public string Phase { get; set; } = "Prepared";
        public List<string> BackedUp { get; set; } = [];
        public List<string> Installed { get; set; } = [];
    }
    public static void Save(string directory, Journal journal)
    {
        Directory.CreateDirectory(directory);
        var path = UpdatePackage.Contained(directory, "journal.json");
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, journal, UpdatePackage.JsonOptions);
            stream.Flush(true);
        }
        File.Move(path + ".tmp", path, true);
    }

    public static long InstalledSize(string root, IEnumerable<string> files)
    {
        long total = 0;
        foreach (var relative in files)
        {
            var path = UpdatePackage.Contained(root, relative);
            if (File.Exists(path)) total = checked(total + new FileInfo(path).Length);
        }
        return total;
    }

    public static void Apply(string root, string transaction, string staging, IReadOnlyList<string> files,
        string previousVersion, string targetVersion, string operationKind, Action<string>? checkpoint = null)
    {
        var journal = new Journal
        {
            TransactionId = Path.GetFileName(transaction),
            Root = Path.GetFullPath(root),
            PreviousVersion = previousVersion,
            TargetVersion = targetVersion,
            OperationKind = operationKind
        };
        Save(transaction, journal);
        try
        {
            // The ZIP is the source of truth for files this update replaces. Preserve unrelated
            // files in the installation directory because there is no ownership manifest.
            if (files.Count > UpdatePackage.MaximumFiles || files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
                throw new InvalidDataException("Invalid update file list.");
            foreach (var relative in files)
            {
                var target = UpdatePackage.Contained(root, relative);
                if (!File.Exists(target)) continue;
                journal.BackedUp.Add(relative);
                Save(transaction, journal);
                var backup = UpdatePackage.Contained(Path.Combine(transaction, "backup"), relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                checkpoint?.Invoke("backup:" + relative);
                var backupStaging = UpdatePackage.Contained(transaction, "backup-staging");
                Directory.CreateDirectory(backupStaging);
                var temporaryBackup = UpdatePackage.Contained(backupStaging, Guid.NewGuid().ToString("N") + ".tmp");
                CopyDurable(target, temporaryBackup);
                File.Move(temporaryBackup, backup, true);
                File.Delete(target);
            }
            foreach (var relative in files)
            {
                journal.Installed.Add(relative);
                Save(transaction, journal);
                var path = UpdatePackage.Contained(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                checkpoint?.Invoke("install:" + relative);
                CopyDurable(UpdatePackage.Contained(staging, relative), path);
                File.Delete(UpdatePackage.Contained(staging, relative));
            }
            journal.Phase = "Installed";
            Save(transaction, journal);
        }
        catch
        {
            Recover(root, transaction);
            throw;
        }
    }

    private static void CopyDurable(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.WriteThrough);
        input.CopyTo(output);
        output.Flush(true);
    }

    public static void Recover(string root, string transaction)
    {
        var path = UpdatePackage.Contained(transaction, "journal.json");
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid transaction journal.");
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path), UpdatePackage.JsonOptions)
            ?? throw new InvalidDataException("Invalid transaction journal.");
        if (journal.SchemaVersion != 1 || journal.Product != "TaskbarLyrics" ||
            journal.TransactionId != Path.GetFileName(transaction) ||
            !string.Equals(journal.Root, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ||
            journal.OperationKind is not ("upgrade" or "reinstall") ||
            journal.BackedUp is not { Count: <= 10001 } || journal.Installed is not { Count: <= 10001 } ||
            journal.Phase is not ("Prepared" or "Installed" or "Committed" or "Recovered")) throw new InvalidDataException("Invalid transaction journal.");
        if (journal.Phase is "Committed" or "Recovered") return;
        // Validate the entire journal before touching files.
        foreach (var relative in journal.Installed.Concat(journal.BackedUp))
        {
            if (UpdatePackage.IsProtected(relative))
                throw new InvalidDataException("Reserved recovery path.");
            _ = UpdatePackage.Contained(root, relative);
            _ = UpdatePackage.Contained(Path.Combine(transaction, "backup"), relative);
        }
        foreach (var relative in journal.Installed.AsEnumerable().Reverse())
        {
            var target = UpdatePackage.Contained(root, relative);
            if (File.Exists(target)) File.Delete(target);
        }
        foreach (var relative in journal.BackedUp)
        {
            var backup = UpdatePackage.Contained(Path.Combine(transaction, "backup"), relative);
            if (!File.Exists(backup)) continue;
            var target = UpdatePackage.Contained(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(backup, target, true);
            using var restored = new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.None);
            restored.Flush(true);
        }
        journal.Phase = "Recovered";
        Save(transaction, journal);
    }
    public static void Commit(string transaction)
    {
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(UpdatePackage.Contained(transaction, "journal.json")), UpdatePackage.JsonOptions)
            ?? throw new InvalidDataException("Invalid transaction journal.");
        journal.Phase = "Committed";
        Save(transaction, journal);
    }
}
