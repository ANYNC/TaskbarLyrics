using System.IO;
using System.Text.Json;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsLayoutDiagnosticLogTests
{
    [Fact]
    public void LogKeepsStructuredEventsFromMultipleWindowsInOneFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TaskbarLyrics-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "session.jsonl");
            var log = new LyricsLayoutDiagnosticLog(path);
            Assert.True(log.Write("snapshot", new { window = 1, scale = 100 }));
            Assert.True(log.Write("trigger", new { window = 2, rejected = true }));
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            using var first = JsonDocument.Parse(lines[0]);
            using var second = JsonDocument.Parse(lines[1]);
            Assert.Equal("snapshot", first.RootElement.GetProperty("kind").GetString());
            Assert.True(first.RootElement.GetProperty("timestamp").TryGetDateTimeOffset(out _));
            Assert.Equal(100, first.RootElement.GetProperty("data").GetProperty("scale").GetInt32());
            Assert.Equal(2, second.RootElement.GetProperty("data").GetProperty("window").GetInt32());
            Assert.True(second.RootElement.GetProperty("data").GetProperty("rejected").GetBoolean());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void UnwritableLogDoesNotThrowOrKeepRetrying()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TaskbarLyrics-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var log = new LyricsLayoutDiagnosticLog(directory);
            Assert.False(log.Write("snapshot", new { scale = 100 }));
            Assert.False(log.Write("trigger", new { accepted = false }));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RecordLimitBoundsOneCollectionSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TaskbarLyrics-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "session.jsonl");
            var log = new LyricsLayoutDiagnosticLog(path);
            for (var i = 0; i < 2500; i++) Assert.True(log.Write("snapshot", new { sequence = i }));
            Assert.False(log.Write("trigger", new { accepted = false }));
            Assert.Equal(2500, File.ReadLines(path).Count());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
