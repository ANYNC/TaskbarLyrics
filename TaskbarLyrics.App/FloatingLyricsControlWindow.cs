using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;
using Point = System.Windows.Point;

namespace TaskbarLyrics.App;

internal partial class FloatingLyricsControlWindow : Window
{
    public static readonly DependencyProperty ShowKeyboardFocusProperty = DependencyProperty.Register(
        nameof(ShowKeyboardFocus), typeof(bool), typeof(FloatingLyricsControlWindow), new PropertyMetadata(false));

    public bool ShowKeyboardFocus
    {
        get => (bool)GetValue(ShowKeyboardFocusProperty);
        private set => SetValue(ShowKeyboardFocusProperty, value);
    }

    private readonly Action<MediaHotkeyAction> _execute;
    private readonly Action<TimeSpan> _seek;
    private readonly TimeProvider _timeProvider;
    private string _trackIdentity = string.Empty;
    private string _seekIdentity = string.Empty;
    private TimeSpan _duration;
    private bool _seeking;
    private bool _playing;
    private TimeSpan? _pendingSeek;
    private long _pendingSeekStarted;
    internal bool IsClosed { get; private set; }
    internal bool KeepOpenForKeyboard { get; private set; }
    internal bool IsPanelVisible { get; private set; }
    internal event EventHandler? Dismissed;
    internal event EventHandler? DragRequested;
    private Point? _gripPress;

    private void OnGripPressed(object sender, MouseButtonEventArgs e)
    {
        if (!IsPanelVisible) return;
        _gripPress = e.GetPosition(this);
        DragGrip.CaptureMouse();
        e.Handled = true;
    }

