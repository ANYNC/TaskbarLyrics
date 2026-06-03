using System;
using System.IO;

namespace TaskbarLyrics.Core.Utilities;

public static class Log
{
    private const long MaxLogFileSizeBytes = 2 * 1024 * 1024;
    private static readonly object LogLock = new();
    private static bool _isVerboseEnabled;

    public enum Level
    {
        Debug,
        Info,
        Warn,
        Error
    }

    public static void Write(Level level, string message)
    {
        if (!_isVerboseEnabled && level < Level.Warn)
        {
            return;
        }

        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log");
            lock (LogLock)
            {
                TruncateIfNeeded(logPath);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpper()}] {message}\n");
            }
        }
        catch
        {
            // 忽略写日志时的异常，防止影响主流程
        }
    }

    public static void Debug(string message) => Write(Level.Debug, message);
    public static void Info(string message) => Write(Level.Info, message);
    public static void Warn(string message) => Write(Level.Warn, message);
    public static void Error(string message) => Write(Level.Error, message);

    public static void SetVerboseEnabled(bool enabled)
    {
        _isVerboseEnabled = enabled;
    }

    private static void TruncateIfNeeded(string logPath)
    {
        if (File.Exists(logPath) && new FileInfo(logPath).Length >= MaxLogFileSizeBytes)
        {
            File.WriteAllText(logPath, string.Empty);
        }
    }
}
