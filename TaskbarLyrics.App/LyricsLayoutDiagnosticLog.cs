using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed class LyricsLayoutDiagnosticLog
{
    internal const string MarkerFileName = "layout-diagnostics.enabled";
    private const int MaximumRecords = 2500;
    private const long MaximumBytes = 16 * 1024 * 1024;
    private static readonly Lazy<LyricsLayoutDiagnosticLog?> CurrentLog = new(CreateForProcess);
    private readonly object _gate = new();
    private readonly string _path;
    private int _records;
    private long _bytes;
    private bool _unavailable;

    internal LyricsLayoutDiagnosticLog(string path) => _path = path;

    internal static LyricsLayoutDiagnosticLog? Current => CurrentLog.Value;

    internal bool Write(string kind, object data)
    {
        lock (_gate)
        {
            if (_unavailable || _records >= MaximumRecords || _bytes >= MaximumBytes) return false;
            try
            {
                var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, kind, data }) + "\n";
                var bytes = Encoding.UTF8.GetByteCount(line);
                if (_bytes + bytes > MaximumBytes) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, line, new UTF8Encoding(false));
                _bytes += bytes;
                _records++;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _unavailable = true;
                Log.Diagnostic("LAYOUT_DIAGNOSTICS", $"WriteFailed Error={ex.GetType().Name}");
                return false;
            }
        }
    }

    private static LyricsLayoutDiagnosticLog? CreateForProcess()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, MarkerFileName))) return null;
        var fileName = $"layout-diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.jsonl";
        var log = new LyricsLayoutDiagnosticLog(Path.Combine(Log.GetLogsDirectory(), fileName));
        var environment = new
        {
            schemaVersion = 1,
            appVersion = typeof(LyricsLayoutDiagnosticLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            os = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            durationSeconds = 600,
            maximumRecords = MaximumRecords,
            maximumBytes = MaximumBytes
        };
        if (!log.Write("session", environment))
        {
            log = new LyricsLayoutDiagnosticLog(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarLyrics", "Logs", fileName));
            if (!log.Write("session", environment)) return null;
        }
        Log.Diagnostic("LAYOUT_DIAGNOSTICS", $"Enabled File='{log._path}' DurationSeconds=600");
        return log;
    }
}