    private void OnGripMoved(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_gripPress is not { } start || e.LeftButton != MouseButtonState.Pressed || !IsPanelVisible) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _gripPress = null;
        DragGrip.ReleaseMouseCapture();
        e.Handled = true;
        RequestDrag();
    }

    internal void RequestDrag()
    {
        if (IsPanelVisible && !IsClosed) DragRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnGripReleased(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _gripPress = null;
        if (DragGrip.IsMouseCaptured) DragGrip.ReleaseMouseCapture();
    }

    internal FloatingLyricsControlWindow(Action<MediaHotkeyAction> execute, Action<TimeSpan> seek,
        TimeProvider? timeProvider = null)
    {
        InitializeComponent();
        var fontFolder = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar);
        FontFamily = new System.Windows.Media.FontFamily(fontFolder, $"./#{AppSettings.BundledFontFamily}");
        _execute = execute;
        _seek = seek;
        _timeProvider = timeProvider ?? TimeProvider.System;
        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        MouseMove += OnMouseMove;
        // Slider handles track clicks itself when IsMoveToPointEnabled is set.
        // Observe handled preview events as well so timeline updates cannot undo the click.
        SeekSlider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnSeekStarted), true);
        SeekSlider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnSeekFinished), true);
        VisualRoot.Opacity = 0;
        // Keep the visible control viewport hittable even between buttons, independent of the material HWND.
        Surface.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Surface.BorderThickness = new Thickness(0);
    }

    internal void Update(TrackInfo? track, bool playing, TimeSpan position, bool canSeek,
        PlayerSourceBadge source, bool light)
    {
        if (IsClosed) return;
        var identity = track is null ? string.Empty : LyricSyncService.BuildStableTrackIdentity(track);
        if (_trackIdentity != identity)
        {
            _trackIdentity = identity;
            _seeking = false;
            _pendingSeek = null;
        }
        _duration = track?.Duration ?? TimeSpan.Zero;
        TitleText.Text = track?.Title ?? "暂无播放";
        ArtistText.Text = track is null ? "当前没有播放内容" : $"{track.Artist} · {source.DisplayName}";
        PreviousButton.IsEnabled = PlayButton.IsEnabled = NextButton.IsEnabled = track is not null;
        SeekSlider.IsEnabled = track is not null && canSeek && _duration > TimeSpan.Zero;
        if (!SeekSlider.IsEnabled)
        {
            _seeking = false;
            _pendingSeek = null;
        }
        if (playing != _playing)
        {
            _playing = playing;
            PlayGlyph.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
            PauseGlyph.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(PlayButton, playing ? "暂停" : "播放");
        }
        SetPalette(light);
        if (!_seeking && CanApplyTimeline(position)) SeekSlider.Value = _duration > TimeSpan.Zero
            ? Math.Clamp(position.TotalMilliseconds / _duration.TotalMilliseconds * 1000, 0, 1000) : 0;
        PaintTime();
    }

    private bool? _light;
    private bool _highContrast;
    private void SetPalette(bool light)
    {
        if (_light == light && _highContrast == SystemParameters.HighContrast) return;
        _light = light;
        _highContrast = SystemParameters.HighContrast;
        Resources["PanelForeground"] = _highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(light ? Color.FromRgb(28, 29, 32) : Color.FromRgb(246, 247, 248));
        Resources["PanelSubtle"] = _highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(light ? Color.FromRgb(98, 103, 113) : Color.FromRgb(169, 173, 181));
        Resources["PanelLine"] = _highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(light ? Color.FromRgb(186, 188, 192) : Color.FromRgb(98, 101, 106));
        Resources["PanelButton"] = _highContrast ? SystemColors.HighlightBrush : new SolidColorBrush(light ? Color.FromRgb(41, 45, 52) : Color.FromRgb(238, 241, 245));
        Resources["PanelButtonText"] = _highContrast ? SystemColors.HighlightTextBrush : new SolidColorBrush(light ? Colors.White : Color.FromRgb(32, 33, 36));
    }

    internal void Prepare(Window owner, FloatingLyricsPanelPlacement placement, double scale, bool playbackControls = true)
    {
        if (IsClosed) return;
        Owner = owner;
        Topmost = owner.Topmost;
        KeepOpenForKeyboard = false;
        var narrow = playbackControls && placement.Bounds.Width / scale < 220;
        Actions.Visibility = SeekSlider.Visibility = ElapsedText.Visibility = DurationText.Visibility =
            playbackControls ? Visibility.Visible : Visibility.Collapsed;
        SeekRow.Height = new GridLength(playbackControls ? 16 : 0);
        TimeRow.Height = new GridLength(playbackControls ? 14 : 0);
        System.Windows.Controls.Grid.SetRow(Actions, narrow ? 1 : 0);
        System.Windows.Controls.Grid.SetColumn(Actions, narrow ? 0 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(Actions, narrow ? 2 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(Metadata, narrow || !playbackControls ? 2 : 1);
        TransportRow.Height = new GridLength(narrow ? 34 : 0);
        PreviousButton.Width = NextButton.Width = narrow ? 20 : 32;
        PlayButton.Width = PlayButton.Height = narrow ? 28 : 36;
        PlayButton.Margin = new Thickness(narrow ? 4 : 6, 0, narrow ? 4 : 6, 0);
        Actions.HorizontalAlignment = narrow ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Stretch;
        // WPF synchronizes its bounds from this single native move/resize.
        // Setting Width/Height/Left/Top first would expose intermediate rectangles.
        TaskbarNativeMethods.SetWindowPos(new WindowInteropHelper(this).EnsureHandle(), IntPtr.Zero,
            (int)Math.Round(placement.Bounds.Left), (int)Math.Round(placement.Bounds.Top),
            (int)Math.Round(placement.Bounds.Width), (int)Math.Round(placement.Bounds.Height),
            TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
        if (!IsVisible) Show();
    }

    private int _visibilityGeneration;

    internal void SetPanelVisible(bool visible, bool immediate = false, Action? completed = null)
    {
        if (IsClosed) return;
        IsPanelVisible = visible;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) NativeWindowInteraction.SetClickThrough(handle, enabled: !visible);
        VisualRoot.IsHitTestVisible = visible;
        foreach (var control in new System.Windows.Controls.Control[] { PlayButton, PreviousButton, NextButton, SeekSlider }) control.IsTabStop = visible;
        if (!visible)
        {
            _gripPress = null;
            if (DragGrip.IsMouseCaptured) DragGrip.ReleaseMouseCapture();
            ShowKeyboardFocus = false;
            _seeking = false; KeepOpenForKeyboard = false;
            _pendingSeek = null;
            if (IsKeyboardFocusWithin) Keyboard.ClearFocus();
            if (SeekSlider.IsMouseCaptureWithin) System.Windows.Input.Mouse.Capture(null);
        }
        var generation = ++_visibilityGeneration;
        FloatingPanelAnimation.Fade(VisualRoot, visible, immediate, () =>
        {
            if (!IsClosed && generation == _visibilityGeneration) completed?.Invoke();
        });
    }

    internal void FocusControls()
    {
        if (!IsPanelVisible || IsClosed) return;
        Activate();
        ShowKeyboardFocus = true;
        KeepOpenForKeyboard = true;
        PlayButton.Focus();
    }

    internal void HidePanel()
    {
        if (IsClosed) return;
        SetPanelVisible(false, immediate: true);
        Hide();
    }

    private void OnPrevious(object sender, RoutedEventArgs e) { if (IsPanelVisible && PreviousButton.IsEnabled) _execute(MediaHotkeyAction.PreviousTrack); }
    private void OnPlay(object sender, RoutedEventArgs e) { if (IsPanelVisible && PlayButton.IsEnabled) _execute(MediaHotkeyAction.TogglePlayPause); }
    private void OnNext(object sender, RoutedEventArgs e) { if (IsPanelVisible && NextButton.IsEnabled) _execute(MediaHotkeyAction.NextTrack); }
    private void OnSeekStarted(object sender, MouseButtonEventArgs e)
    {
        if (!IsPanelVisible || !SeekSlider.IsEnabled) return;
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
        if (IsPanelVisible && _seeking && SeekSlider.IsEnabled && _seekIdentity == _trackIdentity)
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
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!IsPanelVisible) return;
        ShowKeyboardFocus = true;
        KeepOpenForKeyboard = true;
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e) => KeepOpenForKeyboard = false;
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) => ShowKeyboardFocus = false;
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        TaskbarPlacementService.ApplyToolWindowStyle(handle);
        NativeWindowInteraction.SetClickThrough(handle, enabled: !IsPanelVisible);
    }
    protected override void OnClosed(EventArgs e)
    {
        IsClosed = true;
        SourceInitialized -= OnSourceInitialized;
        PreviewKeyDown -= OnPreviewKeyDown;
        PreviewMouseDown -= OnPreviewMouseDown;
        MouseMove -= OnMouseMove;
        SeekSlider.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnSeekStarted));
        SeekSlider.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnSeekFinished));
        base.OnClosed(e);
    }
}
