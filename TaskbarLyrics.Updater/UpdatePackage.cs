using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskbarLyrics.Updates;

public static class UpdatePackage
{
    private static readonly System.Buffers.SearchValues<char> HexCharacters = System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");
    private const string VersionPattern = @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z";
    public const string LegacyTransactionDirectory = ".tbl-update";
    public const string EntryPoint = "TaskbarLyrics.exe";
    public const string SettingsPage = "Web/Settings/settings.html";
    public const long MaximumPackageSize = 512L * 1024 * 1024;
    public const long MaximumExpandedSize = 2L * 1024 * 1024 * 1024;
    public const int MaximumFiles = 10000;
    public const int StartupConfirmationTimeoutSeconds = 120;
    public static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static bool IsVersion(string? version) => version is { Length: <= 32 } && Regex.IsMatch(version, VersionPattern, RegexOptions.CultureInvariant);
    public static string GetTransactionRoot(string installationRoot)
    {
        var normalizedRoot = Path.GetFullPath(installationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var rootKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot))).ToLowerInvariant();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarLyrics", "Updates", "Transactions", rootKey);
    }
    public static bool IsDigest(string? digest) => digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
        digest.AsSpan(7).IndexOfAnyExcept(HexCharacters) < 0;
    public static bool IsProtected(string relative)
    {
        var top = relative.Split('/')[0];
        return top.Equals("Logs", StringComparison.OrdinalIgnoreCase) || top.Equals(LegacyTransactionDirectory, StringComparison.OrdinalIgnoreCase);
    }

    public static string Contained(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Length > 240 || relative.Contains('\\') || relative.Contains(':') ||
            relative.StartsWith('/') || relative.Split('/').Any(segment =>
                string.IsNullOrEmpty(segment) || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                Regex.IsMatch(segment, @"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw new InvalidDataException("Invalid update path.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update path escapes root.");
        var current = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked update directories are not supported.");
            current = current.Parent;
        }
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked update files are not supported.");
        return path;
    }

    public static string ReadVersion(string root)
    {
        var path = Contained(root, EntryPoint);
        if (!File.Exists(path)) throw new InvalidDataException("Missing application executable.");
        var version = FileVersionInfo.GetVersionInfo(path).ProductVersion?.Split('+')[0];
        return IsVersion(version) ? version! : throw new InvalidDataException("Invalid installed version.");
    }

    public static void ValidateExecutable(string path, string version)
    {
        if (!IsVersion(version)) throw new InvalidDataException("Invalid target version.");
        var info = FileVersionInfo.GetVersionInfo(path);
        if (info.ProductVersion?.Split('+')[0] != version || info.ProductName is not ("TaskbarLyrics.App" or "TaskbarLyrics"))
            throw new InvalidDataException("Wrong executable product or version.");
    }

    public static async Task VerifyDigestAsync(Stream stream, string digest, CancellationToken token = default)
    {
        if (!IsDigest(digest)) throw new InvalidDataException("Missing SHA-256 digest.");
        var actual = await SHA256.HashDataAsync(stream, token);
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(digest.AsSpan(7))))
            throw new InvalidDataException("SHA-256 mismatch.");
    }

    public static IReadOnlyList<string> Extract(string package, string version, string destination, CancellationToken token = default)
    {
        if (!IsVersion(version)) throw new InvalidDataException("Invalid target version.");
        using var archive = ZipFile.OpenRead(package);
        var prefix = "TaskbarLyrics-" + version + "/";
        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var entryName = entry.FullName.Replace('\\', '/');
            if (!entryName.StartsWith(prefix, StringComparison.Ordinal) || (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Invalid package root or linked entry.");
            var relative = entryName[prefix.Length..];
            var isDirectory = entryName.EndsWith('/');
            if (relative.Length == 0 && isDirectory) continue;
            if (isDirectory)
            {
                if (entry.Length != 0) throw new InvalidDataException("Invalid directory entry.");
                _ = Contained(destination, relative.TrimEnd('/'));
                continue;
            }
            _ = Contained(destination, relative);
            if (IsProtected(relative)) throw new InvalidDataException("Reserved package path.");
            total = checked(total + entry.Length);
            if (files.Count >= MaximumFiles || entry.Length > MaximumPackageSize || total > MaximumExpandedSize ||
                !TryAddFile(files, directories, relative, entry))
                throw new InvalidDataException("Duplicate entry, conflicting paths, or package exceeds limits.");
        }
        if (!files.ContainsKey(EntryPoint) || !files.ContainsKey(SettingsPage))
            throw new InvalidDataException("Missing required application files.");
        foreach (var (relative, entry) in files)
        {
            token.ThrowIfCancellationRequested();
            var path = Contained(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, false);
        }
        return files.Keys.ToList();
    }

    private static bool TryAddFile(Dictionary<string, ZipArchiveEntry> files, HashSet<string> directories, string relative, ZipArchiveEntry entry)
    {
        if (directories.Contains(relative)) return false;
        for (var index = relative.IndexOf('/'); index > 0; index = relative.IndexOf('/', index + 1))
        {
            var parent = relative[..index];
            if (files.ContainsKey(parent)) return false;
            directories.Add(parent);
        }
        return files.TryAdd(relative, entry);
    }
}
