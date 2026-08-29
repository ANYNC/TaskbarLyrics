using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed class LyricsWindowHost : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private MainWindow? _window;
    private TrackLyricOffsetStore? _trackLyricOffsetStore;
    private IAppCompositionRoot? _compositionRoot;
    private readonly Dictionary<string, LyricsMirrorWindow> _mirrorWindows = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _currentSettings = new();
    private bool _disposed;
    private volatile bool _isVisible;
    private bool _isLyricsContentVisible = true;
    private int _startupAbandoned;

    public LyricsWindowHost(
        AppSettings initialSettings,
        TrackLyricOffsetStore trackLyricOffsetStore,
        IAppCompositionRoot compositionRoot)
    {
        var settings = initialSettings.Clone();
        _thread = new Thread(() => Run(settings, trackLyricOffsetStore, compositionRoot))
        {
            IsBackground = true,
            Name = "TaskbarLyrics Lyrics UI"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Task.Wait(StartupTimeout))
        {
            Interlocked.Exchange(ref _startupAbandoned, 1);
            throw new TimeoutException($"Lyrics window initialization exceeded {StartupTimeout.TotalSeconds:0} seconds.");
        }

        _ready.Task.GetAwaiter().GetResult();
    }

    public bool IsVisible => _isVisible;

    public void Show() => InvokeAsync(() =>
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        foreach (var mirrorWindow in _mirrorWindows.Values)
        {
            mirrorWindow.Show();
        }
        _window.ReplayPresentationState();
        _isVisible = true;
    });

    public void Hide() => InvokeAsync(() =>
    {
        if (_window is null)
        {
            return;
        }

        _window.Hide();
        foreach (var mirrorWindow in _mirrorWindows.Values)
        {
            mirrorWindow.Hide();
        }
        _isVisible = false;
    });

    public void ApplySettings(AppSettings settings)
    {
        var snapshot = settings.Clone();
        InvokeAsync(() => ApplySettingsOnWindowThread(snapshot));
    }

    public void ApplySpectrumTuning(SpectrumTuningSettings settings)
    {
        var snapshot = settings.Clone();
        InvokeAsync(() => _window?.ApplySpectrumTuning(snapshot));
    }

    public void SetSpectrumPreviewEnabled(bool enabled)
    {
        InvokeAsync(() => _window?.SetSpectrumPreviewEnabled(enabled));
    }

    public void RetrySpectrumCapture()
    {
        InvokeAsync(() => _window?.RetrySpectrumCapture());
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

    public Task<bool> TryApplyResolvedLyricsAsync(
        TrackInfo track,
        ResolvedLyrics resolved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(resolved);
        if (_disposed || _dispatcher is null)
        {
            return Task.FromResult(false);
        }

        return _dispatcher.InvokeAsync(
            () => _window?.TryApplyResolvedLyrics(track, resolved) ?? false,
            DispatcherPriority.Normal,
            cancellationToken).Task;
    }

    public Task ExecuteMediaHotkeyAsync(MediaHotkeyAction action, CancellationToken cancellationToken)
    {
        return InvokeAsync(
            () => _window?.ExecuteMediaHotkeyAsync(action, cancellationToken) ?? Task.CompletedTask,
            cancellationToken);
    }

    public void Close()
    {
        if (_disposed)
        {
            return;
        }

        InvokeAsync(() =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            CloseMirrorWindows();
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
    }

    private void Run(
        AppSettings initialSettings,
        TrackLyricOffsetStore trackLyricOffsetStore,
        IAppCompositionRoot compositionRoot)
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _trackLyricOffsetStore = trackLyricOffsetStore;
            _compositionRoot = compositionRoot;
            _window = CreateAndWireLyricsWindow();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            ApplySettingsOnWindowThread(initialSettings);

            if (Volatile.Read(ref _startupAbandoned) != 0)
            {
                _window.Close();
                return;
            }

            _ready.TrySetResult();
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            Log.Error($"Lyrics window initialization failed: {exception}");
            _ready.TrySetException(exception);
        }
    }

    private MainWindow CreateAndWireLyricsWindow()
    {
        var window = new MainWindow(_trackLyricOffsetStore!, _compositionRoot!);
        window.PresentationCommandCreated += OnPresentationCommandCreated;
        window.LyricsContentVisibilityChanged += OnLyricsContentVisibilityChanged;
        window.RecreateWindowRequested += OnLyricsWindowRecreateRequested;
        window.IsVisibleChanged += OnLyricsWindowIsVisibleChanged;
        window.Closed += OnLyricsWindowClosed;
        _isLyricsContentVisible = window.IsLyricsContentVisible;
        return window;
    }

    private void UnwireLyricsWindow(MainWindow window)
    {
        window.PresentationCommandCreated -= OnPresentationCommandCreated;
        window.LyricsContentVisibilityChanged -= OnLyricsContentVisibilityChanged;
        window.RecreateWindowRequested -= OnLyricsWindowRecreateRequested;
        window.IsVisibleChanged -= OnLyricsWindowIsVisibleChanged;
        window.Closed -= OnLyricsWindowClosed;
    }

    private void OnLyricsWindowIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_window is not null)
        {
            _isVisible = _window.IsVisible;
        }
    }

    private void OnLyricsWindowClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        CloseMirrorWindows();
        _isVisible = false;
        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
    }

    private void OnLyricsWindowRecreateRequested(object? sender, EventArgs e)
    {
        // Defer until the current anchor/settings call stack completes on the old window.
        _dispatcher?.BeginInvoke(RecreateLyricsWindow, DispatcherPriority.Normal);
    }

    private void RecreateLyricsWindow()
    {
        if (_disposed || _window is null)
        {
            return;
        }

        var oldWindow = _window;
        _window = null;
        UnwireLyricsWindow(oldWindow);
        oldWindow.CloseForRecreation();
        _window = CreateAndWireLyricsWindow();
        ApplySettingsOnWindowThread(_currentSettings);
        if (_isVisible)
        {
            _window.Show();
        }
    }

    private void ApplySettingsOnWindowThread(AppSettings settings)
    {
        if (_window is null)
        {
            return;
        }

        _currentSettings = settings.Clone();
        _currentSettings.NormalizeDisplaySelection();
        var targets = LyricsDisplayTargetSelector.Select(
            DisplayMonitorService.GetDisplays(),
            _currentSettings.LyricsDisplayMode,
            _currentSettings.SelectedDisplayIds);
        if (targets.Count == 0)
        {
            return;
        }

        var sourceDisplay = targets.FirstOrDefault(display => display.IsPrimary) ?? targets[0];
        var sourceDisplayChanged = !string.Equals(
            _window.DisplayMonitor?.Id,
            sourceDisplay.Id,
            StringComparison.OrdinalIgnoreCase);
        _window.SetDisplayMonitor(sourceDisplay);
        _window.ApplySettings(_currentSettings);
        ReconcileMirrorWindows(targets, sourceDisplay.Id);
        if (sourceDisplayChanged || _isVisible)
        {
            _window.ReplayPresentationState();
        }
    }

    private void ReconcileMirrorWindows(IReadOnlyList<DisplayMonitor> targets, string sourceDisplayId)
    {
        var mirrorDisplays = targets
            .Where(display => !string.Equals(display.Id, sourceDisplayId, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(display => display.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in _mirrorWindows.Keys.Where(id => !mirrorDisplays.ContainsKey(id)).ToList())
        {
            var staleWindow = _mirrorWindows[staleId];
            _mirrorWindows.Remove(staleId);
            staleWindow.Close();
        }

        foreach (var display in mirrorDisplays.Values)
        {
            if (_mirrorWindows.TryGetValue(display.Id, out var existingMirror) &&
                _currentSettings.UseFloatingWindow &&
                existingMirror.IsEmbeddedInTaskbar)
            {
                // A mirror that leaves cross-process taskbar embedding can no longer
                // composite as a top-level layered window; replace it with a fresh one.
                _mirrorWindows.Remove(display.Id);
                existingMirror.Close();
            }

            if (!_mirrorWindows.TryGetValue(display.Id, out var mirrorWindow))
            {
                mirrorWindow = new LyricsMirrorWindow(display);
                _mirrorWindows.Add(display.Id, mirrorWindow);
            }
            else
            {
                mirrorWindow.SetDisplayMonitor(display);
            }

            mirrorWindow.ApplySettings(_currentSettings);
            mirrorWindow.SetContentVisibility(_isLyricsContentVisible);
            if (_isVisible)
            {
                mirrorWindow.Show();
            }
        }
    }

    private void OnPresentationCommandCreated(LyricsPresentationCommand command)
    {
        foreach (var mirrorWindow in _mirrorWindows.Values)
        {
            mirrorWindow.ApplyPresentationCommand(command);
        }
    }

    private void OnLyricsContentVisibilityChanged(
        object? sender,
        LyricsContentVisibilityChangedEventArgs e)
    {
        _isLyricsContentVisible = e.IsVisible;
        foreach (var mirrorWindow in _mirrorWindows.Values)
        {
            mirrorWindow.SetContentVisibility(e.IsVisible);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        InvokeAsync(() => ApplySettingsOnWindowThread(_currentSettings));
    }

    private void CloseMirrorWindows()
    {
        if (_window is not null)
        {
            _window.PresentationCommandCreated -= OnPresentationCommandCreated;
            _window.LyricsContentVisibilityChanged -= OnLyricsContentVisibilityChanged;
        }

        foreach (var mirrorWindow in _mirrorWindows.Values)
        {
            mirrorWindow.Close();
        }

        _mirrorWindows.Clear();
    }

    private void InvokeAsync(Action action)
    {
        if (_disposed || _dispatcher is null)
        {
            return;
        }

        _dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    private Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        if (_disposed || _dispatcher is null)
        {
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
    }
}
