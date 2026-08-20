using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace TaskbarLyrics.Light.App;

public partial class SmtcTimelineMonitorWindow : Window
{
    private readonly SmtcMusicSessionProvider _provider;
    private readonly DispatcherTimer _timer;

    public SmtcTimelineMonitorWindow(SmtcMusicSessionProvider provider)
    {
        InitializeComponent();
        _provider = provider;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _timer.Tick += OnTimerTick;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshView();
        _timer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        RefreshView();
    }

    private void RefreshView()
    {
        var diagnostics = _provider.GetLastTimelineDiagnostics();
        if (diagnostics is null)
        {
            TimelineTextBox.Text = "Waiting for SMTC diagnostics...";
            return;
        }

        var drift = diagnostics.ExtrapolatedPosition - diagnostics.RawPosition;
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Captured(UTC):     {diagnostics.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"SourceAppId:       {diagnostics.SourceAppUserModelId}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"NormalizedSource:  {diagnostics.NormalizedSource}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ResolvedSource:    {diagnostics.ResolvedSource}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"LyricSource:       {_provider.GetCurrentLyricSource()}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"IsPlaying:         {diagnostics.IsPlaying}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"IsFallback:        {diagnostics.IsFallbackSnapshot}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"RawPosition:       {FormatTimeSpan(diagnostics.RawPosition)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"LastUpdatedTime:   {diagnostics.LastUpdatedTimeUtc:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"LastUpdateAge:     {FormatTimeSpan(diagnostics.LastUpdateAge)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Extrapolated:      {FormatTimeSpan(diagnostics.ExtrapolatedPosition)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Extrapolated-Raw:  {FormatTimeSpan(drift)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"SelectedPosition:  {FormatTimeSpan(diagnostics.SelectedPosition)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Strategy:          {diagnostics.StrategyName}");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Title:             {diagnostics.Title}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Artist:            {diagnostics.Artist}");

        TimelineTextBox.Text = builder.ToString();
    }

    private static string FormatTimeSpan(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : string.Empty;
        var abs = value.Duration();
        return $"{sign}{abs:mm\\:ss\\.fff}";
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TimelineTextBox.Text))
        {
            return;
        }

        System.Windows.Clipboard.SetText(TimelineTextBox.Text);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
