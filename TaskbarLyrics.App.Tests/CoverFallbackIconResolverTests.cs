using TaskbarLyrics.App;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class CoverFallbackIconResolverTests
{
    private const string IconDataUri = "data:image/png;base64,iVBORw0KGgo=";

    [Fact]
    public void PrefersSessionUserModelIdOverResolvedSourceName()
    {
        var requested = new List<string>();
        var resolver = CreateResolver(key =>
        {
            requested.Add(key);
            return IconDataUri;
        });

        Assert.Equal(IconDataUri, resolver.Resolve("cloudmusic.exe", "Netease"));
        Assert.Equal(["cloudmusic.exe"], requested);
    }

    [Fact]
    public void UsesSourceNameWhenSessionUserModelIdIsMissing()
    {
        var requested = new List<string>();
        var resolver = CreateResolver(key =>
        {
            requested.Add(key);
            return IconDataUri;
        });

        Assert.Equal(IconDataUri, resolver.Resolve("  ", "Netease"));
        Assert.Equal(["Netease"], requested);
    }

    [Fact]
    public void ReturnsEmptyWithoutResolvingWhenNoSourceIsKnown()
    {
        var lookups = 0;
        var resolver = CreateResolver(_ =>
        {
            lookups++;
            return IconDataUri;
        });

        Assert.Equal(string.Empty, resolver.Resolve(null, "   "));
        Assert.Equal(0, lookups);
    }

    [Fact]
    public void CachesSuccessfulResolutionForTheSameSource()
    {
        var lookups = 0;
        var now = DateTimeOffset.UnixEpoch;
        var resolver = new CoverFallbackIconResolver(
            _ =>
            {
                lookups++;
                return IconDataUri;
            },
            () => now);

        Assert.Equal(IconDataUri, resolver.Resolve("cloudmusic.exe", "Netease"));
        now = now.AddHours(3);
        Assert.Equal(IconDataUri, resolver.Resolve("cloudmusic.exe", "Netease"));
        Assert.Equal(1, lookups);
    }

    [Fact]
    public void ResolvesAgainWhenTheSourceChanges()
    {
        var requested = new List<string>();
        var resolver = CreateResolver(key =>
        {
            requested.Add(key);
            return $"{IconDataUri}{key}";
        });

        Assert.Equal($"{IconDataUri}qqmusic.exe", resolver.Resolve("qqmusic.exe", "QQMusic"));
        Assert.Equal($"{IconDataUri}cloudmusic.exe", resolver.Resolve("cloudmusic.exe", "Netease"));
        Assert.Equal(["qqmusic.exe", "cloudmusic.exe"], requested);
    }

    [Fact]
    public void RetriesFailedResolutionAfterTheRetryInterval()
    {
        var lookups = 0;
        var icon = string.Empty;
        var now = DateTimeOffset.UnixEpoch;
        var resolver = new CoverFallbackIconResolver(
            _ =>
            {
                lookups++;
                return icon;
            },
            () => now);

        Assert.Equal(string.Empty, resolver.Resolve("cloudmusic.exe", null));

        now = now.Add(CoverFallbackIconResolver.FailureRetryInterval - TimeSpan.FromSeconds(1));
        Assert.Equal(string.Empty, resolver.Resolve("cloudmusic.exe", null));
        Assert.Equal(1, lookups);

        now = now.Add(TimeSpan.FromSeconds(2));
        icon = IconDataUri;
        Assert.Equal(IconDataUri, resolver.Resolve("cloudmusic.exe", null));
        Assert.Equal(2, lookups);
    }

    private static CoverFallbackIconResolver CreateResolver(Func<string, string> resolveIcon) =>
        new(resolveIcon, () => DateTimeOffset.UnixEpoch);
}
