using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TaskbarLyrics.App;

public partial class LyricsControlPanelWindow : Window
{
    public static readonly DependencyProperty ShowKeyboardFocusProperty = DependencyProperty.Register(
        nameof(ShowKeyboardFocus), typeof(bool), typeof(LyricsControlPanelWindow), new PropertyMetadata(false));
    public bool ShowKeyboardFocus
    {
        get => (bool)GetValue(ShowKeyboardFocusProperty);
        private set => SetValue(ShowKeyboardFocusProperty, value);
    }

    private readonly Action<MediaHotkeyAction> _execute;
    private readonly Action<TimeSpan> _seek;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _hideTimer;
    private FloatingMaterialCompositor? _compositor;
    private IFloatingPanelMaterial? _material;
    private double _materialBrightness = AppSettings.DefaultEmbeddedPanelBrightness;
    private double _materialTintOpacity = AppSettings.DefaultEmbeddedPanelTintOpacity;
    private double _materialBlurAmount = AppSettings.DefaultEmbeddedPanelBlurAmount;
    private Window? _hoverSource;
    private Rect _hoverSourceBounds;
    private long? _hoverLeftAt;
    private bool _canDismissOnDeactivate;
    private bool _disposed;
    private bool _seeking;
    private bool _playing;
    private string _trackIdentity = string.Empty;
    private string _seekIdentity = string.Empty;
    private TimeSpan _duration;
    private TimeSpan? _pendingSeek;
    private long _pendingSeekStarted;
    private TimeSpan _position;
    private bool _canSeek;
    private DateTimeOffset _lastTimelinePaintUtc;
    private int _showVersion;
    internal bool IsOpening { get; private set; }
    internal bool IsClosing { get; private set; }
    private bool CanInteract => IsVisible && !IsClosing && !_disposed;
    internal event EventHandler? Hiding;
    internal event EventHandler? Hidden;

