using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AutomaticUpdateSchedulerTests
{
    [Fact]
    public async Task DelaysStartupThenSchedulesFromCompletionAndCancelsOnDisable()
    {
        var waits = new List<(TimeSpan Interval, TaskCompletionSource Completion, CancellationToken Token)>();
        var checks = 0;
        using var scheduler = new AutomaticUpdateScheduler(_ => { checks++; return Task.CompletedTask; },
            (interval, token) =>
            {
                var completion = new TaskCompletionSource();
                waits.Add((interval, completion, token));
                return completion.Task;
            });
        scheduler.SetEnabled(true);
        scheduler.SetEnabled(true);
        Assert.Single(waits);
        Assert.Equal(TimeSpan.FromSeconds(5), waits[0].Interval);
        Assert.Equal(0, checks);
        waits[0].Completion.SetResult();
        await Task.Yield();
        Assert.Equal(1, checks);
        Assert.Equal(TimeSpan.FromHours(24), waits[1].Interval);
        waits[1].Completion.SetResult();
        await Task.Yield();
        Assert.Equal(2, checks);
        scheduler.SetEnabled(false);
        Assert.True(waits[2].Token.IsCancellationRequested);
        scheduler.SetEnabled(true);
        Assert.Equal(TimeSpan.FromSeconds(5), waits[3].Interval);
    }

    [Theory]
    [InlineData("v1.7.6", true)]
    [InlineData("V10.0.0", true)]
    [InlineData("1.0.0", true)]
    [InlineData("1.0.0-preview", false)]
    [InlineData("1.0.0.1", false)]
    [InlineData("release-1.0.0", false)]
    [InlineData("01.0.0", false)]
    [InlineData("2147483648.0.0", false)]
    [InlineData("1.0.0\n", false)]
    public void VersionsMustBeStrictStableTriples(string tag, bool valid) =>
        Assert.Equal(valid, UpdateChecker.TryParseVersion(tag, out _));

    [Theory]
    [InlineData("https://github.com/ANYNC/TaskbarLyrics/releases/tag/v1.0.0", true)]
    [InlineData("https://github.com.evil.test/ANYNC/TaskbarLyrics/releases/latest", false)]
    [InlineData("http://github.com/ANYNC/TaskbarLyrics/releases/latest", false)]
    [InlineData("https://github.com/other/project/releases/latest", false)]
    public void ReleaseLinksStayWithinRepository(string url, bool allowed) =>
        Assert.Equal(allowed, UpdateChecker.IsReleaseUrl(url));
}
