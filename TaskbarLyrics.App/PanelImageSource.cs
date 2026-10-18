using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace TaskbarLyrics.App;

internal static class PanelImageSource
{
    internal static async Task<ImageSource?> LoadAsync(string? dataUri)
    {
        var image = Load(dataUri);
        if (image is not null || dataUri is null || !dataUri.StartsWith("data:image/webp;base64,", StringComparison.Ordinal)) return image;
        try
        {
            var bytes = Convert.FromBase64String(dataUri[(dataUri.IndexOf(',') + 1)..]);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
            bitmap.CopyToBuffer(pixels.AsBuffer());
            var result = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, bitmap.PixelWidth * 4);
            result.Freeze(); return result;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or System.Runtime.InteropServices.COMException)
        { return null; }
    }

    internal static ImageSource? Load(string? dataUri)
    {
        if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith("data:image/", StringComparison.Ordinal)) return null;
        try
        {
            var comma = dataUri.IndexOf(',');
            if (comma < 0) return null;
            var header = dataUri[..comma];
            var body = dataUri[(comma + 1)..];
            var bytes = header.EndsWith(";base64", StringComparison.Ordinal)
                ? Convert.FromBase64String(body) : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(body));
            if (header.StartsWith("data:image/svg+xml", StringComparison.Ordinal)) return LoadPresetSvg(bytes);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or IOException or XmlException)
        { return null; }
    }

    // The application's SVG presets consist only of filled paths. External XML resources are disabled.
    private static DrawingImage LoadPresetSvg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var root = XElement.Load(reader);
        var viewBox = ((string?)root.Attribute("viewBox") ?? "0 0 24 24").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        if (viewBox.Length != 4 || viewBox[2] <= 0 || viewBox[3] <= 0) throw new FormatException("Invalid preset viewport");
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(viewBox[0], viewBox[1], viewBox[2], viewBox[3]))));
        foreach (var path in root.Elements().Where(e => e.Name.LocalName == "path"))
        {
            var geometry = Geometry.Parse((string?)path.Attribute("d") ?? string.Empty);
            var color = (Color)ColorConverter.ConvertFromString((string?)path.Attribute("fill") ?? "#000000");
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, geometry));
        }
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
}
