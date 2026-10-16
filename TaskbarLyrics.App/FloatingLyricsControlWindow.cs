using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace TaskbarLyrics.App;

internal partial class FloatingLyricsControlWindow : Window
{
    private readonly Action<MediaHotkeyAction> _execute;
    private readonly Action<TimeSpan> _seek;
    private string _trackIdentity = string.Empty;
    private string _seekIdentity = string.Empty;
    private TimeSpan _duration;
    private bool _seeking;
    private bool _playing;
    internal bool IsClosed { get; private set; }
    internal bool KeepOpenForKeyboard { get; private set; }
    internal bool IsPanelVisible { get; private set; }
    internal event EventHandler? Dismissed;

    internal FloatingLyricsControlWindow(Action<MediaHotkeyAction> execute, Action<TimeSpan> seek)
    {
        InitializeComponent();
        var fontFolder = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar);
        FontFamily = new System.Windows.Media.FontFamily(fontFolder, $"./#{AppSettings.BundledFontFamily}");
        _execute = execute;
        _seek = seek;
        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
        MouseMove += OnMouseMove;
        VisualRoot.Opacity = 0;
        Surface.Background = System.Windows.Media.Brushes.Transparent;
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
        }
        _duration = track?.Duration ?? TimeSpan.Zero;
        TitleText.Text = track?.Title ?? "暂无播放";
        ArtistText.Text = track is null ? "当前没有播放内容" : $"{track.Artist} · {source.DisplayName}";
        TitleText.ToolTip = TitleText.Text;
        ArtistText.ToolTip = ArtistText.Text;
        PreviousButton.IsEnabled = PlayButton.IsEnabled = NextButton.IsEnabled = track is not null;
        SeekSlider.IsEnabled = track is not null && canSeek && _duration > TimeSpan.Zero;
        if (playing != _playing)
        {
            _playing = playing;
            PlayGlyph.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
            PauseGlyph.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(PlayButton, playing ? "暂停" : "播放");
        }
        SetPalette(light);
        if (!_seeking) SeekSlider.Value = _duration > TimeSpan.Zero
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

    internal void Prepare(Window owner, FloatingLyricsPanelPlacement placement, double scale)
    {
        if (IsClosed) return;
        Owner = owner;
        Topmost = owner.Topmost;
        KeepOpenForKeyboard = false;
        Width = placement.Bounds.Width / scale;
        Height = placement.Bounds.Height / scale;
        var narrow = Width < 220;
        System.Windows.Controls.Grid.SetRow(Actions, narrow ? 1 : 0);
        System.Windows.Controls.Grid.SetColumn(Actions, narrow ? 0 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(Actions, narrow ? 2 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(Metadata, narrow ? 2 : 1);
        TransportRow.Height = new GridLength(narrow ? 34 : 0);
        PreviousButton.Width = NextButton.Width = narrow ? 20 : 32;
        PlayButton.Width = PlayButton.Height = narrow ? 28 : 36;
        PlayButton.Margin = new Thickness(narrow ? 4 : 6, 0, narrow ? 4 : 6, 0);
        Actions.HorizontalAlignment = narrow ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Stretch;
        Left = placement.Bounds.Left / scale;
        Top = placement.Bounds.Top / scale;
        Show();
        TaskbarNativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            (int)Math.Round(placement.Bounds.Left), (int)Math.Round(placement.Bounds.Top),
            (int)Math.Round(placement.Bounds.Width), (int)Math.Round(placement.Bounds.Height),
            TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
    }

    internal void SetPanelVisible(bool visible, bool immediate = false)
    {
        if (IsClosed) return;
        IsPanelVisible = visible;
        VisualRoot.IsHitTestVisible = visible;
        foreach (var control in new System.Windows.Controls.Control[] { PlayButton, PreviousButton, NextButton, SeekSlider }) control.IsTabStop = visible;
        if (!visible)
        {
            _seeking = false; KeepOpenForKeyboard = false;
            if (IsKeyboardFocusWithin) Keyboard.ClearFocus();
            if (SeekSlider.IsMouseCaptureWithin) System.Windows.Input.Mouse.Capture(null);
        }
        var current = VisualRoot.Opacity;
        VisualRoot.BeginAnimation(OpacityProperty, null);
        VisualRoot.Opacity = visible ? 1 : 0;
        if (!immediate && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            VisualRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(current, visible ? 1 : 0, TimeSpan.FromMilliseconds(180)));
    }

    internal void FocusControls()
    {
        if (!IsPanelVisible || IsClosed) return;
        Activate();
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
    private void OnSeekStarted(object sender, MouseButtonEventArgs e) { _seeking = true; _seekIdentity = _trackIdentity; }
    private void OnSeekFinished(object sender, MouseButtonEventArgs e) => CommitSeek();
    private void OnSeekKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        { _seeking = true; _seekIdentity = _trackIdentity; }
    }
    private void OnSeekKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_seeking) CommitSeek();
    }
    private void CommitSeek()
    {
        if (IsPanelVisible && _seeking && SeekSlider.IsEnabled && _seekIdentity == _trackIdentity)
            _seek(TimeSpan.FromMilliseconds(SeekSlider.Value / 1000 * _duration.TotalMilliseconds));
        _seeking = false;
    }
    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => PaintTime();
    private void PaintTime()
    {
        if (ElapsedText is null) return;
        var position = TimeSpan.FromMilliseconds(SeekSlider.Value / 1000 * _duration.TotalMilliseconds);
        ElapsedText.Text = FormatTime(position);
        DurationText.Text = FormatTime(_duration);
        AutomationProperties.SetHelpText(SeekSlider, $"{ElapsedText.Text} / {DurationText.Text}");
    }
    private static string FormatTime(TimeSpan value) => value.ToString(value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!IsPanelVisible) return;
        KeepOpenForKeyboard = true;
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e) => KeepOpenForKeyboard = false;
    private void OnSourceInitialized(object? sender, EventArgs e) => TaskbarPlacementService.ApplyToolWindowStyle(new WindowInteropHelper(this).Handle);
    protected override void OnClosed(EventArgs e)
    {
        IsClosed = true;
        SourceInitialized -= OnSourceInitialized;
        PreviewKeyDown -= OnPreviewKeyDown;
        MouseMove -= OnMouseMove;
        base.OnClosed(e);
    }
}
