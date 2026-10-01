using TaskbarLyrics.App;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class CustomPlayerIconTests
{
    private const int MaximumBytes = 128 * 1024;

    [Fact]
    public void FormatsPngAsDataUrl()
    {
        var png = new byte[] { 137, 80, 78, 71 };

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(png)}", CustomPlayerIcon.ToPngDataUrl(png));
        Assert.Equal(string.Empty, CustomPlayerIcon.ToPngDataUrl([]));
    }

    [Fact]
    public void AcceptsPngUpToTheStorageLimit()
    {
        Assert.True(CustomPlayerIcon.AcceptsPng(Png(MaximumBytes)));
        Assert.False(CustomPlayerIcon.AcceptsPng(Png(MaximumBytes + 1)));
    }

    [Fact]
    public void RejectsPayloadsThatAreNotStorablePngs()
    {
        Assert.False(CustomPlayerIcon.AcceptsPng([]));
        Assert.False(CustomPlayerIcon.AcceptsPng([255, 216, 255, 224]));
    }

    private static byte[] Png(int length)
    {
        var png = new byte[length];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        return png;
    }
}
