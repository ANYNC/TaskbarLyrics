using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Media = System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
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
    private string? _lastCoverTrackId;
    private string? _currentCoverDataUri;
    private string _currentCoverFallbackText = "N";
    private string _currentCoverFallbackColorCss = "rgba(67, 160, 71, 1)";
    private bool _enableSmtcTimelineMonitor;
    private SmtcTimelineMonitorWindow? _smtcTimelineMonitorWindow;
    private bool _isWebViewReady;
    private bool _isWebViewInitializing;
    private bool _isWebDocumentReady;
    private bool _isShowingWebErrorPage;
    private bool _isTimerTickRunning;
    private int _lastWebCurrentLineIndex = -1;
    private string _lastWebTrackId = string.Empty;
    private FrameworkElement? _lyricsWebViewElement;
    private object? _lyricsWebViewControl;
    private EventInfo? _lyricsNavigationCompletedEvent;
    private Delegate? _lyricsNavigationCompletedHandler;
    private bool _usingCompositionWebView;

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
            Interval = TimeSpan.FromMilliseconds(60)
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

        // Web renderer is now fully transparent by design.
        RootBorder.Background = Media.Brushes.Transparent;
        RootBorder.BorderBrush = Media.Brushes.Transparent;
        RootBorder.BorderThickness = new Thickness(0);

        _lyricSyncService = BuildLyricSyncService();
        AnchorToTaskbar();
        AttachToTaskbarHost();
        ResetLineTransforms();
        PushStyleToWebView(settings);
        PushLyricsToWebView(_currentLine, _nextLine, 0, _lastWebCurrentLineIndex, _lastWebTrackId);
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AnchorToTaskbar();
        AttachToTaskbarHost();
        ResetLineTransforms();
        await EnsureLyricsWebViewReadyAsync();
        PushLyricsToWebView(_currentLine, _nextLine, 0, _lastWebCurrentLineIndex, _lastWebTrackId);
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

        DetachWebViewNavigationHandler();

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
        if (_isTimerTickRunning)
        {
            return;
        }

        _isTimerTickRunning = true;
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
            _lastWebCurrentLineIndex = frame.CurrentLineIndex;
            _lastWebTrackId = snapshot.Track?.Id ?? string.Empty;

            UpdateLyricLines(current, next, frame.LineProgress);
            PushLyricsToWebView(current, next, frame.LineProgress, frame.CurrentLineIndex, _lastWebTrackId);
            UpdateCover(snapshot);
        }
        catch (Exception ex)
        {
            _currentLine = $"Lyric service error: {ex.Message}";
            _nextLine = string.Empty;
            DisplayCurrentLine = _currentLine;
            DisplayNextLine = _nextLine;
            PushLyricsToWebView(_currentLine, _nextLine, 0, _lastWebCurrentLineIndex, _lastWebTrackId);
            Debug.WriteLine(ex);
        }
        finally
        {
            _isTimerTickRunning = false;
        }
    }

    private void UpdateLyricLines(string current, string next, double lineProgress)
    {
        _currentLine = current;
        _nextLine = next;
        DisplayCurrentLine = current;
        DisplayNextLine = next;
        CurrentLineTextBlock.Text = current;
        NextLineTextBlock.Text = string.IsNullOrWhiteSpace(next) ? " " : next;
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
        if (string.Equals(trackId, _lastCoverTrackId, StringComparison.Ordinal))
        {
            return;
        }

        _lastCoverTrackId = trackId;

        var sourceApp = snapshot.Track?.SourceApp ?? string.Empty;
        _currentCoverFallbackText = sourceApp.StartsWith("Q", StringComparison.OrdinalIgnoreCase) ? "Q" : "N";
        var fallbackColor = sourceApp.StartsWith("Q", StringComparison.OrdinalIgnoreCase)
            ? Media.Color.FromRgb(41, 182, 246)
            : Media.Color.FromRgb(67, 160, 71);
        _currentCoverFallbackColorCss = ToCssColor(fallbackColor);

        if (snapshot.CoverImageBytes is { Length: > 0 } bytes)
        {
            _currentCoverDataUri = BuildCoverDataUri(bytes);
            PushCoverToWebView();
            return;
        }

        _currentCoverDataUri = null;
        PushCoverToWebView();
    }

    private async Task EnsureLyricsWebViewReadyAsync()
    {
        if (_isWebViewReady || _isWebViewInitializing)
        {
            return;
        }

        _isWebViewInitializing = true;
        try
        {
            var webViewControl = EnsureWebViewControlCreated();
            var webViewUserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarLyrics",
                "WebView2");
            Directory.CreateDirectory(webViewUserDataFolder);
            var environmentOptions = _usingCompositionWebView
                ? new CoreWebView2EnvironmentOptions("--disable-gpu --disable-gpu-compositing")
                : new CoreWebView2EnvironmentOptions("--disable-gpu");
            var webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: webViewUserDataFolder,
                options: environmentOptions);

            await EnsureCoreWebView2Async(webViewControl, webViewEnvironment);
            TrySetDefaultBackgroundColor(webViewControl, System.Drawing.Color.Transparent);
            var coreWebView2 = TryGetCoreWebView2(webViewControl);
            if (coreWebView2 is not null)
            {
                coreWebView2.Settings.IsStatusBarEnabled = false;
                coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView2.Settings.AreDevToolsEnabled = false;
                coreWebView2.Settings.IsZoomControlEnabled = false;
                coreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
            }

            AttachWebViewNavigationHandler(webViewControl);
            NavigateWebViewToString(GetLyricsWebUiHtml());
            _isWebViewReady = true;
            _isShowingWebErrorPage = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _isWebViewReady = false;
            _isWebDocumentReady = false;
            _isShowingWebErrorPage = false;
        }
        finally
        {
            _isWebViewInitializing = false;
        }
    }

    private void PushLyricsToWebView(string current, string next, double lineProgress, int currentLineIndex, string? trackId)
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var currentJson = JsonSerializer.Serialize(current);
        var nextJson = JsonSerializer.Serialize(next);
        var progressJson = JsonSerializer.Serialize(Math.Clamp(lineProgress, 0, 1));
        var lineIndexJson = JsonSerializer.Serialize(currentLineIndex);
        var trackIdJson = JsonSerializer.Serialize(trackId ?? string.Empty);
        var script = $"window.taskbarLyrics?.setLyrics({currentJson}, {nextJson}, {progressJson}, {lineIndexJson}, {trackIdJson});";
        _ = ExecuteWebScriptAsync(script);
    }

    private void PushCoverToWebView()
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var dataUriJson = JsonSerializer.Serialize(_currentCoverDataUri ?? string.Empty);
        var fallbackTextJson = JsonSerializer.Serialize(_currentCoverFallbackText);
        var fallbackColorJson = JsonSerializer.Serialize(_currentCoverFallbackColorCss);
        var script = $"window.taskbarLyrics?.setCover({dataUriJson}, {fallbackTextJson}, {fallbackColorJson});";
        _ = ExecuteWebScriptAsync(script);
    }

    private static string BuildCoverDataUri(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var mime = DetectImageMimeType(bytes);
        var base64 = Convert.ToBase64String(bytes);
        return $"data:{mime};base64,{base64}";
    }

    private static string DetectImageMimeType(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
            bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
        {
            return "image/gif";
        }

        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "image/webp";
        }

        if (bytes.Length >= 12 &&
            bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
        {
            return "image/avif";
        }

        return "image/jpeg";
    }

    private void OnLyricsWebViewNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _isWebDocumentReady = false;
            if (!_isShowingWebErrorPage)
            {
                _isShowingWebErrorPage = true;
                NavigateWebViewToString(GetLyricsWebErrorHtml($"WebView navigation failed: {e.WebErrorStatus}"));
            }
            return;
        }
        _isWebDocumentReady = true;
        if (_isShowingWebErrorPage)
        {
            return;
        }

        if (System.Windows.Application.Current is App app)
        {
            PushStyleToWebView(app.Settings);
        }

        PushLyricsToWebView(_currentLine, _nextLine, 0, _lastWebCurrentLineIndex, _lastWebTrackId);
        PushCoverToWebView();
    }

    private void PushStyleToWebView(AppSettings settings)
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var stylePayload = new
        {
            fontFamily = string.IsNullOrWhiteSpace(settings.FontFamily)
                ? "SF Pro Display, SF Pro Text, Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Microsoft YaHei"
                : settings.FontFamily,
            fontSize = Math.Clamp(settings.FontSize, 10, 40),
            fontWeight = settings.FontWeight,
            primaryColor = ToCssColor(_primaryTextColor),
            secondaryColor = ToCssColor(_secondaryTextColor)
        };

        var payloadJson = JsonSerializer.Serialize(stylePayload);
        var script = $"window.taskbarLyrics?.applyStyle({payloadJson});";
        _ = ExecuteWebScriptAsync(script);
    }

    private object EnsureWebViewControlCreated()
    {
        if (_lyricsWebViewControl is not null && _lyricsWebViewElement is not null)
        {
            return _lyricsWebViewControl;
        }

        object control = new WebView2();
        _usingCompositionWebView = false;

        if (control is not FrameworkElement element || control is not UIElement uiElement)
        {
            throw new InvalidOperationException("WebView control is not a WPF element.");
        }

        element.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        element.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        element.Focusable = false;
        element.IsHitTestVisible = false;

        LyricsWebHost.Children.Clear();
        LyricsWebHost.Children.Add(uiElement);

        _lyricsWebViewControl = control;
        _lyricsWebViewElement = element;
        return control;
    }

    private static async Task EnsureCoreWebView2Async(object webViewControl, CoreWebView2Environment environment)
    {
        var ensureMethod = webViewControl.GetType().GetMethod(
            "EnsureCoreWebView2Async",
            new[] { typeof(CoreWebView2Environment) });
        if (ensureMethod is null)
        {
            throw new MissingMethodException(
                webViewControl.GetType().FullName,
                "EnsureCoreWebView2Async");
        }

        var ensureTask = ensureMethod.Invoke(webViewControl, new object?[] { environment }) as Task;
        if (ensureTask is null)
        {
            throw new InvalidOperationException("EnsureCoreWebView2Async did not return Task.");
        }

        await ensureTask.ConfigureAwait(true);
    }

    private static void TrySetDefaultBackgroundColor(object webViewControl, System.Drawing.Color color)
    {
        var property = webViewControl.GetType().GetProperty("DefaultBackgroundColor");
        if (property is null || !property.CanWrite || property.PropertyType != typeof(System.Drawing.Color))
        {
            return;
        }

        property.SetValue(webViewControl, color);
    }

    private static CoreWebView2? TryGetCoreWebView2(object webViewControl)
    {
        var property = webViewControl.GetType().GetProperty("CoreWebView2");
        return property?.GetValue(webViewControl) as CoreWebView2;
    }

    private void AttachWebViewNavigationHandler(object webViewControl)
    {
        DetachWebViewNavigationHandler();
        var eventInfo = webViewControl.GetType().GetEvent("NavigationCompleted");
        if (eventInfo?.EventHandlerType is null)
        {
            return;
        }

        var handlerMethod = GetType().GetMethod(
            nameof(OnLyricsWebViewNavigationCompleted),
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (handlerMethod is null)
        {
            return;
        }

        var handler = Delegate.CreateDelegate(
            eventInfo.EventHandlerType,
            this,
            handlerMethod,
            throwOnBindFailure: false);
        if (handler is null)
        {
            return;
        }

        eventInfo.AddEventHandler(webViewControl, handler);
        _lyricsNavigationCompletedEvent = eventInfo;
        _lyricsNavigationCompletedHandler = handler;
    }

    private void DetachWebViewNavigationHandler()
    {
        if (_lyricsWebViewControl is null ||
            _lyricsNavigationCompletedEvent is null ||
            _lyricsNavigationCompletedHandler is null)
        {
            return;
        }

        _lyricsNavigationCompletedEvent.RemoveEventHandler(_lyricsWebViewControl, _lyricsNavigationCompletedHandler);
        _lyricsNavigationCompletedEvent = null;
        _lyricsNavigationCompletedHandler = null;
    }

    private void NavigateWebViewToString(string html)
    {
        if (_lyricsWebViewControl is null)
        {
            return;
        }

        var method = _lyricsWebViewControl.GetType().GetMethod("NavigateToString", new[] { typeof(string) });
        method?.Invoke(_lyricsWebViewControl, new object?[] { html });
    }

    private Task? ExecuteWebScriptAsync(string script)
    {
        if (_lyricsWebViewControl is null)
        {
            return null;
        }

        var method = _lyricsWebViewControl.GetType().GetMethod("ExecuteScriptAsync", new[] { typeof(string) });
        return method?.Invoke(_lyricsWebViewControl, new object?[] { script }) as Task;
    }

    private static string ToCssColor(Media.Color color)
    {
        var alpha = Math.Round(color.A / 255.0, 4, MidpointRounding.AwayFromZero);
        return $"rgba({color.R}, {color.G}, {color.B}, {alpha.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string LoadLyricsWebAssetText(string fileName)
    {
        var assetPath = Path.Combine(AppContext.BaseDirectory, "Web", "Lyrics", fileName);
        if (!File.Exists(assetPath))
        {
            throw new FileNotFoundException($"Lyrics web asset not found: {assetPath}", assetPath);
        }

        return File.ReadAllText(assetPath);
    }

    private static string GetLyricsWebUiHtml()
    {
        var htmlTemplate = LoadLyricsWebAssetText("index.html");
        var styleCss = LoadLyricsWebAssetText("style.css");
        var scriptJs = LoadLyricsWebAssetText("app.js");
        return htmlTemplate
            .Replace("{{STYLE_CSS}}", styleCss, StringComparison.Ordinal)
            .Replace("{{APP_JS}}", scriptJs, StringComparison.Ordinal);
    }

    private static string GetLyricsWebErrorHtml(string message)
    {
        var safeMessage = System.Net.WebUtility.HtmlEncode(message);
        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <style>
    html, body {
      width: 100%;
      height: 100%;
      margin: 0;
      padding: 0;
      background: #121212;
      color: #f8f8f8;
      font-family: "Segoe UI", "Microsoft YaHei UI", sans-serif;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .error {
      padding: 8px 10px;
      font-size: 12px;
      line-height: 1.25;
      border-radius: 6px;
      border: 1px solid rgba(255, 255, 255, 0.2);
      background: rgba(255, 255, 255, 0.06);
      max-width: 100%;
      white-space: pre-wrap;
      word-break: break-word;
    }
  </style>
</head>
<body>
  <div class="error">{{safeMessage}}</div>
</body>
</html>
""";
    }

    private void AnchorToTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        const double normalTaskbarHeight = 48;
        var taskbarHeight = Math.Max(normalTaskbarHeight, screenHeight - workArea.Height);
        var desiredHeight = Math.Max(36, taskbarHeight - 4);
        Height = Math.Min(desiredHeight, taskbarHeight);

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
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
