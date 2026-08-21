using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class ActiveSessionCacheTests
{
    [Fact]
    public void FindInWhenLastLyricsSessionIsStillAvailableReturnsTheSameInstance()
    {
        var activeSession = new object();
        var cache = new ActiveSessionCache<object>();
        cache.Remember(activeSession);

        var result = cache.FindIn([new object(), activeSession]);

        Assert.Same(activeSession, result);
        Assert.Same(activeSession, cache.Current);
    }

    [Fact]
    public void FindInWhenLastLyricsSessionIsGoneClearsItAndFallsBackToSelection()
    {
        var activeSession = new object();
        var cache = new ActiveSessionCache<object>();
        cache.Remember(activeSession);

        var result = cache.FindIn([new object()]);

        Assert.Null(result);
        Assert.Null(cache.Current);
        Assert.Null(cache.FindIn([activeSession]));
    }
}
