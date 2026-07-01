using Media = System.Windows.Media;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace TaskbarLyrics.Light.App;

internal static class CoverAccentExtractor
{
    public static Media.Color? TryExtract(byte[]? imageBytes)
    {
        if (imageBytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var image = ImageSharpImage.Load<Rgba32>(imageBytes);
            var stepX = Math.Max(1, image.Width / 24);
            var stepY = Math.Max(1, image.Height / 24);
            double totalWeight = 0;
            double r = 0;
            double g = 0;
            double b = 0;

            for (var y = stepY / 2; y < image.Height; y += stepY)
            {
                var row = image.DangerousGetPixelRowMemory(y).Span;
                for (var x = stepX / 2; x < image.Width; x += stepX)
                {
                    var pixel = row[x];
                    if (pixel.A < 24)
                    {
                        continue;
                    }

                    var pr = pixel.R / 255.0;
                    var pg = pixel.G / 255.0;
                    var pb = pixel.B / 255.0;
                    var max = Math.Max(pr, Math.Max(pg, pb));
                    var min = Math.Min(pr, Math.Min(pg, pb));
                    var saturation = max <= 0 ? 0 : (max - min) / max;
                    var luminance = (0.2126 * pr) + (0.7152 * pg) + (0.0722 * pb);
                    var weight = 0.25 + saturation + (0.35 * (1 - Math.Abs(luminance - 0.55)));
                    r += pixel.R * weight;
                    g += pixel.G * weight;
                    b += pixel.B * weight;
                    totalWeight += weight;
                }
            }

            if (totalWeight <= 0)
            {
                return null;
            }

            return Media.Color.FromRgb(
                (byte)Math.Clamp(Math.Round(r / totalWeight), 0, 255),
                (byte)Math.Clamp(Math.Round(g / totalWeight), 0, 255),
                (byte)Math.Clamp(Math.Round(b / totalWeight), 0, 255));
        }
        catch
        {
            return null;
        }
    }
}
