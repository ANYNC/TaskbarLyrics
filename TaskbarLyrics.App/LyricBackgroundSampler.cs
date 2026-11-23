using System.Runtime.InteropServices;
using System.Windows;
using Drawing = System.Drawing;
using Point = System.Windows.Point;

namespace TaskbarLyrics.App;

internal readonly record struct LyricBackgroundText(Rect Bounds, double Opacity);
internal sealed record LyricBackgroundRequest(
    Rect Region, IReadOnlyList<LyricBackgroundText> Text, IReadOnlyList<Rect> InkBounds, bool IsSpectrum);

internal sealed class LyricBackgroundSampler
{
    private const int InlineBands = 24;
    private const int CrossSamples = 20;
    private const long MinimumSampleInterval = 150;
    private const int MaxCapturePixels = 2_000_000;
    private AdaptiveLyricForeground _policy = new();
    private (bool DarkText, bool Confirming)? _published;
    private long _lastSampleTime;
    private int _generation;
    private bool _enabled;
    private bool _sampling;

    public void Configure(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        Reset();
    }

    public void Reset()
    {
        _generation++;
        _policy = new();
        _published = null;
        _lastSampleTime = 0;
    }

    public async Task SampleAsync(
        FrameworkElement webView,
        LyricBackgroundRequest request,
        Func<bool> canSample,
        Func<string, Task?> publish)
    {
        var now = Environment.TickCount64;
        if (!_enabled || _sampling || now - _lastSampleTime < MinimumSampleInterval ||
            !canSample() || !TryGetScreenBounds(webView, out var host))
        {
            return;
        }

        _lastSampleTime = now;
        _sampling = true;
        var generation = _generation;
        try
        {
            var region = ToScreen(request.Region, host);
            var capture = Drawing.Rectangle.Intersect(Drawing.Rectangle.FromLTRB(
                (int)Math.Floor(region.Left), (int)Math.Floor(region.Top),
                (int)Math.Ceiling(region.Right), (int)Math.Ceiling(region.Bottom)),
                System.Windows.Forms.SystemInformation.VirtualScreen);
            var text = request.Text.Select(line => line with { Bounds = ToScreen(line.Bounds, host) }).ToArray();
            var ink = request.InkBounds.Select(bounds => ToScreen(bounds, host)).ToArray();
            var scale = System.Windows.Media.VisualTreeHelper.GetDpi(webView).PixelsPerDip;
            var background = await Task.Run(() => ReadContrast(capture, text, ink, scale, request.IsSpectrum));
            if (!_enabled || _generation != generation || !canSample() ||
                !TryGetScreenBounds(webView, out var currentHost) || currentHost != host)
            {
                return;
            }

            var useDarkText = _policy.Observe(background, Environment.TickCount64, App.IsSystemUiUsingLightTheme());
            var result = (DarkText: useDarkText, Confirming: _policy.IsConfirmingChange);
            if (result != _published &&
                publish(LyricsStyleScriptFactory.SetAdaptiveForeground(result.DarkText, result.Confirming)) is { } sent)
            {
                await sent;
                if (_generation == generation) _published = result;
            }
        }
        finally
        {
            _sampling = false;
        }
    }

