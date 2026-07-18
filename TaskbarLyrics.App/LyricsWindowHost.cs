using System.Threading;
using System.Windows.Threading;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.App;

internal sealed class LyricsWindowHost : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private MainWindow? _window;
    private bool _disposed;
    private volatile bool _isVisible;

    public LyricsWindowHost(AppSettings initialSettings, TrackLyricOffsetStore trackLyricOffsetStore)
    {
        var settings = initialSettings.Clone();
        _thread = new Thread(() => Run(settings, trackLyricOffsetStore))
        {
            IsBackground = true,
            Name = "TaskbarLyrics Lyrics UI"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public bool IsVisible => _isVisible;

    public void Show() => InvokeAsync(() =>
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        _isVisible = true;
    });

    public void Hide() => InvokeAsync(() =>
    {
        if (_window is null)
        {
            return;
        }

        _window.Hide();
        _isVisible = false;
    });

    public void ApplySettings(AppSettings settings)
    {
        var snapshot = settings.Clone();
        InvokeAsync(() => _window?.ApplySettings(snapshot));
    }

    public void ApplySpectrumTuning(SpectrumTuningSettings settings)
    {
        var snapshot = settings.Clone();
        InvokeAsync(() => _window?.ApplySpectrumTuning(snapshot));
    }

    public void SetSpectrumDisplayMode(bool enabled, SpectrumDisplayMode mode)
    {
        InvokeAsync(() => _window?.SetSpectrumDisplayMode(enabled, mode));
    }

    public void SetSpectrumPreviewEnabled(bool enabled)
    {
        InvokeAsync(() => _window?.SetSpectrumPreviewEnabled(enabled));
    }

    public void OpenSmtcTimelineMonitorWindow()
    {
        InvokeAsync(() => _window?.OpenSmtcTimelineMonitorWindow());
    }

    public Task<CurrentTrackLyricsContext?> GetCurrentTrackLyricsContextAsync()
    {
        if (_disposed || _dispatcher is null)
        {
            return Task.FromResult<CurrentTrackLyricsContext?>(null);
        }

        return _dispatcher.InvokeAsync(
            () => _window?.GetCurrentTrackLyricsContextSnapshot(),
            DispatcherPriority.Normal).Task;
    }

    public void Close()
    {
        if (_disposed)
        {
            return;
        }

        InvokeAsync(() =>
        {
            _window?.Close();
            Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        });
        _disposed = true;

        if (!_thread.Join(TimeSpan.FromMilliseconds(200)))
        {
            _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
        }
    }

    public void Dispose()
    {
        Close();
        _ready.Dispose();
    }

    private void Run(AppSettings initialSettings, TrackLyricOffsetStore trackLyricOffsetStore)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _window = new MainWindow(trackLyricOffsetStore);
        _window.ApplySettings(initialSettings);
        _window.IsVisibleChanged += (_, _) => _isVisible = _window.IsVisible;
        _window.Closed += (_, _) =>
        {
            _isVisible = false;
            Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        };

        _ready.Set();
        Dispatcher.Run();
    }

    private void InvokeAsync(Action action)
    {
        if (_disposed || _dispatcher is null)
        {
            return;
        }

        _dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }
}
