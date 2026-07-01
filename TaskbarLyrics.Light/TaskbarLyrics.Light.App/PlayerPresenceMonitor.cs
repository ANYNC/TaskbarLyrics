using System.Windows.Threading;

namespace TaskbarLyrics.Light.App;

internal sealed class PlayerPresenceMonitor : IDisposable
{
    private readonly SmtcMusicSessionProvider _provider = new();
    private readonly DispatcherTimer _timer;
    private bool _isPlayerActive;
    private bool _isDisposed;

    public PlayerPresenceMonitor()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;
    }

    public event EventHandler<bool>? PresenceChanged;

    public bool IsPlayerActive => _isPlayerActive;

    public void ApplySettings(AppSettings settings)
    {
        _provider.SetRecognitionOrder(
            settings.SourceRecognitionOrder,
            BuildEnabledSources(settings));
    }

    public void Start()
    {
        if (_isDisposed)
        {
            return;
        }

        if (!_timer.IsEnabled)
        {
            _ = RefreshAsync();
            _timer.Start();
        }
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            var active = await _provider.IsEnabledPlaybackActiveAsync();
            if (active == _isPlayerActive)
            {
                return;
            }

            _isPlayerActive = active;
            PresenceChanged?.Invoke(this, active);
        }
        catch
        {
            // 忽略 SMTC 瞬时异常，下一轮继续检测。
        }
    }

    private static IReadOnlyCollection<string> BuildEnabledSources(AppSettings settings)
    {
        var sources = new List<string>();
        if (settings.EnableQQMusic) sources.Add("QQMusic");
        if (settings.EnableNetease) sources.Add("Netease");
        if (settings.EnableKugou) sources.Add("Kugou");
        if (settings.EnableSpotify) sources.Add("Spotify");
        return sources;
    }
}