    private static bool TryGetScreenBounds(FrameworkElement view, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (!view.IsVisible || PresentationSource.FromVisual(view) is null ||
            view.ActualWidth <= 0 || view.ActualHeight <= 0)
        {
            return false;
        }

        bounds = new Rect(view.PointToScreen(new Point()),
            view.PointToScreen(new Point(view.ActualWidth, view.ActualHeight)));
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static Rect ToScreen(Rect normalized, Rect host) =>
        new(host.X + normalized.X * host.Width, host.Y + normalized.Y * host.Height,
            normalized.Width * host.Width, normalized.Height * host.Height);

    private static LyricBackgroundContrast? ReadContrast(Drawing.Rectangle area, LyricBackgroundText[] text,
        Rect[] ink, double scale, bool isSpectrum)
    {
        if (area.Width <= 0 || area.Height <= 0 || (long)area.Width * area.Height > MaxCapturePixels)
        {
            return null;
        }

        try
        {
            using var bitmap = new Drawing.Bitmap(area.Width, area.Height, Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            {
                if (!TryCopyScreen(graphics, area)) return null;
            }

            return ReadBands(area, bitmap, text, ink, scale, isSpectrum);
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException)
        {
            // Screen geometry or the desktop can change while the frame is being copied.
            return null;
        }
    }

    internal static LyricBackgroundContrast? ReadBands(Drawing.Rectangle area, Drawing.Bitmap bitmap,
        LyricBackgroundText[] text, Rect[] ink, double scale, bool isSpectrum = false)
    {
        var vertical = area.Height > area.Width;
        var inlineSize = vertical ? area.Height : area.Width;
        var crossSize = vertical ? area.Width : area.Height;
        var bandCount = Math.Clamp(inlineSize / 8, 1, InlineBands);
        var horizontalSpectrum = isSpectrum && !vertical;
        var crossCount = Math.Min(crossSize, horizontalSpectrum ? 8 : CrossSamples);
        // Keep narrow spectrum gaps sampleable while excluding the antialiased bar edges.
        var halo = (isSpectrum ? 0.25 : 2) * scale;
        var exclusions = ink.Select(bounds => Rect.Inflate(bounds, halo, halo)).ToArray();
        var nearby = text.Select(line => line with { Bounds = Rect.Inflate(line.Bounds, 8 * scale, 8 * scale) }).ToArray();
        var dark = new List<double>();
        var light = new List<double>();
        var wholeDark = new List<double>();
        var wholeLight = new List<double>();
        var textBands = 0;
        var usableTextBands = 0;

        for (var band = 0; band < bandCount; band++)
        {
            var start = inlineSize * band / bandCount;
            var end = inlineSize * (band + 1) / bandCount;
            var slice = vertical
                ? new Rect(area.Left, area.Top + start, area.Width, end - start)
                : new Rect(area.Left + start, area.Top, end - start, area.Height);
            var hasText = text.Any(line => line.Bounds.IntersectsWith(slice));
            var countBefore = dark.Count;
            if (hasText) textBands++;
            var inlineStep = horizontalSpectrum ? 1 : Math.Max(1, (end - start) / 3);
            for (var inline = start; inline < end; inline += inlineStep)
            {
                // Spectrum masks span the viewport vertically. Visit each column so a
                // regular sampling stride cannot skip every gap in closely spaced bars.
                var screenX = area.Left + inline + 0.5;
                if (horizontalSpectrum && exclusions.Any(mask => screenX >= mask.Left && screenX < mask.Right))
                    continue;
                for (var cross = 0; cross < crossCount; cross++)
                {
                    var offset = crossSize * cross / crossCount;
                    var x = vertical ? offset : inline;
                    var y = vertical ? inline : offset;
                    var point = new Point(area.Left + x + 0.5, area.Top + y + 0.5);
                    if (!horizontalSpectrum && exclusions.Any(mask => mask.Contains(point))) continue;

                    var pixel = bitmap.GetPixel(x, y);
                    var backgroundContrast = AdaptiveLyricForeground.Measure(pixel);
                    wholeDark.Add(backgroundContrast.Dark);
                    wholeLight.Add(backgroundContrast.Light);
                    var opacity = 1d;
                    var nearText = false;
                    if (hasText)
                    {
                        foreach (var line in nearby)
                        {
                            if (!line.Bounds.Contains(point)) continue;
                            nearText = true;
                            opacity = Math.Min(opacity, line.Opacity);
                        }
                    }
                    if (nearText)
                    {
                        var contrast = AdaptiveLyricForeground.Measure(pixel, opacity);
                        dark.Add(contrast.Dark);
                        light.Add(contrast.Light);
                    }
                }
            }

            if (dark.Count > countBefore) usableTextBands++;
        }

        if ((text.Length > 0 && (textBands == 0 || usableTextBands * 2 < textBands)) || wholeDark.Count < 8)
        {
            return null;
        }

        return dark.Count == 0
            ? new(AdaptiveLyricForeground.LowerContrast(wholeDark), AdaptiveLyricForeground.LowerContrast(wholeLight))
            : new(AdaptiveLyricForeground.LowerContrast(dark), AdaptiveLyricForeground.LowerContrast(light));
    }

    private static bool TryCopyScreen(Drawing.Graphics graphics, Drawing.Rectangle area)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return false;
        try
        {
            var destination = graphics.GetHdc();
            try
            {
                const uint sourceCopyWithLayers = 0x40CC0020;
                return BitBlt(destination, 0, 0, area.Width, area.Height, screen,
                    area.Left, area.Top, sourceCopyWithLayers);
            }
            finally
            {
                graphics.ReleaseHdc(destination);
            }
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);
}
