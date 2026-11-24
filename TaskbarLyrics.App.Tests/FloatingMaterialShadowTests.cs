using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void FloatingShadowDoesNotExposeItsOpaqueCaster()
    {
        RunSta(() =>
        {
            var owner = CreateOwner();
            using var material = new FloatingMaterialFallbackWindow(shadowOnly: true);
            try
            {
                var ownerHandle = new WindowInteropHelper(owner).Handle;
                var dpi = TaskbarNativeMethods.GetDpiForWindow(ownerHandle);
                var scale = dpi / 96.0;
                material.Configure(light: true, brightness: 0.55, tintOpacity: 0.65, blurAmount: 24);
                material.Position(FloatingLyricsPanelController.GetBounds(owner), scale, ownerHandle);
                material.SetVisible(true, immediate: true);
                material.UpdateLayout();

                var width = (int)Math.Ceiling(material.ActualWidth * scale);
                var height = (int)Math.Ceiling(material.ActualHeight * scale);
                var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
                bitmap.Render(material);
                var stride = width * 4;
                var pixels = new byte[stride * height];
                bitmap.CopyPixels(pixels, stride, 0);
                var maxAlpha = 0;
                for (var offset = 3; offset < pixels.Length; offset += 4)
                    maxAlpha = Math.Max(maxAlpha, pixels[offset]);

                // The light shadow uses 16% opacity. Any darker pixel comes from the caster.
                if (SystemParameters.HighContrast)
                    Assert.Equal(0, maxAlpha);
                else
                    Assert.InRange(maxAlpha, 1, (int)Math.Ceiling(255 * 0.16));
            }
            finally { owner.Close(); }
        });
    }
}
