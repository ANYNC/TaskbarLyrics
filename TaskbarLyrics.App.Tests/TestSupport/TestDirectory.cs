using System.IO;

namespace TaskbarLyrics.App.Tests;

internal sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, $"test-{Guid.NewGuid():N}");

    public TestDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
