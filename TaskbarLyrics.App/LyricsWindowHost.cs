using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;
using Windows.UI.ViewManagement;

namespace TaskbarLyrics.App;

internal sealed class LyricsWindowHost : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private readonly Action _openSettings;
    private readonly Action _toggleTranslation;
    private readonly Action<FloatingWindowSize> _saveFloatingSize;
    private FloatingWindowSize? _pendingSizeCommit;
    private Dispatcher? _dispatcher;
    private MainWindow? _window;
    private TrackLyricOffsetStore? _trackLyricOffsetStore;
    private IAppCompositionRoot? _compositionRoot;
    private readonly Dictionary<string, LyricsMirrorWindow> _mirrorWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _previewGate = new();
    private AppSettings? _pendingPreviewSettings;
    private bool _previewScheduled;
    private AppSettings _currentSettings = new();
    private bool _disposed;
    private volatile bool _isVisible;
    private bool _isLyricsContentVisible = true;
    private int _startupAbandoned;
    private Task? _dispatcherShutdown;
    private UISettings? _uiSettings;
    private DisplayEnvironmentObserver? _displayObserver;
    private bool _recreatingWindow;
    private bool _recreationQueued;

    public LyricsWindowHost(
        AppSettings initialSettings,
        TrackLyricOffsetStore trackLyricOffsetStore,
        IAppCompositionRoot compositionRoot,
        Action openSettings,
        Action toggleTranslation,
        Action<FloatingWindowSize> saveFloatingSize)
    {
        _openSettings = openSettings;
        _toggleTranslation = toggleTranslation;
        _saveFloatingSize = saveFloatingSize;
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
        _isVisible = true;
        if (_window is null)
        {
            _displayObserver?.RequestRefresh();
            return;
        }

        ApplySettingsOnWindowThread(_currentSettings);
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
        _isVisible = false;
        if (_window is null)
        {
            return;
        }

        _window.CloseControlPanel();
        _window.Hide();
        foreach (var mirrorWindow in _mirrorWindows.Values)
        {
            mirrorWindow.Hide();
        }
        _isVisible = false;
    });

    public void ResetFloatingPositions() => InvokeAsync(() =>
    {
        _window?.ResetFloatingPositions();
        ApplySettingsOnWindowThread(_currentSettings);
    });

    public void ApplySettings(AppSettings settings)
    {
        var snapshot = settings.Clone();
        lock (_previewGate)
        {
            _pendingPreviewSettings = null;
        }
        InvokeAsync(() => ApplySettingsOnWindowThread(snapshot));
    }

    public void PreviewSettings(AppSettings settings)
    {
        var snapshot = settings.Clone();
        lock (_previewGate)
        {
            if (_disposed || _dispatcher is null ||
                _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            {
                return;
            }

            // A busy lyrics dispatcher should render the newest drag value, not queued old values.
            _pendingPreviewSettings = snapshot;
            if (_previewScheduled)
            {
                return;
            }

            _previewScheduled = true;
            _dispatcher.BeginInvoke((Action)ApplyPendingPreviewOnWindowThread, DispatcherPriority.Render);
        }
    }

    private void ApplyPendingPreviewOnWindowThread()
    {
        AppSettings? snapshot;
        lock (_previewGate)
        {
            snapshot = _pendingPreviewSettings;
            _pendingPreviewSettings = null;
            _previewScheduled = false;
        }

        if (snapshot is not null)
        {
            ApplySettingsOnWindowThread(snapshot);
        }
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

    public void ApplyUiLanguage() => InvokeAsync(() =>
    {
        _window?.ReloadForUiLanguage();
        foreach (var mirror in _mirrorWindows.Values)
        {
            mirror.ReloadForUiLanguage();
        }
    });

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

    public void Close() => TaskObserver.Observe(CloseAsync(), "lyrics window host shutdown");

    public Task CloseAsync()
    {
        lock (_previewGate)
        {
            if (_disposed) return _stopped.Task;
            _disposed = true;
            _pendingPreviewSettings = null;
        }

        if (_dispatcher is { HasShutdownStarted: false, HasShutdownFinished: false } dispatcher)
            TaskObserver.Observe(dispatcher.InvokeAsync(CloseOnWindowThreadAsync).Task.Unwrap(), "lyrics window close");
        return _stopped.Task;
    }

    private async Task CloseOnWindowThreadAsync()
    {
        try
        {
            _displayObserver?.Dispose();
            _displayObserver = null;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            CloseMirrorWindows();
            _window?.CloseFromHost();
        }
        finally
        {
            await ShutdownDispatcherAsync();
        }
    }

    private Task ShutdownDispatcherAsync() => _dispatcherShutdown ??= ShutdownDispatcherCoreAsync();

    private async Task ShutdownDispatcherCoreAsync()
    {
        try
        {
            // The native queue needs this STA's message pump until its completion callback returns.
            await FloatingMaterialCompositor.WaitForPendingShutdownAsync();
        }
        finally
        {
            _dispatcher!.BeginInvokeShutdown(DispatcherPriority.Normal);
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
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(_dispatcher));
            _uiSettings = new UISettings();
            _uiSettings.TextScaleFactorChanged += OnTextScaleFactorChanged;
            _trackLyricOffsetStore = trackLyricOffsetStore;
            _compositionRoot = compositionRoot;
            _displayObserver = new DisplayEnvironmentObserver(RefreshDisplayEnvironment);
            _window = CreateAndWireLyricsWindow();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            ApplySettingsOnWindowThread(initialSettings);

            if (Volatile.Read(ref _startupAbandoned) != 0)
            {
                Close();
            }
            else _ready.TrySetResult();
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            Log.Error($"Lyrics window initialization failed: {exception}");
            _ready.TrySetException(exception);
            if (_dispatcher is { HasShutdownStarted: false, HasShutdownFinished: false })
            {
                TaskObserver.Observe(CloseOnWindowThreadAsync(), "lyrics initialization cleanup");
                Dispatcher.Run();
            }
        }
        finally
        {
            _displayObserver?.Dispose();
            _displayObserver = null;
            if (_uiSettings is not null) _uiSettings.TextScaleFactorChanged -= OnTextScaleFactorChanged;
            _uiSettings = null;
            _stopped.TrySetResult();
        }
    }

    private MainWindow CreateAndWireLyricsWindow()
    {
        var window = new MainWindow(_trackLyricOffsetStore!, _compositionRoot!, _openSettings, _toggleTranslation);
        window.PresentationCommandCreated += OnPresentationCommandCreated;
        window.LyricsContentVisibilityChanged += OnLyricsContentVisibilityChanged;
        window.RecreateWindowRequested += OnLyricsWindowRecreateRequested;
        window.FloatingSizePreviewRequested += OnFloatingSizePreviewRequested;
        window.FloatingSizeCommitted += OnFloatingSizeCommitted;
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
        window.FloatingSizePreviewRequested -= OnFloatingSizePreviewRequested;
        window.FloatingSizeCommitted -= OnFloatingSizeCommitted;
        window.IsVisibleChanged -= OnLyricsWindowIsVisibleChanged;
        window.Closed -= OnLyricsWindowClosed;
    }

    private void OnLyricsWindowIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        // Explorer hiding/destroying its child must not overwrite user intent.
        if (_window?.IsVisible == true) _isVisible = true;
        else if (System.Windows.Application.Current is App { UserWantsLyricsVisible: false }) _isVisible = false;
    }

    private void OnLyricsWindowClosed(object? sender, EventArgs e)
    {
        if (_disposed || sender is not MainWindow window || window != _window) return;
        UnwireLyricsWindow(window);
        _window = null;
        Log.Diagnostic("EMBED", "Lyrics window closed unexpectedly; scheduling display recovery.");
        _displayObserver?.RequestRefresh();
    }

    private void OnLyricsWindowRecreateRequested(object? sender, EventArgs e)
    {
        // Defer until the current anchor/settings call stack completes on the old window.
        if (_disposed || _recreationQueued || sender != _window || _dispatcher is null) return;
        _displayObserver?.RequestRefresh();
        _recreationQueued = true;
        TaskObserver.Observe(_dispatcher.InvokeAsync(async () =>
        {
            _recreationQueued = false;
            await RecreateLyricsWindowAsync();
        }, DispatcherPriority.Normal).Task.Unwrap(), "lyrics window recreation");
    }

    private async Task RecreateLyricsWindowAsync()
    {
        if (_disposed || _recreatingWindow)
        {
            return;
        }

        _recreatingWindow = true;
        try
        {
            var oldWindow = _window;
            if (LyricsDisplayDiagnostics.Enabled)
                Log.Diagnostic("DISPLAY_RECOVERY", $"RecreateBegin OldWindow={oldWindow?.DisplayDiagnosticId} UserVisible={_isVisible}");
            _window = null;
            if (oldWindow is not null)
            {
                UnwireLyricsWindow(oldWindow);
                oldWindow.CloseFromHost();
            }
            await FloatingMaterialCompositor.WaitForPendingShutdownAsync();
            if (_disposed) return;
            _window = CreateAndWireLyricsWindow();
            foreach (var mirror in _mirrorWindows.Values) _window.RegisterFloatingLyricsHost(mirror);
            ApplySettingsOnWindowThread(_currentSettings);
            if (_isVisible) _window.Show();
            _window.RecordDisplayDiagnostics("RecreateCompleted", capture: true);
        }
        finally { _recreatingWindow = false; }
    }

    private void OnFloatingSizePreviewRequested(FloatingWindowSize size, Window source, Rect anchor)
    {
        // Preview only the existing hosts. Restoring saved placement or replaying content here
        // fights the gesture's positioning and needlessly reconfigures WebView on every move.
        _currentSettings = size.ApplyTo(_currentSettings);
        _window?.ApplySettings(_currentSettings, keepControlPanelOpen: true, preserveFloatingPosition: true,
            floatingResizeAnchor: source == _window ? anchor : null);
        foreach (var mirror in _mirrorWindows.Values)
            mirror.ApplySettings(_currentSettings, keepControlPanelOpen: true, preserveFloatingPosition: true,
                floatingResizeAnchor: source == mirror ? anchor : null);
    }

    private void OnFloatingSizeCommitted(FloatingWindowSize size)
    {
        _pendingSizeCommit = size;
        _saveFloatingSize(size);
    }

    private void ApplySettingsOnWindowThread(AppSettings settings, bool keepControlPanelOpen = false)
    {
        if (_disposed) return;
        _currentSettings = settings.Clone();
        _currentSettings.NormalizeDisplaySelection();
        if (_window is null)
        {
            return;
        }

        if (!keepControlPanelOpen)
        {
            _window.CancelFloatingSizeAdjustment();
            keepControlPanelOpen = settings.UseFloatingWindow &&
                _pendingSizeCommit == FloatingWindowSize.FromSettings(settings);
            _pendingSizeCommit = null;
        }
        var displays = DisplayMonitorService.GetDisplays();
        var targets = LyricsDisplayTargetSelector.Select(
            displays,
            _currentSettings.LyricsDisplayMode,
            _currentSettings.SelectedDisplayIds);
        if (LyricsDisplayDiagnostics.Enabled)
            Log.Diagnostic("DISPLAY_TARGET", $"Window={_window.DisplayDiagnosticId} Mode={_currentSettings.LyricsDisplayMode} " +
                $"Selected='{string.Join(";", _currentSettings.SelectedDisplayIds)}' " +
                $"Available='{string.Join(";", displays.Select(display => $"{display.Id}|Primary={display.IsPrimary}|{display.Bounds}|Dpi={display.PixelsPerDip}"))}' " +
                $"Targets='{string.Join(";", targets.Select(display => display.Id))}'");
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
        _window.ApplySettings(_currentSettings, keepControlPanelOpen);
        ReconcileMirrorWindows(targets, sourceDisplay.Id, keepControlPanelOpen);
        if (sourceDisplayChanged || _isVisible)
        {
            _window.ReplayPresentationState();
        }
    }

    private void ReconcileMirrorWindows(IReadOnlyList<DisplayMonitor> targets, string sourceDisplayId, bool keepControlPanelOpen = false)
    {
        var mirrorDisplays = targets
            .Where(display => !string.Equals(display.Id, sourceDisplayId, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(display => display.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in _mirrorWindows.Keys.Where(id => !mirrorDisplays.ContainsKey(id)).ToList())
        {
            var staleWindow = _mirrorWindows[staleId];
            _mirrorWindows.Remove(staleId);
            _window?.CloseControlPanel();
            staleWindow.Close();
        }

        foreach (var display in mirrorDisplays.Values)
        {
            if (_mirrorWindows.TryGetValue(display.Id, out var existingMirror) &&
                (existingMirror.NeedsWindowReplacement ||
                 (_currentSettings.UseFloatingWindow && existingMirror.IsEmbeddedInTaskbar)))
            {
                // A mirror that leaves cross-process taskbar embedding can no longer
                // composite as a top-level layered window; replace it with a fresh one.
                _mirrorWindows.Remove(display.Id);
                _window?.CloseControlPanel();
                existingMirror.Close();
            }

            if (!_mirrorWindows.TryGetValue(display.Id, out var mirrorWindow))
            {
                mirrorWindow = new LyricsMirrorWindow(display, (owner, bounds, monitor, setExpanded, area, activation) =>
                    _window?.ToggleControlPanelNear(bounds, monitor, setExpanded, owner, area, activation), _compositionRoot!,
                    () => _window?.CloseControlPanel());
                _mirrorWindows.Add(display.Id, mirrorWindow);
                mirrorWindow.RecreateWindowRequested += OnMirrorWindowRecreateRequested;
                mirrorWindow.Closed += OnMirrorWindowClosed;
                _window?.RegisterFloatingLyricsHost(mirrorWindow);
            }
            else
            {
                mirrorWindow.SetDisplayMonitor(display);
            }

            mirrorWindow.ApplySettings(_currentSettings, keepControlPanelOpen);
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

    private void RefreshDisplayEnvironment()
    {
        if (LyricsDisplayDiagnostics.Enabled)
            Log.Diagnostic("DISPLAY_RECOVERY", $"Refresh Window={_window?.DisplayDiagnosticId} UserVisible={_isVisible} " +
                $"Disposed={_disposed} Recreating={_recreatingWindow} Queued={_recreationQueued}");
        if (_disposed || _recreatingWindow || _recreationQueued) return;
        if (_window is null)
        {
            TaskObserver.Observe(RecreateLyricsWindowAsync(), "lyrics display recovery");
            return;
        }
        ApplySettingsOnWindowThread(_currentSettings);
        _window?.QueueDisplayLayoutRefresh();
        _window?.RecordDisplayDiagnostics("RecoveryPass", capture: true);
        foreach (var mirror in _mirrorWindows.Values) mirror.RecordDisplayDiagnostics("RecoveryPass", capture: true);
    }

    private void OnMirrorWindowRecreateRequested(object? sender, EventArgs e) => _displayObserver?.RequestRefresh();

    private void OnMirrorWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not LyricsMirrorWindow mirror) return;
        mirror.RecreateWindowRequested -= OnMirrorWindowRecreateRequested;
        mirror.Closed -= OnMirrorWindowClosed;
        var id = _mirrorWindows.FirstOrDefault(pair => pair.Value == mirror).Key;
        if (id is null) return;
        _mirrorWindows.Remove(id);
        if (!_disposed)
        {
            _window?.CloseControlPanel();
            _displayObserver?.RequestRefresh();
        }
    }

    private void OnTextScaleFactorChanged(UISettings sender, object args) => InvokeAsync(() =>
    {
        _window?.QueueDisplayLayoutRefresh();
        foreach (var mirror in _mirrorWindows.Values)
            mirror.ApplySettings(_currentSettings, keepControlPanelOpen: true, preserveFloatingPosition: true);
    });

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Taskbar size and edge changes update the work area without a resolution change.
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.Window or UserPreferenceCategory.General)
            InvokeAsync(() => _displayObserver?.RequestRefresh());
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
            mirrorWindow.RecreateWindowRequested -= OnMirrorWindowRecreateRequested;
            mirrorWindow.Closed -= OnMirrorWindowClosed;
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
