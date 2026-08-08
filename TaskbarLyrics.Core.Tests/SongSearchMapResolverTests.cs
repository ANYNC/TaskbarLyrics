using TaskbarLyrics.Core.Database;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class SongSearchMapResolverTests
{
    [Fact]
    public void SelectBestMappingPrefersExactAlbum()
    {
        var legacy = CreateMapping(string.Empty, "Legacy title");
        var exact = CreateMapping("Studio album", "Studio title");

        var selected = SongSearchMapResolver.SelectBestMapping(
            [legacy, exact],
            "Studio album");

        Assert.Same(exact, selected);
    }

    [Fact]
    public void SelectBestMappingFallsBackToAlbumlessLegacyMapping()
    {
        var legacy = CreateMapping(string.Empty, "Legacy title");
        var otherAlbum = CreateMapping("Other album", "Other title");

        var selected = SongSearchMapResolver.SelectBestMapping(
            [legacy, otherAlbum],
            "Unmapped album");

        Assert.Same(legacy, selected);
    }

    [Fact]
    public void SelectBestMappingAvoidsAmbiguousCrossAlbumFallback()
    {
        var first = CreateMapping("First album", "First title");
        var second = CreateMapping("Second album", "Second title");

        var selected = SongSearchMapResolver.SelectBestMapping(
            [first, second],
            "Unmapped album");

        Assert.Null(selected);
    }

    [Fact]
    public void SelectBestMappingKeepsSingleExistingMappingCompatible()
    {
        var existing = CreateMapping("Old album metadata", "Mapped title");

        var selected = SongSearchMapResolver.SelectBestMapping(
            [existing],
            "Corrected album metadata");

        Assert.Same(existing, selected);
    }

    private static SongSearchMap CreateMapping(string originalAlbum, string mappedTitle) => new()
    {
        OriginalTitle = "Original title",
        OriginalArtist = "Original artist",
        OriginalAlbum = originalAlbum,
        MappedTitle = mappedTitle
    };
}
