using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class ActiveSessionCacheTests
{
    [Fact]
    public void FindIn_WhenLastLyricsSessionIsStillAvailable_ReturnsTheSameInstance()
    {
        var activeSession = new object();
        var cache = new ActiveSessionCache<object>();
        cache.Remember(activeSession);

        var result = cache.FindIn([new object(), activeSession]);

        Assert.Same(activeSession, result);
    }

    [Fact]
    public void FindIn_WhenLastLyricsSessionIsGone_ClearsItAndFallsBackToSelection()
    {
        var activeSession = new object();
        var cache = new ActiveSessionCache<object>();
        cache.Remember(activeSession);

        var result = cache.FindIn([new object()]);

        Assert.Null(result);
        Assert.Null(cache.FindIn([activeSession]));
    }
}
