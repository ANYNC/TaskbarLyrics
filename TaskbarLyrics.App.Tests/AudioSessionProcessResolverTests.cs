using TaskbarLyrics.App;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AudioSessionProcessResolverTests
{
    private const string PlayerPath = @"C:\Player\player.exe";

    private static readonly int[] SingleProcess = [4242];
    private static readonly int[] RepeatedProcess = [4242, 4242];
    private static readonly int[] TwoProcesses = [4242, 5151];
    private static readonly int[] NoProcess = [];

    [Fact]
    public void ResolvesTheExecutablePathOfTheOnlyPlayingProcess()
    {
        Assert.Equal(PlayerPath, AudioSessionProcessResolver.SelectSingleSessionExecutablePath(SingleProcess, ResolvePlayingProcess));
    }

    [Fact]
    public void CollapsesRepeatedIdentifiersOfTheSameProcess()
    {
        Assert.Equal(PlayerPath, AudioSessionProcessResolver.SelectSingleSessionExecutablePath(RepeatedProcess, ResolvePlayingProcess));
    }

    [Fact]
    public void SkipsWhenNothingIsPlaying()
    {
        Assert.Null(AudioSessionProcessResolver.SelectSingleSessionExecutablePath(NoProcess, ResolvePlayingProcess));
    }

    [Fact]
    public void SkipsAmbiguousAudioSessions()
    {
        Assert.Null(AudioSessionProcessResolver.SelectSingleSessionExecutablePath(TwoProcesses, ResolvePlayingProcess));
    }

    [Fact]
    public void ReturnsNullWhenThePlayingProcessPathIsUnavailable()
    {
        Assert.Null(AudioSessionProcessResolver.SelectSingleSessionExecutablePath(SingleProcess, _ => null));
    }

    private static string? ResolvePlayingProcess(int processId) => processId == SingleProcess[0] ? PlayerPath : null;
}
