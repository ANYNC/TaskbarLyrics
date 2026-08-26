using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FontCatalogServiceTests
{
    [Fact]
    public void ResolveInstalledFamilyAcceptsDuplicateValuesIgnoringCase()
    {
        var fonts = new[]
        {
            new FontCatalogOption { Value = "Normal", Label = "Elyaris" },
            new FontCatalogOption { Value = "normal", Label = "Elyaris" }
        };

        var resolved = FontCatalogService.ResolveInstalledFamily("ELYARIS", fonts);

        Assert.Equal("Normal", resolved);
    }
}
