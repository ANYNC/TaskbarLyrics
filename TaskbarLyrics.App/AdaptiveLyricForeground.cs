using Drawing = System.Drawing;

namespace TaskbarLyrics.App;

internal readonly record struct LyricBackgroundContrast(double Dark, double Light);

internal sealed class AdaptiveLyricForeground
{
    private const double RequiredAdvantage = 0.25;
    private const long StabilityMilliseconds = 300;
    private const long BackgroundExpiryMilliseconds = 1800;
    private const long MaximumConfirmationGapMilliseconds = 600;
    private const double MainTextOpacity = 0.90 * 0.98;
    private static readonly double[] Linear = Enumerable.Range(0, 256).Select(value =>
    {
        var channel = value / 255d;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }).ToArray();
    private static readonly Drawing.Color DarkText =
        Drawing.Color.FromArgb(unchecked((int)Convert.ToUInt32(AppSettings.DarkForegroundColor[1..], 16)));
    private static readonly Drawing.Color LightText =
        Drawing.Color.FromArgb(unchecked((int)Convert.ToUInt32(AppSettings.LightForegroundColor[1..], 16)));

    private bool? _currentDarkText;
    private bool? _pendingDarkText;
    private long _pendingSince;
    private long? _lastReading;

    public bool IsConfirmingChange => _pendingDarkText.HasValue;

    public bool Observe(LyricBackgroundContrast? background, long now, bool fallbackDarkText)
    {
        if (background is not { } contrast)
        {
            _pendingDarkText = null;
            if (_lastReading is not { } last || now - last >= BackgroundExpiryMilliseconds)
                _currentDarkText = fallbackDarkText;
            return _currentDarkText ?? fallbackDarkText;
        }

        if (_lastReading is { } previous && now - previous > MaximumConfirmationGapMilliseconds)
            _pendingDarkText = null;
        _lastReading = now;
        var useDarkText = _currentDarkText ?? fallbackDarkText;
        var advantage = contrast.Dark - contrast.Light;
        if (Math.Abs(advantage) >= RequiredAdvantage)
        {
            useDarkText = advantage > 0;
        }

        if (_currentDarkText is null)
        {
            _currentDarkText = useDarkText;
            return useDarkText;
        }

        if (_currentDarkText == useDarkText)
        {
            _pendingDarkText = null;
            return useDarkText;
        }

        if (_pendingDarkText != useDarkText)
        {
            _pendingDarkText = useDarkText;
            _pendingSince = now;
        }
        else if (now - _pendingSince >= StabilityMilliseconds)
        {
            _currentDarkText = useDarkText;
            _pendingDarkText = null;
        }

        return _currentDarkText.Value;
    }

    public static LyricBackgroundContrast Measure(Drawing.Color background, double opacity = MainTextOpacity) =>
        new(Contrast(DarkText, background, opacity), Contrast(LightText, background, opacity));

    public static double LowerContrast(List<double> values)
    {
        values.Sort();
        return values[(values.Count - 1) / 10];
    }

    private static double Contrast(Drawing.Color foreground, Drawing.Color background, double opacity)
    {
        byte Blend(byte foreground, byte background) =>
            (byte)Math.Round(background + (foreground - background) * opacity);

        var foregroundLuminance = Luminance(
            Blend(foreground.R, background.R), Blend(foreground.G, background.G), Blend(foreground.B, background.B));
        var backgroundLuminance = Luminance(background.R, background.G, background.B);
        return (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05) /
            (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
    }

    private static double Luminance(byte red, byte green, byte blue) =>
        0.2126 * Linear[red] + 0.7152 * Linear[green] + 0.0722 * Linear[blue];
}
