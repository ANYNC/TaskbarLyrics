using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace TaskbarLyrics.Light.App;

internal static class UpdateInstaller
{
    private const string ExecutableName = "TaskbarLyrics.Light.exe";
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(8);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = DownloadTimeout
    };

    public static async Task<PreparedUpdate> DownloadAndPrepareAsync(
        UpdateCheckResult update,
        IProgress<UpdateInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!update.HasUpdate || update.Asset is null)
        {
            throw new InvalidOperationException("没有可安装的更新包。");
        }

        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var restartExecutablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(restartExecutablePath))
        {
            restartExecutablePath = Path.Combine(installDirectory, ExecutableName);
        }

        var versionSegment = MakeSafePathSegment(update.Version);
        var workingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarLyrics.Light",
            "Updates",
            $"{versionSegment}-{DateTimeOffset.Now:yyyyMMddHHmmss}");
        var extractRoot = Path.Combine(workingRoot, "extracted");
        var downloadPath = Path.Combine(workingRoot, update.Asset.Name);
        var scriptPath = Path.Combine(workingRoot, "apply-update.ps1");
        var logPath = Path.Combine(workingRoot, "update.log");

        Directory.CreateDirectory(workingRoot);
        Directory.CreateDirectory(extractRoot);

        progress?.Report(new UpdateInstallProgress("正在下载更新包...", 0));
        await DownloadAssetAsync(update.Asset, downloadPath, progress, cancellationToken);

        progress?.Report(new UpdateInstallProgress("正在解压更新包..."));
        ZipFile.ExtractToDirectory(downloadPath, extractRoot, overwriteFiles: true);

        var payloadDirectory = FindPayloadDirectory(extractRoot);
        WriteInstallerScript(scriptPath);

        progress?.Report(new UpdateInstallProgress("更新包已准备完成。", 1));
        return new PreparedUpdate(
            update.Version,
            update.Asset.Name,
            payloadDirectory,
            installDirectory,
            restartExecutablePath,
            scriptPath,
            logPath);
    }

    public static void LaunchInstaller(PreparedUpdate update)
    {
        if (!File.Exists(update.ScriptPath))
        {
            throw new FileNotFoundException("更新安装脚本不存在。", update.ScriptPath);
        }

        if (!Directory.Exists(update.PayloadDirectory))
        {
            throw new DirectoryNotFoundException("更新包目录不存在。");
        }

        var requiresElevation = !CanWriteToDirectory(update.TargetDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = requiresElevation,
            CreateNoWindow = !requiresElevation,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (requiresElevation)
        {
            startInfo.Verb = "runas";
        }

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(update.ScriptPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-SourceDir");
        startInfo.ArgumentList.Add(update.PayloadDirectory);
        startInfo.ArgumentList.Add("-TargetDir");
        startInfo.ArgumentList.Add(update.TargetDirectory);
        startInfo.ArgumentList.Add("-RestartExe");
        startInfo.ArgumentList.Add(update.RestartExecutablePath);
        startInfo.ArgumentList.Add("-LogPath");
        startInfo.ArgumentList.Add(update.LogPath);

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("无法启动更新安装器。");
        }
    }

    private static async Task DownloadAssetAsync(
        UpdateReleaseAsset asset,
        string destinationPath,
        IProgress<UpdateInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("TaskbarLyrics-Light");
        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        if ((!totalBytes.HasValue || totalBytes <= 0) && asset.Size > 0)
        {
            totalBytes = asset.Size;
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destinationPath);
        var buffer = new byte[81920];
        long downloadedBytes = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;

            if (totalBytes is > 0)
            {
                var percent = Math.Clamp((double)downloadedBytes / totalBytes.Value, 0, 1);
                progress?.Report(new UpdateInstallProgress(
                    $"正在下载更新包... {FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes.Value)}",
                    percent));
            }
            else
            {
                progress?.Report(new UpdateInstallProgress(
                    $"正在下载更新包... {FormatBytes(downloadedBytes)}"));
            }
        }
    }

    private static string FindPayloadDirectory(string extractRoot)
    {
        var directExecutable = Path.Combine(extractRoot, ExecutableName);
        if (File.Exists(directExecutable))
        {
            return extractRoot;
        }

        var candidate = Directory
            .EnumerateFiles(extractRoot, ExecutableName, SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => directory!)
            .OrderByDescending(directory => File.Exists(Path.Combine(directory, "TaskbarLyrics.Light.dll")) ? 1 : 0)
            .ThenBy(directory => directory.Length)
            .FirstOrDefault();

        if (candidate is null)
        {
            throw new InvalidOperationException("更新包中没有找到 TaskbarLyrics.Light.exe。");
        }

        return candidate;
    }

    private static void WriteInstallerScript(string scriptPath)
    {
        var script = """
param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$SourceDir,
    [Parameter(Mandatory=$true)][string]$TargetDir,
    [Parameter(Mandatory=$true)][string]$RestartExe,
    [Parameter(Mandatory=$true)][string]$LogPath
)

$ErrorActionPreference = "Stop"

function Write-UpdateLog {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Add-Content -LiteralPath $LogPath -Value "[$timestamp] $Message"
}

try {
    Write-UpdateLog "Waiting for process $ProcessId to exit."
    try {
        Wait-Process -Id $ProcessId -Timeout 45 -ErrorAction SilentlyContinue
    } catch {
    }

    Start-Sleep -Milliseconds 800
    Write-UpdateLog "Copying files from $SourceDir to $TargetDir."

    $copied = $false
    for ($attempt = 1; $attempt -le 80; $attempt++) {
        try {
            $items = Get-ChildItem -LiteralPath $SourceDir -Force
            foreach ($item in $items) {
                Copy-Item -LiteralPath $item.FullName -Destination $TargetDir -Recurse -Force -ErrorAction Stop
            }

            $copied = $true
            break
        } catch {
            Write-UpdateLog "Copy attempt $attempt failed: $($_.Exception.Message)"
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $copied) {
        throw "Failed to copy update files."
    }

    Write-UpdateLog "Restarting $RestartExe."
    Start-Process -FilePath $RestartExe -WorkingDirectory $TargetDir
} catch {
    Write-UpdateLog "Update failed: $($_.Exception.Message)"
    try {
        Start-Process -FilePath $RestartExe -WorkingDirectory $TargetDir
    } catch {
    }

    exit 1
}
""";

        File.WriteAllText(scriptPath, script, Encoding.UTF8);
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".update-write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string MakeSafePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:F0} {units[unitIndex]}"
            : $"{size:F1} {units[unitIndex]}";
    }
}

internal sealed record PreparedUpdate(
    string Version,
    string AssetName,
    string PayloadDirectory,
    string TargetDirectory,
    string RestartExecutablePath,
    string ScriptPath,
    string LogPath);

internal sealed record UpdateInstallProgress(
    string Message,
    double? Percent = null);
