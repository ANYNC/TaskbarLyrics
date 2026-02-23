using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Media = System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskbarLyrics.Adapters.Netease;
using TaskbarLyrics.Adapters.QQMusic;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int WmShowWindow = 0x0018;
    private readonly IMusicSessionProvider _musicSessionProvider;
    private readonly DispatcherTimer _timer;
    private readonly uint _taskbarCreatedMessage;
    private readonly Media.SolidColorBrush _currentLineBrush = new(Media.Colors.White);
    private readonly Media.SolidColorBrush _nextLineBrush = new(Media.Color.FromArgb(150, 255, 255, 255));
    private readonly Media.SolidColorBrush _incomingLineBrush = new(Media.Color.FromArgb(150, 255, 255, 255));
    private Media.Color _primaryTextColor = Media.Colors.White;
    private Media.Color _secondaryTextColor = Media.Color.FromArgb(190, 255, 255, 255);
    private const double SecondaryLineBrightness = 0.40;
    private readonly TimeSpan _lineTransitionDuration = TimeSpan.FromMilliseconds(360);
    private readonly Stopwatch _lineTransitionClock = new();
    private double _lineTransitionTravel;
    private double _lineTrackHeight = 18;
    private double _secondaryLineFontSize = 12;
    private bool _suppressPromotedSizeAnimation;
    private bool _isLineTransitionAnimating;
    private LyricSyncService _lyricSyncService;
    private string _currentLine = "TaskbarLyrics started";
    private string _nextLine = "Waiting for lyrics...";
    private string _displayCurrentLine = "TaskbarLyrics started";
    private string _displayNextLine = "Waiting for lyrics...";
    private string? _pendingCurrentLine;
    private string? _pendingNextLine;
    private double _pendingLineProgress;
    private string? _lastCoverTrackId;
    private bool _enableSmtcTimelineMonitor;
    private SmtcTimelineMonitorWindow? _smtcTimelineMonitorWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        CurrentLineTextBlock.Foreground = _currentLineBrush;
        NextLineTextBlock.Foreground = _nextLineBrush;
        IncomingNextLineTextBlock.Foreground = _incomingLineBrush;

        _musicSessionProvider = new SmtcMusicSessionProvider();
        _lyricSyncService = BuildLyricSyncService();
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => AnchorToTaskbar();
        IsVisibleChanged += OnIsVisibleChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (System.Windows.Application.Current is App app)
        {
            ApplySettings(app.Settings);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayCurrentLine
    {
        get => _displayCurrentLine;
        private set
        {
            if (_displayCurrentLine == value)
            {
                return;
            }

            _displayCurrentLine = value;
            OnPropertyChanged();
        }
    }

    public string DisplayNextLine
    {
        get => _displayNextLine;
        private set
        {
            if (_displayNextLine == value)
            {
                return;
            }

            _displayNextLine = value;
            OnPropertyChanged();
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        if (_musicSessionProvider is SmtcMusicSessionProvider smtcProvider)
        {
            smtcProvider.SetRecognitionOrder(settings.SourceRecognitionOrder);
        }

        Width = Math.Clamp(settings.WindowWidth, 320, 1400);
        CurrentLineTextBlock.FontSize = Math.Clamp(settings.FontSize, 10, 40);
        _secondaryLineFontSize = Math.Max(9, Math.Round(CurrentLineTextBlock.FontSize * 0.92, 2));
        NextLineTextBlock.FontSize = _secondaryLineFontSize;
        IncomingNextLineTextBlock.FontSize = _secondaryLineFontSize;
        ApplyStableLineTrackLayout();
        var fontFamilyText = string.IsNullOrWhiteSpace(settings.FontFamily)
            ? "SF Pro Display, SF Pro Text, Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei"
            : settings.FontFamily;
        var lyricFontFamily = ResolveFontFamily(fontFamilyText);
        var lyricFontWeight = ResolveFontWeight(settings.FontWeight);
        CurrentLineTextBlock.FontFamily = lyricFontFamily;
        NextLineTextBlock.FontFamily = lyricFontFamily;
        IncomingNextLineTextBlock.FontFamily = lyricFontFamily;
        CurrentLineTextBlock.FontWeight = lyricFontWeight;
        NextLineTextBlock.FontWeight = lyricFontWeight;
        IncomingNextLineTextBlock.FontWeight = lyricFontWeight;

        try
        {
            var brush = (Media.Brush?)new Media.BrushConverter().ConvertFromString(settings.ForegroundColor);
            if (brush is Media.SolidColorBrush solid)
            {
                _primaryTextColor = solid.Color;
            }
            else
            {
                _primaryTextColor = Media.Colors.White;
            }
        }
        catch
        {
            _primaryTextColor = Media.Colors.White;
        }

        _secondaryTextColor = Media.Color.FromArgb(
            (byte)Math.Clamp((int)(_primaryTextColor.A * 0.76), 0, 255),
            _primaryTextColor.R,
            _primaryTextColor.G,
            _primaryTextColor.B);
        SetLineBrushes(1.0, SecondaryLineBrightness, SecondaryLineBrightness);

        if (settings.ShowBackground)
        {
            RootBorder.Background = new Media.SolidColorBrush(Media.Color.FromArgb(
                (byte)(Math.Clamp(settings.BackgroundOpacity, 0, 1) * 255),
                17,
                17,
                17));
        }
        else
        {
            RootBorder.Background = Media.Brushes.Transparent;
        }

        if (settings.ShowBorder)
        {
            RootBorder.BorderBrush = new Media.SolidColorBrush(Media.Color.FromArgb(130, 255, 255, 255));
            RootBorder.BorderThickness = new Thickness(1);
        }
        else
        {
            RootBorder.BorderBrush = Media.Brushes.Transparent;
            RootBorder.BorderThickness = new Thickness(0);
        }

        _lyricSyncService = BuildLyricSyncService();
        AnchorToTaskbar();
        AttachToTaskbarHost();
        ResetLineTransforms();
        _enableSmtcTimelineMonitor = settings.EnableSmtcTimelineMonitor;
        if (IsLoaded)
        {
            UpdateSmtcTimelineMonitorWindow();
        }
    }

    private LyricSyncService BuildLyricSyncService()
    {
        var providers = new List<ILyricProvider>
        {
            new LrcLibLyricProvider(),
            new GenericSmtcLyricProvider(),
            new NeteaseLyricProvider(),
            new QQMusicLyricProvider()
        };

        return new LyricSyncService(new LyricProviderRegistry(providers));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AnchorToTaskbar();
        AttachToTaskbarHost();
        ResetLineTransforms();
        UpdateSmtcTimelineMonitorWindow();
        _timer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            AnchorToTaskbar();
            AttachToTaskbarHost();
            ResetLineTransforms();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App app && !app.IsExiting)
        {
            e.Cancel = true;
            app.MarkLyricsHiddenByUser();
            Hide();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CloseSmtcTimelineMonitorWindow();
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Loaded -= OnLoaded;
        SourceInitialized -= OnSourceInitialized;
        Closing -= OnClosing;
        Closed -= OnClosed;
        IsVisibleChanged -= OnIsVisibleChanged;

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.RemoveHook(WndProc);
        }

        Media.CompositionTarget.Rendering -= OnCompositionRendering;
    }

    private void UpdateSmtcTimelineMonitorWindow()
    {
        if (_musicSessionProvider is not SmtcMusicSessionProvider smtcProvider || !_enableSmtcTimelineMonitor)
        {
            CloseSmtcTimelineMonitorWindow();
            return;
        }

        if (_smtcTimelineMonitorWindow is { IsVisible: true })
        {
            return;
        }

        var monitorWindow = new SmtcTimelineMonitorWindow(smtcProvider);
        if (IsLoaded && IsVisible)
        {
            monitorWindow.Owner = this;
        }

        monitorWindow.Closed += OnSmtcTimelineMonitorClosed;
        _smtcTimelineMonitorWindow = monitorWindow;
        monitorWindow.Show();
    }

    private void CloseSmtcTimelineMonitorWindow()
    {
        if (_smtcTimelineMonitorWindow is null)
        {
            return;
        }

        _smtcTimelineMonitorWindow.Closed -= OnSmtcTimelineMonitorClosed;
        _smtcTimelineMonitorWindow.Close();
        _smtcTimelineMonitorWindow = null;
    }

    private void OnSmtcTimelineMonitorClosed(object? sender, EventArgs e)
    {
        if (sender is SmtcTimelineMonitorWindow window)
        {
            window.Closed -= OnSmtcTimelineMonitorClosed;
        }

        _smtcTimelineMonitorWindow = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AnchorToTaskbar();
            AttachToTaskbarHost();
        });
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            EnsureVisibleIfExpected();

            var snapshot = await _musicSessionProvider.GetCurrentAsync();
            var frame = await _lyricSyncService.GetDisplayFrameAsync(snapshot);
            if (_musicSessionProvider is SmtcMusicSessionProvider smtcProvider)
            {
                smtcProvider.SetCurrentLyricSource(_lyricSyncService.CurrentLyricSourceApp);
            }

            var current = string.IsNullOrWhiteSpace(frame.CurrentLine)
                ? "Waiting for lyrics..."
                : frame.CurrentLine;

            var next = frame.NextLine;

            UpdateLyricLines(current, next, frame.LineProgress);
            UpdateCover(snapshot);
        }
        catch (Exception ex)
        {
            _currentLine = string.Empty;
            _nextLine = string.Empty;
            DisplayCurrentLine = $"Lyric service error: {ex.Message}";
            DisplayNextLine = string.Empty;
            _isLineTransitionAnimating = false;
            ResetLineTransforms();
            Debug.WriteLine(ex);
        }
    }

    private void UpdateLyricLines(string current, string next, double lineProgress)
    {
        if (string.IsNullOrWhiteSpace(DisplayCurrentLine))
        {
            _currentLine = current;
            _nextLine = next;
            DisplayCurrentLine = current;
            DisplayNextLine = next;
            return;
        }

        if (string.Equals(_currentLine, current, StringComparison.Ordinal))
        {
            _nextLine = next;
            if (!_isLineTransitionAnimating)
            {
                DisplayNextLine = next;
                IncomingNextLineTextBlock.Text = string.Empty;
            }
            else
            {
                _pendingCurrentLine = current;
                _pendingNextLine = next;
                _pendingLineProgress = lineProgress;
                IncomingNextLineTextBlock.Text = next;
            }
            return;
        }

        _pendingCurrentLine = current;
        _pendingNextLine = next;
        _pendingLineProgress = lineProgress;
        StartLinePromotionTransition();
    }

    private void StartLinePromotionTransition()
    {
        if (_isLineTransitionAnimating || _pendingCurrentLine is null)
        {
            return;
        }

        _isLineTransitionAnimating = true;
        if (!string.Equals(DisplayNextLine, _pendingCurrentLine, StringComparison.Ordinal))
        {
            DisplayNextLine = _pendingCurrentLine;
        }
        IncomingNextLineTextBlock.Text = _pendingNextLine ?? string.Empty;
        _suppressPromotedSizeAnimation = ShouldSuppressPromotedSizeAnimation(_pendingCurrentLine);
        NextLineTextBlock.TextTrimming = TextTrimming.None;
        _lineTransitionTravel = GetLineTravelDistance();
        _lineTransitionClock.Restart();

        Media.CompositionTarget.Rendering -= OnCompositionRendering;
        Media.CompositionTarget.Rendering += OnCompositionRendering;
        ApplyTransitionVisuals(0);
    }

    private void CompleteLinePromotionTransition()
    {
        _isLineTransitionAnimating = false;
        Media.CompositionTarget.Rendering -= OnCompositionRendering;
        _lineTransitionClock.Reset();

        if (_pendingCurrentLine is null)
        {
            ResetLineTransforms();
            return;
        }

        _currentLine = _pendingCurrentLine;
        _nextLine = _pendingNextLine ?? string.Empty;
        NextLineTextBlock.FontSize = _secondaryLineFontSize;
        NextLineTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        NextLineHost.Opacity = 1;
        DisplayCurrentLine = _currentLine;
        DisplayNextLine = _nextLine;
        IncomingNextLineTextBlock.Text = string.Empty;
        ResetLineTransforms();

        _pendingCurrentLine = null;
        _pendingNextLine = null;
        _pendingLineProgress = 0;
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (!_isLineTransitionAnimating)
        {
            return;
        }

        var progress = Math.Clamp(
            _lineTransitionClock.Elapsed.TotalMilliseconds / _lineTransitionDuration.TotalMilliseconds,
            0,
            1);

        ApplyTransitionVisuals(progress);

        if (progress >= 1)
        {
            CompleteLinePromotionTransition();
        }
    }

    private void ApplyTransitionVisuals(double progress)
    {
        var travelProgress = GetAppleMusicTravelProgress(progress);
        var currentFade = EaseOutCubic(Clamp01((progress - 0.02) / 0.68));
        var promotedProgress = EaseOutCubic(Clamp01((progress - 0.06) / 0.72));
        var incomingProgress = EaseOutCubic(Clamp01(progress / 0.86));
        var promotedSizeProgress = GetPromotionSizeProgress(progress);
        var travel = _lineTransitionTravel;

        var translatedY = AlignToPhysicalPixel(-travel * travelProgress);
        CurrentLineTranslateTransform.Y = translatedY;
        CurrentLineHost.Opacity = 1 - currentFade;
        CurrentLineScaleTransform.ScaleX = 1;
        CurrentLineScaleTransform.ScaleY = 1;

        var promotedFontSize = _suppressPromotedSizeAnimation
            ? _secondaryLineFontSize
            : Lerp(_secondaryLineFontSize, CurrentLineTextBlock.FontSize, promotedSizeProgress);
        NextLineTranslateTransform.Y = translatedY;
        NextLineHost.Opacity = 1;
        NextLineTextBlock.FontSize = promotedFontSize;
        NextLineScaleTransform.ScaleX = 1;
        NextLineScaleTransform.ScaleY = 1;

        IncomingNextLineTranslateTransform.Y = translatedY;
        IncomingNextLineHost.Opacity = string.IsNullOrWhiteSpace(IncomingNextLineTextBlock.Text)
            ? 0
            : incomingProgress;
        IncomingNextLineScaleTransform.ScaleX = 1;
        IncomingNextLineScaleTransform.ScaleY = 1;

        var nextBrightness = Lerp(SecondaryLineBrightness, 1.0, promotedProgress);
        SetLineBrushes(1.0, nextBrightness, SecondaryLineBrightness);
    }

    private double AlignToPhysicalPixel(double dipValue)
    {
        var dpiScaleY = Media.VisualTreeHelper.GetDpi(this).DpiScaleY;
        if (dpiScaleY <= 0)
        {
            return dipValue;
        }

        return Math.Round(dipValue * dpiScaleY, MidpointRounding.AwayFromZero) / dpiScaleY;
    }

    private static double GetAppleMusicTravelProgress(double t)
    {
        return EaseOutCubic(Clamp01(t));
    }

    private static double EaseOutCubic(double t)
    {
        var x = 1 - t;
        return 1 - (x * x * x);
    }

    private static double EaseOutSine(double t)
    {
        return Math.Sin((t * Math.PI) / 2.0);
    }

    private static double GetPromotionSizeProgress(double t)
    {
        // Complete font-size promotion earlier, then hold steady to prevent tail-end jitter.
        return EaseOutCubic(Clamp01((t - 0.05) / 0.77));
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    private static double Clamp01(double value)
    {
        return Math.Clamp(value, 0, 1);
    }

    private bool ShouldSuppressPromotedSizeAnimation(string promotedLine)
    {
        if (string.IsNullOrWhiteSpace(promotedLine))
        {
            return false;
        }

        var availableWidth = Math.Max(0, NextLineHost.ActualWidth);
        if (availableWidth <= 1)
        {
            return false;
        }

        var dpi = Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Media.Typeface(
            NextLineTextBlock.FontFamily,
            NextLineTextBlock.FontStyle,
            NextLineTextBlock.FontWeight,
            NextLineTextBlock.FontStretch);
        var formatted = new Media.FormattedText(
            promotedLine,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            CurrentLineTextBlock.FontSize,
            Media.Brushes.White,
            dpi);

        // If line still overflows at primary font size, avoid per-frame font-size changes.
        return formatted.WidthIncludingTrailingWhitespace >= (availableWidth - 1);
    }

    private void SetLineBrushes(double currentFactor, double nextFactor, double incomingFactor)
    {
        _currentLineBrush.Color = ScaleColorAlpha(_primaryTextColor, currentFactor);
        _nextLineBrush.Color = ScaleColorAlpha(_primaryTextColor, nextFactor);
        _incomingLineBrush.Color = ScaleColorAlpha(_primaryTextColor, incomingFactor);
    }

    private static Media.Color ScaleColorAlpha(Media.Color color, double factor)
    {
        var clamped = Clamp01(factor);
        return Media.Color.FromArgb(
            (byte)Math.Clamp((int)(color.A * clamped), 0, 255),
            color.R,
            color.G,
            color.B);
    }

    private static FontWeight ResolveFontWeight(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "light" => FontWeights.Light,
            "medium" => FontWeights.Medium,
            "semibold" => FontWeights.SemiBold,
            "bold" => FontWeights.Bold,
            _ => FontWeights.Normal
        };
    }

    private void ApplyStableLineTrackLayout()
    {
        // Keep both lyric rows on a fixed-height track to avoid content-driven remeasure jumps.
        var primaryLineHeight = Math.Ceiling(CurrentLineTextBlock.FontSize * 1.24);
        var secondaryLineHeight = Math.Ceiling(_secondaryLineFontSize * 1.24);
        var trackHeight = Math.Max(primaryLineHeight, secondaryLineHeight) + 1;
        var dpiScaleY = Media.VisualTreeHelper.GetDpi(this).DpiScaleY;
        if (dpiScaleY > 0)
        {
            // Align row height to physical pixels to reduce sub-pixel baseline drift on handoff.
            trackHeight = Math.Round(trackHeight * dpiScaleY, MidpointRounding.AwayFromZero) / dpiScaleY;
        }
        _lineTrackHeight = trackHeight;

        CurrentLineTrackRow.Height = new GridLength(trackHeight);
        NextLineTrackRow.Height = new GridLength(trackHeight);
        IncomingLineTrackRow.Height = new GridLength(trackHeight);

        CurrentLineHost.Height = trackHeight;
        NextLineHost.Height = trackHeight;
        IncomingNextLineHost.Height = trackHeight;

        // Force identical line box metrics for all three layers so incoming -> real second line
        // handoff does not shift by glyph-dependent ascent/descent differences.
        ApplyFixedLineBox(CurrentLineTextBlock, trackHeight);
        ApplyFixedLineBox(NextLineTextBlock, trackHeight);
        ApplyFixedLineBox(IncomingNextLineTextBlock, trackHeight);
    }

    private static void ApplyFixedLineBox(System.Windows.Controls.TextBlock textBlock, double lineHeight)
    {
        textBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        textBlock.LineHeight = lineHeight;
    }

    private static Media.FontFamily ResolveFontFamily(string fontFamilyText)
    {
        var candidates = fontFamilyText
            .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (candidates.Length == 0)
        {
            return new Media.FontFamily("Microsoft YaHei UI");
        }

        var installed = Media.Fonts.SystemFontFamilies
            .SelectMany(f => f.FamilyNames.Values.Append(f.Source))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (installed.Contains(candidate))
            {
                return new Media.FontFamily(candidate);
            }
        }

        return new Media.FontFamily("Microsoft YaHei UI");
    }

    private double GetLineTravelDistance()
    {
        if (_lineTrackHeight > 0.5)
        {
            return _lineTrackHeight;
        }

        var fallback = Math.Round(CurrentLineTextBlock.FontSize * 1.24, MidpointRounding.AwayFromZero) + 1;
        return Math.Max(12, fallback);
    }

    private void ResetLineTransforms()
    {
        _isLineTransitionAnimating = false;
        _suppressPromotedSizeAnimation = false;
        Media.CompositionTarget.Rendering -= OnCompositionRendering;
        _lineTransitionClock.Reset();

        CurrentLineTranslateTransform.BeginAnimation(Media.TranslateTransform.YProperty, null);
        NextLineTranslateTransform.BeginAnimation(Media.TranslateTransform.YProperty, null);
        IncomingNextLineTranslateTransform.BeginAnimation(Media.TranslateTransform.YProperty, null);
        CurrentLineScaleTransform.BeginAnimation(Media.ScaleTransform.ScaleXProperty, null);
        CurrentLineScaleTransform.BeginAnimation(Media.ScaleTransform.ScaleYProperty, null);
        NextLineScaleTransform.BeginAnimation(Media.ScaleTransform.ScaleXProperty, null);
        NextLineScaleTransform.BeginAnimation(Media.ScaleTransform.ScaleYProperty, null);
        IncomingNextLineScaleTransform.BeginAnimation(Media.ScaleTransform.ScaleXProperty, null);
        IncomingNextLineScaleTransform.BeginAnimation(Media.ScaleTransform.ScaleYProperty, null);
        CurrentLineHost.BeginAnimation(OpacityProperty, null);
        NextLineHost.BeginAnimation(OpacityProperty, null);
        IncomingNextLineHost.BeginAnimation(OpacityProperty, null);

        CurrentLineTranslateTransform.Y = 0;
        NextLineTranslateTransform.Y = 0;
        IncomingNextLineTranslateTransform.Y = 0;
        CurrentLineScaleTransform.ScaleX = 1;
        CurrentLineScaleTransform.ScaleY = 1;
        NextLineScaleTransform.ScaleX = 1;
        NextLineScaleTransform.ScaleY = 1;
        IncomingNextLineScaleTransform.ScaleX = 1;
        IncomingNextLineScaleTransform.ScaleY = 1;
        NextLineTextBlock.FontSize = _secondaryLineFontSize;
        IncomingNextLineTextBlock.FontSize = _secondaryLineFontSize;
        NextLineTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        IncomingNextLineTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        CurrentLineHost.Opacity = 1;
        NextLineHost.Opacity = 1;
        IncomingNextLineHost.Opacity = 0;
        IncomingNextLineTextBlock.Text = string.Empty;
        SetLineBrushes(1.0, SecondaryLineBrightness, SecondaryLineBrightness);
    }

    private void UpdateCover(PlaybackSnapshot snapshot)
    {
        var trackId = snapshot.Track?.Id;
        if (string.Equals(trackId, _lastCoverTrackId, StringComparison.Ordinal) && CoverImage.Source is not null)
        {
            ShowCoverImage();
            return;
        }

        _lastCoverTrackId = trackId;

        var sourceApp = snapshot.Track?.SourceApp ?? string.Empty;
        var fallbackText = sourceApp.StartsWith("Q", StringComparison.OrdinalIgnoreCase) ? "Q" : "N";
        var fallbackColor = sourceApp.StartsWith("Q", StringComparison.OrdinalIgnoreCase)
            ? Media.Color.FromRgb(41, 182, 246)
            : Media.Color.FromRgb(67, 160, 71);

        CoverFallbackText.Text = fallbackText;
        CoverFallbackBorder.Background = new Media.SolidColorBrush(fallbackColor);

        if (snapshot.CoverImageBytes is { Length: > 0 } bytes)
        {
            var image = LoadCoverBitmap(bytes);
            if (image is not null)
            {
                CoverImage.Source = image;
                ShowCoverImage();
                return;
            }
        }

        CoverImage.Source = null;
        ShowCoverFallback();
    }

    private void ShowCoverImage()
    {
        CoverImageHost.Visibility = Visibility.Visible;
        CoverFallbackBorder.Visibility = Visibility.Collapsed;
        CoverFallbackText.Visibility = Visibility.Collapsed;
    }

    private void ShowCoverFallback()
    {
        CoverImageHost.Visibility = Visibility.Collapsed;
        CoverFallbackBorder.Visibility = Visibility.Visible;
        CoverFallbackText.Visibility = Visibility.Visible;
    }

    private static BitmapSource? LoadCoverBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void AnchorToTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var taskbarHeight = Math.Max(32, screenHeight - workArea.Height);

        Height = Math.Max(36, taskbarHeight - 4);

        var settings = (System.Windows.Application.Current as App)?.Settings ?? new AppSettings();
        Left = settings.HorizontalAnchor switch
        {
            LyricsHorizontalAnchor.Left => Math.Max(0, settings.XOffset),
            LyricsHorizontalAnchor.Center => ((screenWidth - Width) / 2.0) + settings.XOffset,
            _ => Math.Max(0, screenWidth - Width - 230 + settings.XOffset)
        };

        Top = screenHeight - taskbarHeight + ((taskbarHeight - Height) / 2.0) + settings.YOffset;
    }

    private void AttachToTaskbarHost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var shellTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (shellTray == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_HWNDPARENT, shellTray);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_ASYNCWINDOWPOS |
            NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreatedMessage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AnchorToTaskbar();
                AttachToTaskbarHost();
            }));
        }
        else if (msg == WmShowWindow)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsureVisibleIfExpected();
                AnchorToTaskbar();
                AttachToTaskbarHost();
            }));
        }

        return IntPtr.Zero;
    }

    private void EnsureVisibleIfExpected()
    {
        if (System.Windows.Application.Current is not App app)
        {
            return;
        }

        if (app.IsExiting || !app.UserWantsLyricsVisible)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        AnchorToTaskbar();
        AttachToTaskbarHost();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class NativeMethods
{
    internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal const int GWL_HWNDPARENT = -8;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_ASYNCWINDOWPOS = 0x4000;
    internal const uint SWP_NOSENDCHANGING = 0x0400;
    internal const uint SWP_NOOWNERZORDER = 0x0200;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const int SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindowEx(
        IntPtr hWndParent,
        IntPtr hWndChildAfter,
        string? lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