    internal LyricsControlPanelWindow(Action<MediaHotkeyAction> execute, Action<TimeSpan> seek)
    {
        InitializeComponent();
        _execute = execute;
        _seek = seek;
        var fontFolder = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar);
        FontFamily = new FontFamily(fontFolder, $"./#{AppSettings.BundledFontFamily}");
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        _hideTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(180) };
        _hoverTimer.Tick += OnHoverTick;
        _hideTimer.Tick += OnHideTick;
        SourceInitialized += OnSourceInitialized;
        Deactivated += OnDeactivated;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        NativeWindowTheme.ThemeChanged += OnThemeChanged;
        SeekSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnSeekStarted), true);
        SeekSlider.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnSeekFinished), true);
        ApplyTheme();
    }

    internal void UpdateTrack(TrackInfo? track, bool isPlaying, string? coverDataUri,
        string? fallbackIconDataUri, bool translationEnabled, PlayerSourceBadge sourceBadge)
    {
        if (_disposed) return;
        var identity = track is null ? string.Empty : LyricSyncService.BuildStableTrackIdentity(track);
        if (_trackIdentity != identity) { _trackIdentity = identity; _seeking = false; _pendingSeek = null; }
        var title = track is null ? "暂无播放" : string.IsNullOrEmpty(track.Title) ? "未知歌曲" : track.Title;
        var artist = track is null ? "当前没有播放内容" : string.IsNullOrEmpty(track.Artist) ? "未知歌手" : track.Artist;
        if (TitleText.Text != title) { TitleText.Text = title; TitleText.ToolTip = title; }
        if (ArtistText.Text != artist) { ArtistText.Text = artist; ArtistText.ToolTip = artist; }
        SourceText.Text = sourceBadge.DisplayName ?? string.Empty;
        SourceRow.Visibility = SourceText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PreviousButton.IsEnabled = PlayButton.IsEnabled = NextButton.IsEnabled = track is not null;
        UpdateArtwork(coverDataUri, fallbackIconDataUri, sourceBadge);
        PaintPlayback(isPlaying);
    }

    private string? _coverUri;
    private string? _sourceUri;
    private void UpdateArtwork(string? cover, string? fallback, PlayerSourceBadge badge)
    {
        var uri = string.IsNullOrEmpty(cover) ? fallback : cover;
        if (_coverUri != uri)
        {
            _coverUri = uri;
            TaskObserver.Observe(LoadCoverAsync(uri, fallback), "taskbar panel artwork");
        }
        if (_sourceUri != badge.IconDataUri)
        {
            _sourceUri = badge.IconDataUri;
            TaskObserver.Observe(LoadSourceAsync(_sourceUri), "taskbar panel source icon");
        }
        SourceMark.Background = badge.UsesDefaultIcon ? Brushes.WhiteSmoke : Brushes.Transparent;
    }

    private async Task LoadCoverAsync(string? uri, string? fallback)
    {
        var image = await PanelImageSource.LoadAsync(uri) ?? await PanelImageSource.LoadAsync(fallback);
        if (!_disposed && _coverUri == uri)
        {
            CoverBrush.ImageSource = image;
            CoverFallback.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async Task LoadSourceAsync(string? uri)
    {
        var image = await PanelImageSource.LoadAsync(uri);
        if (!_disposed && _sourceUri == uri)
        {
            SourceIcon.Source = image;
            SourceFallback.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    internal void UpdateTimeline(TimeSpan position, TimeSpan duration, bool isPlaying, bool canSeek)
    {
        if (_disposed) return;
        _position = position;
        _duration = duration;
        _canSeek = canSeek;
        _playing = isPlaying;
        if (!CanInteract || DateTimeOffset.UtcNow - _lastTimelinePaintUtc < TimeSpan.FromMilliseconds(250)) return;
        PaintTimeline();
    }

    private void PaintTimeline()
    {
        _lastTimelinePaintUtc = DateTimeOffset.UtcNow;
        PaintPlayback(_playing);
        SeekSlider.IsEnabled = PreviousButton.IsEnabled && _canSeek && _duration > TimeSpan.Zero;
        if (!SeekSlider.IsEnabled) { _seeking = false; _pendingSeek = null; }
        if (!_seeking && CanApplyTimeline(_position)) SeekSlider.Value = _duration > TimeSpan.Zero
            ? Math.Clamp(_position.TotalMilliseconds / _duration.TotalMilliseconds * 1000, 0, 1000) : 0;
        PaintTime();
    }

    private void PaintPlayback(bool playing)
    {
        _playing = playing;
        PlayGlyph.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseGlyph.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(PlayButton, playing ? "暂停" : "播放");
    }

    internal void TrackHoverSource(Window source, Rect boundsPx)
    {
        _canDismissOnDeactivate = false;
        _hoverSource = source;
        _hoverSourceBounds = boundsPx;
        _hoverLeftAt = null;
        _hoverTimer.Start();
    }

    internal void ConfigureMaterial(double brightness, double tintOpacity, double blurAmount)
    {
        if (_disposed) return;
        _materialBrightness = brightness;
        _materialTintOpacity = tintOpacity;
        _materialBlurAmount = blurAmount;
        _material?.Configure(NativeWindowTheme.IsLight, brightness, tintOpacity, blurAmount);
    }

    private void OnHoverTick(object? sender, EventArgs e)
    {
        if (_hoverSource is not { IsVisible: true } source) { HidePanel(immediate: true); return; }
        if (!GetCursorPos(out var cursor)) return;
        var point = new Point(cursor.X, cursor.Y);
        var hoveredRoot = GetAncestor(WindowFromPoint(cursor), 2);
        var sourceRoot = GetAncestor(new WindowInteropHelper(source).Handle, 2);
        var panelRoot = new WindowInteropHelper(this).Handle;
        if (hoveredRoot == sourceRoot && _hoverSourceBounds.Contains(point) ||
            hoveredRoot == panelRoot && NativeWindowInteraction.GetScreenBounds(this).Contains(point) ||
            IsActive && Mouse.LeftButton == MouseButtonState.Pressed)
        { _hoverLeftAt = null; return; }
        _hoverLeftAt ??= Environment.TickCount64;
        if (Environment.TickCount64 - _hoverLeftAt >= 350) HidePanel();
    }

    internal async Task ShowNearAsync(Rect anchor, DisplayMonitor display, CoverPosition coverPosition, bool activate = true)
    {
        if (_disposed) return;
        var version = ++_showVersion;
        IsOpening = true;
        IsClosing = false;
        _hideTimer.Stop();
        _canDismissOnDeactivate = false;
        try
        {
            // Preserve cancellation when a source click arrives while preparation is queued.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (_disposed || version != _showVersion) return;
            ApplyTheme();
            var work = display.WorkArea;
            var area = new Rect(work.Left, work.Top, work.Width, work.Height);
            var scale = display.PixelsPerDip;
            var approximate = LyricsControlPanelPlacement.Place(anchor, area, new Size(Width * scale, Height * scale), scale, coverPosition);
            Left = approximate.X / scale; Top = approximate.Y / scale;
            Show();
            var hwnd = new WindowInteropHelper(this).Handle;
            scale = TaskbarNativeMethods.GetDpiForWindow(hwnd) / 96.0;
            if (scale <= 0) scale = display.PixelsPerDip;
            var size = new Size(ActualWidth * scale, ActualHeight * scale);
            var position = LyricsControlPanelPlacement.Place(anchor, area, size, scale, coverPosition);
            TaskbarNativeMethods.SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(position.X), (int)Math.Round(position.Y),
                (int)Math.Round(size.Width), (int)Math.Round(size.Height), TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
            _material ??= CreateMaterial();
            // The material is decorative and must not receive focus or control input.
            _ = EnableWindow(_material.Handle, false);
            _material.Configure(NativeWindowTheme.IsLight, _materialBrightness, _materialTintOpacity, _materialBlurAmount);
            _material.Position(new Rect(position, size), scale, hwnd);
            if (!_material.EnsureBelow(hwnd)) throw new InvalidOperationException("Cannot position taskbar panel material.");
            _material.SetVisible(true);
            NativeWindowInteraction.SetClickThrough(hwnd, false);
            VisualRoot.IsHitTestVisible = true;
            PaintTimeline();
            FadeTo(1);
            _canDismissOnDeactivate = activate;
            if (activate) Activate();
        }
        catch { HidePanel(immediate: true); throw; }
        finally { if (version == _showVersion) IsOpening = false; }
    }

    private IFloatingPanelMaterial CreateMaterial()
    {
        if (!SystemParameters.HighContrast)
        {
            try
            {
                _compositor ??= new FloatingMaterialCompositor();
                var material = new FloatingMaterialWindow(_compositor);
                if (material.UsesBackdrop) return material;
                material.Dispose();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException or DllNotFoundException)
            { Log.Diagnostic("CONTROL_PANEL", $"Native material unavailable: {ex.GetType().Name}"); }
        }
        return new FloatingMaterialFallbackWindow();
    }

    internal void HidePanel(bool immediate = false)
    {
        _hoverTimer.Stop(); _hoverSource = null; _hoverLeftAt = null;
        if (IsClosing && !immediate) return;
        ++_showVersion;
        var opening = IsOpening;
        IsOpening = false;
        _canDismissOnDeactivate = false;
        if (!IsVisible) { if (opening) Hidden?.Invoke(this, EventArgs.Empty); return; }
        IsClosing = true;
        // Let the source affordance fade with the panel, rather than after Hidden.
        Hiding?.Invoke(this, EventArgs.Empty);
        VisualRoot.IsHitTestVisible = false;
        NativeWindowInteraction.SetClickThrough(new WindowInteropHelper(this).Handle, true);
        _seeking = false; _pendingSeek = null;
        if (SeekSlider.IsMouseCaptureWithin) Mouse.Capture(null);
        if (IsKeyboardFocusWithin) Keyboard.ClearFocus();
        ShowKeyboardFocus = false;
        _material?.SetVisible(false, immediate);
        if (immediate || !Animate) { FinishHide(); return; }
        FadeTo(0);
        _hideTimer.Start();
    }

    private static bool Animate => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    private void FadeTo(double opacity)
    {
        var from = VisualRoot.Opacity;
        VisualRoot.BeginAnimation(OpacityProperty, null);
        VisualRoot.Opacity = opacity;
        if (Animate) VisualRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(from, opacity, TimeSpan.FromMilliseconds(180)));
    }
    private void OnHideTick(object? sender, EventArgs e) => FinishHide();
    private void FinishHide()
    {
        _hideTimer.Stop();
        VisualRoot.BeginAnimation(OpacityProperty, null); VisualRoot.Opacity = 0;
        if (!IsVisible) return;
        Hide(); IsClosing = false; Hidden?.Invoke(this, EventArgs.Empty);
    }
    private void OnSourceInitialized(object? sender, EventArgs e) => TaskbarPlacementService.ApplyToolWindowStyle(new WindowInteropHelper(this).Handle);
    private void OnDeactivated(object? sender, EventArgs e) { if (_canDismissOnDeactivate) HidePanel(); }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CanInteract) return;
        ShowKeyboardFocus = true;
        if (e.Key == Key.Escape) { e.Handled = true; HidePanel(); }
    }
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) => ShowKeyboardFocus = false;
    private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplyTheme);
    private void ApplyTheme()
    {
        if (_disposed) return;
        var light = NativeWindowTheme.IsLight;
        Resources["PanelForeground"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(light ? Color.FromRgb(28, 29, 32) : Color.FromRgb(246, 247, 248));
        Resources["PanelSubtle"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(light ? Color.FromRgb(98, 103, 113) : Color.FromRgb(169, 173, 181));
        Resources["PanelLine"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(light ? Color.FromRgb(186, 188, 192) : Color.FromRgb(98, 101, 106));
        Resources["PanelButton"] = SystemParameters.HighContrast ? SystemColors.HighlightBrush : new SolidColorBrush(light ? Color.FromRgb(41, 45, 52) : Color.FromRgb(238, 241, 245));
        Resources["PanelButtonText"] = SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : new SolidColorBrush(light ? Colors.White : Color.FromRgb(32, 33, 36));
        _material?.Configure(light, _materialBrightness, _materialTintOpacity, _materialBlurAmount);
    }

    private void OnPrevious(object sender, RoutedEventArgs e) { if (CanInteract && PreviousButton.IsEnabled) _execute(MediaHotkeyAction.PreviousTrack); }
    private void OnPlay(object sender, RoutedEventArgs e) { if (CanInteract && PlayButton.IsEnabled) _execute(MediaHotkeyAction.TogglePlayPause); }
    private void OnNext(object sender, RoutedEventArgs e) { if (CanInteract && NextButton.IsEnabled) _execute(MediaHotkeyAction.NextTrack); }
    private void OnSeekStarted(object sender, MouseButtonEventArgs e)
    {
        if (!CanInteract || !SeekSlider.IsEnabled) return;
        _seeking = true;
        _seekIdentity = _trackIdentity;
    }
    private void OnSeekFinished(object sender, MouseButtonEventArgs e) => CommitSeek();
    private void OnSeekKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsSeekKey(e.Key))
        { _seeking = true; _seekIdentity = _trackIdentity; }
    }
    private void OnSeekKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_seeking && IsSeekKey(e.Key)) CommitSeek();
    }
    private static bool IsSeekKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down
        or Key.Home or Key.End or Key.PageUp or Key.PageDown;

    private void CommitSeek()
    {
        if (CanInteract && _seeking && SeekSlider.IsEnabled && _seekIdentity == _trackIdentity)
        {
            var target = SliderPosition;
            // Hold the local value while SMTC still reports the position before this seek.
            // Publish the pending target before calling out, including for synchronous updates.
            _pendingSeek = target;
            _pendingSeekStarted = _timeProvider.GetTimestamp();
            _seek(target);
        }
        _seeking = false;
    }

    private bool CanApplyTimeline(TimeSpan position)
    {
        if (_pendingSeek is not { } target) return true;
        var elapsed = _timeProvider.GetElapsedTime(_pendingSeekStarted);
        var expected = Math.Min(_duration.TotalSeconds, target.TotalSeconds + (_playing ? elapsed.TotalSeconds : 0));
        if (elapsed < TimeSpan.FromSeconds(2) && Math.Abs(position.TotalSeconds - expected) > 0.5)
            return false;
        _pendingSeek = null;
        return true;
    }
    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => PaintTime();
    private TimeSpan SliderPosition => TimeSpan.FromMilliseconds(Math.Round(SeekSlider.Value / 1000 * _duration.TotalMilliseconds));
    private void PaintTime()
    {
        if (ElapsedText is null) return;
        var position = SliderPosition;
        ElapsedText.Text = FormatTime(position);
        DurationText.Text = FormatTime(_duration);
        AutomationProperties.SetHelpText(SeekSlider, $"{ElapsedText.Text} / {DurationText.Text}");
    }
    private static string FormatTime(TimeSpan value) => value.ToString(value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
    protected override void OnClosed(EventArgs e)
    {
        _disposed = true; ++_showVersion;
        _hoverTimer.Stop(); _hideTimer.Stop();
        _hoverTimer.Tick -= OnHoverTick; _hideTimer.Tick -= OnHideTick;
        SourceInitialized -= OnSourceInitialized; Deactivated -= OnDeactivated;
        PreviewKeyDown -= OnPreviewKeyDown; PreviewMouseDown -= OnPreviewMouseDown;
        NativeWindowTheme.ThemeChanged -= OnThemeChanged;
        SeekSlider.RemoveHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnSeekStarted));
        SeekSlider.RemoveHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnSeekFinished));
        _material?.Dispose(); _compositor?.Dispose(); _hoverSource = null;
        base.OnClosed(e);
    }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out TaskbarNativeMethods.NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(TaskbarNativeMethods.NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnableWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool enabled);
}
