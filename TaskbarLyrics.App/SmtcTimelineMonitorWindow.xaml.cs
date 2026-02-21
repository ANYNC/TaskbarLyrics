using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace TaskbarLyrics.App;

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
        builder.AppendLine($"Captured(UTC):     {diagnostics.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine($"SourceAppId:       {diagnostics.SourceAppUserModelId}");
        builder.AppendLine($"NormalizedSource:  {diagnostics.NormalizedSource}");
        builder.AppendLine($"ResolvedSource:    {diagnostics.ResolvedSource}");
        builder.AppendLine($"LyricSource:       {_provider.GetCurrentLyricSource()}");
        builder.AppendLine($"IsPlaying:         {diagnostics.IsPlaying}");
        builder.AppendLine($"IsFallback:        {diagnostics.IsFallbackSnapshot}");
        builder.AppendLine();
        builder.AppendLine($"RawPosition:       {FormatTimeSpan(diagnostics.RawPosition)}");
        builder.AppendLine($"LastUpdatedTime:   {diagnostics.LastUpdatedTimeUtc:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine($"LastUpdateAge:     {FormatTimeSpan(diagnostics.LastUpdateAge)}");
        builder.AppendLine($"Extrapolated:      {FormatTimeSpan(diagnostics.ExtrapolatedPosition)}");
        builder.AppendLine($"Extrapolated-Raw:  {FormatTimeSpan(drift)}");
        builder.AppendLine($"SelectedPosition:  {FormatTimeSpan(diagnostics.SelectedPosition)}");
        builder.AppendLine($"Strategy:          {diagnostics.StrategyName}");
        builder.AppendLine();
        builder.AppendLine($"Title:             {diagnostics.Title}");
        builder.AppendLine($"Artist:            {diagnostics.Artist}");

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
