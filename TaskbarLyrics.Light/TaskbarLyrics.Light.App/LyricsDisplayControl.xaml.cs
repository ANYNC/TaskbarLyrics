using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using TaskbarLyrics.Light.App.Ui;
using Media = System.Windows.Media;

namespace TaskbarLyrics.Light.App;

public partial class LyricsDisplayControl : System.Windows.Controls.UserControl
{
    private const string SearchingText = "正在检索歌词...";
    private const int SpectrumBarCount = 24;
    private const int TransitionDurationMs = 560;
    private const double TrackSwitchSearchMinVisibleMs = 900;
    private const double DescenderBuffer = 2;
    private const double MinHostHeight = 26;
    private const double MinRowHeight = 13;
    private const double RowDescenderPadding = 4;
    private const string RowHeightProbe = "国gyÁ";
    private const double CoverMinHeight = 34;
    private const double LyricsGridTopMargin = 3;
    private const double WindowVerticalMargin = 6;
    private const double WindowHorizontalMargin = 16;
    private const double SurfaceHorizontalPadding = 8;
    private const double CoverColumnWidth = 34;
    private const double CoverLyricsGap = 8;
    private const double LyricsColumnHorizontalMargin = 6;
    private const double LyricsTextPadding = 24;
    private const double MinLyricsContentWidth = 160;
    private const double SpectrumContentWidth = 230;

    private readonly Border[] _spectrumBars = new Border[SpectrumBarCount];
    private readonly double[] _spectrumTargets = new double[SpectrumBarCount];
    private readonly double[] _spectrumVisuals = new double[SpectrumBarCount];
    private SolidColorBrush _primaryBrush = CreateFrozenBrush(Media.Colors.White);
    private SolidColorBrush _secondaryBrush = CreateFrozenBrush(Media.Color.FromArgb(190, 255, 255, 255));
    private readonly CubicBezierEasing _slideEase = new(0.22, 0.72, 0.24, 1);
    private readonly Dictionary<RowHeightCacheKey, double> _rowHeightCache = new();

    private readonly DispatcherTimer _spectrumRenderTimer;

    private string _displayedCurrent = string.Empty;
    private string _displayedNext = string.Empty;
    private string _lastTrackId = string.Empty;
    private int _lastCurrentLineIndex = -1;
    private double _lastLineProgress;
    private double _secondaryOpacity = 0.72;
    private double _requestedFontSize = 13;
    private bool _autoAdjustLineGap = true;
    private double _manualLineGap = 2;
    private double _lineGapOffset;
    private double _rowHeightPx = 14;
    private double _nextRowHeightPx = 14;
    private double _rowGapPx = 1;
    private double _linePitchPx = 15;
    private double _currentFontSize = 13;
    private double _nextFontSize = 12;

    private bool _isTransitioning;
    private bool _transitionFinalized;
    private bool _isSpectrumMode;
    private bool _hasAudioDrivenSpectrum;
    private int _transitionGeneration;
    private LyricsFrame? _queuedFrame;
    private DateTime _trackSwitchSearchStartedAt;
    private DispatcherTimer? _searchDwellTimer;
    private DispatcherTimer? _transitionFallbackTimer;

    private EventHandler? _renderingHandler;
    private DateTime _transitionStartTime;
    private double _transitionBaseNextOpacity;
    private double _transitionBaseNextFontSize;
    private double _transitionBaseNextRowHeight;
    private double _transitionTargetCurrentFontSize;
    private string _transitionPromoted = string.Empty;
    private string _transitionUpcoming = string.Empty;
    private double _transitionProgress;
    private int _transitionLineIndex = -1;

    private bool _useCoverImageA = true;
    private int _coverGeneration;
    private bool _autoAdjustWindowWidth = true;
    private bool _metricsUpdatePending;
    private double _lastNotifiedPreferredHostHeight;
    private double _lastNotifiedPreferredContentWidth;

    private SpectrumTuningSettings _spectrumTuning = SpectrumTuningSettings.CreateDefault();
    private Media.FontFamily _fontFamily = new(AppSettings.DefaultFontFamily);
    private System.Windows.FontWeight _fontWeight = FontWeights.Bold;
    private Media.Effects.Effect? _textShadowEffect;

    public double PreferredHostHeight { get; private set; }

    public double PreferredContentWidth { get; private set; }

    public double PreferredWindowHeight =>
        Math.Max(
            CoverMinHeight + WindowVerticalMargin,
            PreferredHostHeight + LyricsGridTopMargin + WindowVerticalMargin);

    public double PreferredWindowWidth =>
        WindowWidthLimits.Clamp(
            WindowHorizontalMargin + SurfaceHorizontalPadding + CoverColumnWidth + CoverLyricsGap +
            LyricsColumnHorizontalMargin + PreferredContentWidth);

    public void NotifyScreenMetricsChanged()
    {
        _lastNotifiedPreferredContentWidth = -1;
        UpdatePreferredWidth();
    }

    public event EventHandler? PreferredHeightChanged;

    public event EventHandler? PreferredWidthChanged;

    public LyricsDisplayControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        RenderOptions.SetBitmapScalingMode(TrackPanel, BitmapScalingMode.HighQuality);

        for (var i = 0; i < SpectrumBarCount; i++)
        {
            var bar = new Border
            {
                Width = 3,
                Height = 8,
                CornerRadius = new CornerRadius(999),
                Background = _primaryBrush,
                Opacity = 0.72,
                Margin = new Thickness(i == 0 ? 0 : 1.5, 0, 1.5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new System.Windows.Point(0.5, 1),
                RenderTransform = new ScaleTransform(1, 1)
            };
            _spectrumBars[i] = bar;
            SpectrumPanel.Children.Add(bar);
        }

        _spectrumRenderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _spectrumRenderTimer.Tick += OnSpectrumRenderTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeChanged += (_, _) => UpdateMetrics();
        UpdateMetrics();
    }

    public void ApplyStyle(AppSettings settings, Media.Color primary, Media.Color secondary)
    {
        _primaryBrush = CreateFrozenBrush(primary);
        _secondaryBrush = CreateFrozenBrush(secondary);
        _requestedFontSize = Math.Clamp(settings.FontSize, 10, 40);
        _autoAdjustLineGap = settings.AutoAdjustLineGap;
        _manualLineGap = Math.Clamp(settings.LineGap, 0, 24);
        _lineGapOffset = Math.Clamp(settings.LineGapOffset, -12, 24);
        _fontFamily = new Media.FontFamily(BundledFontRegistrar.ResolveFontFamily(settings.FontFamily));
        _fontWeight = ParseFontWeight(settings.FontWeight);
        _rowHeightCache.Clear();
        _autoAdjustWindowWidth = settings.AutoAdjustWindowWidth;
        ApplyTextTrimming();

        SurfaceBorder.Background = settings.ShowBackground
            ? CreateFrozenBrush(Media.Color.FromArgb(
                (byte)Math.Clamp(settings.BackgroundOpacity * 255, 0, 255), 18, 18, 24))
            : Media.Brushes.Transparent;

        SurfaceBorder.BorderBrush = settings.ShowBorder
            ? CreateFrozenBrush(Media.Color.FromArgb(41, 255, 255, 255))
            : Media.Brushes.Transparent;
        SurfaceBorder.BorderThickness = settings.ShowBorder ? new Thickness(1) : new Thickness(0);

        _textShadowEffect = settings.ShowTextShadow
            ? new Media.Effects.DropShadowEffect
            {
                Color = Media.Colors.Black,
                Opacity = 0.36,
                BlurRadius = 2,
                ShadowDepth = 1,
                Direction = 270,
                RenderingBias = Media.Effects.RenderingBias.Performance
            }
            : null;

        CurrentLineText.Effect = _textShadowEffect;
        NextLineText.Effect = _textShadowEffect;
        IncomingLineText.Effect = _textShadowEffect;

        ApplyLineTypography(CurrentLineText, true);
        ApplyLineTypography(NextLineText, false);
        ApplyLineTypography(IncomingLineText, false);

        foreach (var bar in _spectrumBars)
        {
            bar.Background = _primaryBrush;
            bar.Effect = _textShadowEffect;
        }

        UpdateMetrics();
        UpdatePreferredWidth();
    }

    public void SetLyrics(
        string current,
        string next,
        double progress,
        int currentLineIndex,
        string? trackId,
        bool showSpectrum,
        bool isPlaying)
    {
        var safeCurrent = ToDisplayLine(current, SearchingText);
        var safeNext = ToDisplayLine(next, " ");
        var p = Math.Clamp(progress, 0, 1);
        var normalizedTrackId = trackId ?? string.Empty;

        if (showSpectrum)
        {
            if (normalizedTrackId.Length > 0)
            {
                _lastTrackId = normalizedTrackId;
            }

            CancelActiveTransition();
            _trackSwitchSearchStartedAt = DateTime.MinValue;
            SetCurrentLine(safeCurrent);
            SetSecondaryLine(" ");
            IncomingLineText.Text = " ";
            _lastCurrentLineIndex = currentLineIndex >= 0 ? currentLineIndex : -1;
            _lastLineProgress = p;
            SetDisplayMode(true);
            if (!isPlaying)
            {
                SetSpectrumTargetValues(null);
            }

            UpdatePreferredWidth();
            return;
        }

        SetDisplayMode(false);
        ClearSpectrumBars();

        if (normalizedTrackId.Length > 0 && normalizedTrackId != _lastTrackId)
        {
            ResetForTrackSwitch(safeCurrent, safeNext, p, currentLineIndex, normalizedTrackId);
            return;
        }

        if (normalizedTrackId.Length > 0)
        {
            _lastTrackId = normalizedTrackId;
        }

        ApplyFrame(safeCurrent, safeNext, p, currentLineIndex);
        UpdatePreferredWidth();
    }

    public void SetSpectrum(IReadOnlyList<float> values)
    {
        if (!_isSpectrumMode)
        {
            return;
        }

        if (values is null || values.Count == 0)
        {
            _hasAudioDrivenSpectrum = false;
            SetSpectrumTargetValues(null);
            return;
        }

        _hasAudioDrivenSpectrum = true;
        for (var i = 0; i < SpectrumBarCount; i++)
        {
            _spectrumTargets[i] = i < values.Count ? Math.Clamp(values[i], 0f, 1f) : 0;
        }

        StartSpectrumRenderer();
    }

    public void SetSpectrumTuning(SpectrumTuningSettings settings)
    {
        _spectrumTuning = settings.Clone();
        if (_isSpectrumMode)
        {
            StartSpectrumRenderer();
        }
    }

    public bool SetCover(byte[]? imageBytes, string fallbackText, Media.Color fallbackColor)
    {
        var generation = ++_coverGeneration;
        CoverBorder.Background = CreateFrozenBrush(fallbackColor);
        CoverFallbackText.Text = string.IsNullOrWhiteSpace(fallbackText) ? "N" : fallbackText[..1].ToUpperInvariant();

        if (imageBytes is not { Length: > 0 })
        {
            ShowCoverFallback();
            return false;
        }

        try
        {
            using var stream = new MemoryStream(imageBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            CrossfadeCover(bitmap, generation);
            return true;
        }
        catch
        {
            ShowCoverFallback();
            return false;
        }
    }

    private void CrossfadeCover(BitmapSource bitmap, int generation)
    {
        var incoming = _useCoverImageA ? CoverImageA : CoverImageB;
        var outgoing = _useCoverImageA ? CoverImageB : CoverImageA;

        incoming.BeginAnimation(OpacityProperty, null);
        outgoing.BeginAnimation(OpacityProperty, null);
        incoming.Source = bitmap;
        incoming.Opacity = 0;
        CoverFallbackText.Opacity = 0;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(440))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        fadeIn.Completed += (_, _) =>
        {
            if (generation != _coverGeneration)
            {
                return;
            }

            incoming.BeginAnimation(OpacityProperty, null);
            incoming.Opacity = 1;
        };

        var fadeOut = new DoubleAnimation(outgoing.Opacity, 0, TimeSpan.FromMilliseconds(440))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        fadeOut.Completed += (_, _) =>
        {
            if (generation != _coverGeneration)
            {
                return;
            }

            outgoing.BeginAnimation(OpacityProperty, null);
            outgoing.Source = null;
            outgoing.Opacity = 0;
            _useCoverImageA = !_useCoverImageA;
        };

        incoming.BeginAnimation(OpacityProperty, fadeIn);
        outgoing.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ShowCoverFallback()
    {
        CoverImageA.BeginAnimation(OpacityProperty, null);
        CoverImageB.BeginAnimation(OpacityProperty, null);
        CoverImageA.Source = null;
        CoverImageB.Source = null;
        CoverImageA.Opacity = 0;
        CoverImageB.Opacity = 0;
        CoverFallbackText.Opacity = 1;
    }

    private void ApplyFrame(string safeCurrent, string safeNext, double progress, int currentLineIndex)
    {
        if (_isTransitioning)
        {
            _queuedFrame = new LyricsFrame(safeCurrent, safeNext, progress, currentLineIndex);
            return;
        }

        var hasLineIndex = currentLineIndex >= 0;

        if (hasLineIndex)
        {
            if (_lastCurrentLineIndex < 0)
            {
                if (IsSearchingLine(_displayedCurrent))
                {
                    StartTransition(safeCurrent, safeNext, progress, currentLineIndex);
                }
                else
                {
                    SetCurrentLine(safeCurrent);
                    SetSecondaryLine(safeNext);
                    UpdateSecondaryOpacity(progress);
                }

                _lastCurrentLineIndex = currentLineIndex;
                _lastLineProgress = progress;
                return;
            }

            if (currentLineIndex != _lastCurrentLineIndex)
            {
                StartTransition(safeCurrent, safeNext, progress, currentLineIndex);
            }
            else
            {
                if (!string.Equals(safeCurrent, _displayedCurrent, StringComparison.Ordinal))
                {
                    SetCurrentLine(safeCurrent);
                }

                SetSecondaryLine(safeNext);
                UpdateSecondaryOpacity(progress);
            }

            _lastLineProgress = progress;
            return;
        }

        var isRepeatedPromotion = safeCurrent == _displayedCurrent &&
            _displayedNext == _displayedCurrent &&
            safeNext != _displayedNext;
        var isUnchanged = safeCurrent == _displayedCurrent && safeNext == _displayedNext;
        var wrappedProgress = isUnchanged &&
            !double.IsNaN(_lastLineProgress) &&
            (_lastLineProgress - progress) > 0.16 &&
            _lastLineProgress > 0.62;

        if (safeCurrent != _displayedCurrent || isRepeatedPromotion || wrappedProgress)
        {
            StartTransition(safeCurrent, safeNext, progress, -1);
        }
        else
        {
            SetSecondaryLine(safeNext);
            UpdateSecondaryOpacity(progress);
        }

        _lastLineProgress = progress;
    }

    private void ResetForTrackSwitch(string safeCurrent, string safeNext, double progress, int lineIndex, string trackId)
    {
        CancelActiveTransition();
        _lastTrackId = trackId;
        _lastCurrentLineIndex = -1;
        _lastLineProgress = 0;
        _trackSwitchSearchStartedAt = DateTime.UtcNow;

        var hasLyricFrame = lineIndex >= 0 && !IsSearchingLine(safeCurrent);

        if (!IsSearchingLine(_displayedCurrent))
        {
            StartTransition(SearchingText, " ", 0, -1);
            if (hasLyricFrame)
            {
                _queuedFrame = new LyricsFrame(safeCurrent, safeNext, progress, lineIndex);
            }
        }
        else
        {
            SetSecondaryLine(" ");
            UpdateSecondaryOpacity(0);
            if (hasLyricFrame)
            {
                ApplyFrameAfterSearchDwell(new LyricsFrame(safeCurrent, safeNext, progress, lineIndex));
            }
        }
    }

    private void ApplyFrameAfterSearchDwell(LyricsFrame frame)
    {
        _searchDwellTimer?.Stop();
        if (!ShouldHoldAfterSearch(frame))
        {
            ApplyFrame(frame.Current, frame.Next, frame.Progress, frame.LineIndex);
            return;
        }

        var elapsed = (DateTime.UtcNow - _trackSwitchSearchStartedAt).TotalMilliseconds;
        var delay = Math.Max(0, TrackSwitchSearchMinVisibleMs - elapsed);
        _searchDwellTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
        _searchDwellTimer.Tick += (_, _) =>
        {
            _searchDwellTimer.Stop();
            _trackSwitchSearchStartedAt = DateTime.MinValue;
            ApplyFrame(frame.Current, frame.Next, frame.Progress, frame.LineIndex);
        };
        _searchDwellTimer.Start();
    }

    private bool ShouldHoldAfterSearch(LyricsFrame frame) =>
        _trackSwitchSearchStartedAt != DateTime.MinValue &&
        IsSearchingLine(_displayedCurrent) &&
        frame.LineIndex >= 0 &&
        !IsSearchingLine(frame.Current);

    private void StartTransition(string newCurrent, string newNext, double progress, int currentLineIndex)
    {
        if (_isTransitioning)
        {
            _queuedFrame = new LyricsFrame(newCurrent, newNext, progress, currentLineIndex);
            return;
        }

        StopTransitionAnimations();
        _isTransitioning = true;
        _transitionFinalized = false;
        var generation = ++_transitionGeneration;

        var promoted = ToDisplayLine(newCurrent, SearchingText);
        var upcoming = ToDisplayLine(newNext, " ");
        _transitionPromoted = promoted;
        _transitionUpcoming = upcoming;
        _transitionProgress = progress;
        _transitionLineIndex = currentLineIndex;
        _transitionBaseNextOpacity = _secondaryOpacity;
        _transitionBaseNextFontSize = _nextFontSize;
        _transitionBaseNextRowHeight = _nextRowHeightPx;
        _transitionTargetCurrentFontSize = _currentFontSize;

        SetNoAnimState();
        NextLineText.Text = promoted;
        SetLineRowHeight(NextLineText, _rowHeightPx);
        NextLineText.Foreground = _primaryBrush;
        IncomingLineText.Text = upcoming;
        SetLineRowHeight(IncomingLineText, _nextRowHeightPx);
        IncomingLineText.Opacity = _secondaryOpacity;
        CurrentLineText.Opacity = 0.98;
        NextLineText.Opacity = _transitionBaseNextOpacity;
        NextLineText.FontSize = _transitionBaseNextFontSize;
        ClearNoAnimState();
        UpdatePreferredWidth();

        _transitionStartTime = DateTime.UtcNow;
        StartTransitionRendering(generation);

        _transitionFallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TransitionDurationMs + 120) };
        _transitionFallbackTimer.Tick += (_, _) =>
        {
            _transitionFallbackTimer?.Stop();
            if (generation != _transitionGeneration)
            {
                return;
            }

            FinalizeTransition();
        };
        _transitionFallbackTimer.Start();
    }

    private void StartTransitionRendering(int generation)
    {
        StopOpacityRendering();
        TrackTransform.BeginAnimation(TranslateTransform.YProperty, null);
        TrackTransform.Y = 0;

        _renderingHandler = (_, _) =>
        {
            if (generation != _transitionGeneration || !_isTransitioning)
            {
                StopOpacityRendering();
                return;
            }

            var elapsed = (DateTime.UtcNow - _transitionStartTime).TotalMilliseconds;
            var t = Math.Clamp(elapsed / TransitionDurationMs, 0, 1);
            var fadeOutE = GetFadeOutEase(t);
            var fadeInE = GetFadeInEase(t);
            var sizeE = GetSizeEase(t);
            var slideE = _slideEase.Ease(t);

            TrackTransform.Y = -_linePitchPx * slideE;
            CurrentLineText.Opacity = 0.98 + ((0.16 - 0.98) * fadeOutE);
            NextLineText.Opacity = _transitionBaseNextOpacity + ((0.98 - _transitionBaseNextOpacity) * fadeInE);
            IncomingLineText.Opacity = _secondaryOpacity;
            NextLineText.MinHeight = _transitionBaseNextRowHeight +
                ((_rowHeightPx - _transitionBaseNextRowHeight) * sizeE);
            NextLineText.ClearValue(FrameworkElement.HeightProperty);
            IncomingLineText.MinHeight = _nextRowHeightPx;
            NextLineText.FontSize = _transitionBaseNextFontSize +
                ((_transitionTargetCurrentFontSize - _transitionBaseNextFontSize) * sizeE);

            if (t >= 1)
            {
                StopOpacityRendering();
                _transitionFallbackTimer?.Stop();
                FinalizeTransition();
            }
        };
        CompositionTarget.Rendering += _renderingHandler;
    }

    private void StopOpacityRendering()
    {
        if (_renderingHandler is not null)
        {
            CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }
    }

    private void FinalizeTransition()
    {
        if (_transitionFinalized)
        {
            return;
        }

        _transitionFinalized = true;
        StopTransitionAnimations();

        SetNoAnimState();
        SetCurrentLine(_transitionPromoted);
        SetSecondaryLine(_transitionUpcoming);
        IncomingLineText.Text = " ";
        TrackTransform.Y = 0;

        CurrentLineText.ClearValue(OpacityProperty);
        NextLineText.ClearValue(OpacityProperty);
        NextLineText.ClearValue(FontSizeProperty);
        IncomingLineText.ClearValue(OpacityProperty);
        NextLineText.Foreground = _secondaryBrush;
        CurrentLineText.Opacity = 0.98;
        ApplyLineMetrics();

        _secondaryOpacity = IncomingLineText.Opacity;
        if (double.IsNaN(_secondaryOpacity) || _secondaryOpacity <= 0.01)
        {
            _secondaryOpacity = 0.72;
        }

        ClearNoAnimState();

        UpdateSecondaryOpacity(_transitionProgress);
        _isTransitioning = false;
        _lastLineProgress = Math.Clamp(_transitionProgress, 0, 1);

        if (_transitionLineIndex >= 0)
        {
            _lastCurrentLineIndex = _transitionLineIndex;
        }

        if (_metricsUpdatePending)
        {
            UpdateMetrics();
        }

        if (_queuedFrame is { } frame)
        {
            _queuedFrame = null;
            ApplyFrameAfterSearchDwell(frame);
        }
    }

    private void SetNoAnimState()
    {
        TrackTransform.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void ClearNoAnimState()
    {
        TrackTransform.BeginAnimation(TranslateTransform.YProperty, null);
        TrackTransform.Y = 0;
    }

    private void StopTransitionAnimations()
    {
        _transitionFallbackTimer?.Stop();
        _transitionFallbackTimer = null;
        StopOpacityRendering();
        TrackTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CurrentLineText.BeginAnimation(OpacityProperty, null);
        NextLineText.BeginAnimation(OpacityProperty, null);
        NextLineText.BeginAnimation(FontSizeProperty, null);
    }

    private void CancelActiveTransition()
    {
        _transitionGeneration++;
        _searchDwellTimer?.Stop();
        StopTransitionAnimations();
        _isTransitioning = false;
        _transitionFinalized = false;
        _queuedFrame = null;
        TrackTransform.Y = 0;
        CurrentLineText.Opacity = 0.98;
        NextLineText.Opacity = _secondaryOpacity;
        NextLineText.Foreground = _secondaryBrush;
        ApplyLineMetrics();
        IncomingLineText.Opacity = 0;
    }

    private void SetDisplayMode(bool showSpectrum)
    {
        if (_isSpectrumMode == showSpectrum)
        {
            return;
        }

        _isSpectrumMode = showSpectrum;
        AnimateLayerOpacity(LyricsLayer, showSpectrum ? 0 : 1);
        AnimateLayerOpacity(SpectrumLayer, showSpectrum ? 1 : 0);

        if (showSpectrum)
        {
            StartSpectrumRenderer();
        }
        else
        {
            ClearSpectrumBars();
        }

        UpdatePreferredWidth();
    }

    private static void AnimateLayerOpacity(UIElement layer, double target)
    {
        layer.BeginAnimation(UIElement.OpacityProperty, null);
        var animation = new DoubleAnimation(layer.Opacity, target, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        layer.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void SetSpectrumTargetValues(IReadOnlyList<float>? values)
    {
        for (var i = 0; i < SpectrumBarCount; i++)
        {
            _spectrumTargets[i] = values is not null && i < values.Count
                ? Math.Clamp(values[i], 0f, 1f)
                : 0;
        }

        StartSpectrumRenderer();
    }

    private void ClearSpectrumBars()
    {
        _hasAudioDrivenSpectrum = false;
        for (var i = 0; i < SpectrumBarCount; i++)
        {
            _spectrumTargets[i] = 0;
            _spectrumVisuals[i] = 0;
            _spectrumBars[i].Height = 8;
        }

        _spectrumRenderTimer.Stop();
    }

    private void StartSpectrumRenderer()
    {
        if (!_spectrumRenderTimer.IsEnabled)
        {
            _spectrumRenderTimer.Start();
        }
    }

    private void OnSpectrumRenderTick(object? sender, EventArgs e)
    {
        var isSettled = true;
        for (var i = 0; i < SpectrumBarCount; i++)
        {
            var target = _spectrumTargets[i];
            var current = _spectrumVisuals[i];
            var rate = target > current ? _spectrumTuning.FrontendRise : _spectrumTuning.FrontendFall;
            var next = current + ((target - current) * rate);
            _spectrumVisuals[i] = Math.Abs(next - target) < 0.002 ? target : next;

            if (Math.Abs(_spectrumVisuals[i] - target) >= 0.002)
            {
                isSettled = false;
            }

            var level = _spectrumVisuals[i];
            _spectrumBars[i].Height = _spectrumTuning.MinBarHeight + (level * _spectrumTuning.BarHeightRange);
            _spectrumBars[i].Opacity = _spectrumTuning.BarOpacity;
        }

        if (!_hasAudioDrivenSpectrum && isSettled)
        {
            _spectrumRenderTimer.Stop();
        }
    }

    private void UpdateMetrics()
    {
        var idealHost = ComputeMetricsFromFont(_requestedFontSize).HostHeight;
        NotifyPreferredHeightIfChanged(idealHost + DescenderBuffer);

        if (_isTransitioning)
        {
            _metricsUpdatePending = true;
            return;
        }

        _metricsUpdatePending = false;
        if (ActualHeight <= 0)
        {
            ApplyMetricsFromFont(_requestedFontSize);
            ApplyLineMetrics();
            return;
        }

        var availableHost = Math.Max(MinHostHeight, ActualHeight - DescenderBuffer - LyricsGridTopMargin);
        ApplyMetricsFromFont(_requestedFontSize, availableHost);
        ApplyLineMetrics();
        TrackTransform.Y = 0;
        UpdatePreferredWidth();
    }

    private double ResolveLineGap(double fontSize) =>
        _autoAdjustLineGap
            ? Math.Max(0, Math.Round(fontSize * 0.06) + _lineGapOffset)
            : Math.Max(0, _manualLineGap);

    private double MeasureFontRowHeight(double fontSize)
    {
        var cacheKey = new RowHeightCacheKey(_fontFamily.Source, fontSize, _fontWeight);
        if (_rowHeightCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var dpi = IsLoaded ? VisualTreeHelper.GetDpi(this).PixelsPerDip : 1.0;
        var formatted = new FormattedText(
            RowHeightProbe,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(_fontFamily, FontStyles.Normal, _fontWeight, FontStretches.Normal),
            fontSize,
            _primaryBrush,
            dpi);
        var measured = Math.Max(MinRowHeight, Math.Ceiling(formatted.Height) + RowDescenderPadding);
        _rowHeightCache[cacheKey] = measured;
        return measured;
    }

    private static SolidColorBrush CreateFrozenBrush(Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private readonly record struct RowHeightCacheKey(string FontFamily, double FontSize, FontWeight Weight);

    private (double HostHeight, double PrimaryRow, double NextRow, double Gap) ComputeMetricsFromFont(double fontSize)
    {
        var nextSize = Math.Max(9, fontSize * 0.92);
        var primaryRow = MeasureFontRowHeight(fontSize);
        var nextRow = MeasureFontRowHeight(nextSize);
        var gap = ResolveLineGap(fontSize);
        return (primaryRow + gap + nextRow, primaryRow, nextRow, gap);
    }

    private void ApplyMetricsFromFont(double requestedFont, double? maxHost = null)
    {
        var fontSize = requestedFont;
        var metrics = ComputeMetricsFromFont(fontSize);

        if (maxHost.HasValue && metrics.HostHeight > maxHost.Value + 0.5)
        {
            var scale = maxHost.Value / metrics.HostHeight;
            fontSize = Math.Clamp(requestedFont * scale, 10, requestedFont);
            metrics = ComputeMetricsFromFont(fontSize);
        }

        _currentFontSize = fontSize;
        _nextFontSize = Math.Max(9, fontSize * 0.92);
        _rowHeightPx = metrics.PrimaryRow;
        _nextRowHeightPx = metrics.NextRow;
        _rowGapPx = metrics.Gap;
        _linePitchPx = metrics.PrimaryRow + metrics.Gap;
    }

    private static void SetLineRowHeight(TextBlock textBlock, double minHeight)
    {
        textBlock.MinHeight = minHeight;
        textBlock.ClearValue(FrameworkElement.HeightProperty);
    }

    private void ApplyLineMetrics()
    {
        SetLineRowHeight(CurrentLineText, _rowHeightPx);
        CurrentLineText.FontSize = _currentFontSize;
        NextLineText.Margin = new Thickness(0, _rowGapPx, 0, 0);
        SetLineRowHeight(NextLineText, _nextRowHeightPx);
        NextLineText.FontSize = _nextFontSize;
        IncomingLineText.Margin = new Thickness(0, _rowGapPx, 0, 0);
        SetLineRowHeight(IncomingLineText, _nextRowHeightPx);
        IncomingLineText.FontSize = _nextFontSize;
    }

    private void NotifyPreferredHeightIfChanged(double hostHeight)
    {
        if (Math.Abs(hostHeight - _lastNotifiedPreferredHostHeight) < 0.5)
        {
            return;
        }

        _lastNotifiedPreferredHostHeight = hostHeight;
        PreferredHostHeight = hostHeight;
        PreferredHeightChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePreferredWidth()
    {
        var contentWidth = _isSpectrumMode
            ? SpectrumContentWidth
            : Math.Max(MinLyricsContentWidth, MeasurePreferredTextWidth() + LyricsTextPadding);

        NotifyPreferredContentWidthIfChanged(contentWidth);
    }

    private double MeasurePreferredTextWidth()
    {
        var lines = new HashSet<string>();
        AddLineIfMeaningful(lines, _displayedCurrent);
        AddLineIfMeaningful(lines, _displayedNext);
        AddLineIfMeaningful(lines, CurrentLineText.Text);
        AddLineIfMeaningful(lines, NextLineText.Text);
        AddLineIfMeaningful(lines, IncomingLineText.Text);
        AddLineIfMeaningful(lines, _transitionPromoted);
        AddLineIfMeaningful(lines, _transitionUpcoming);

        return lines.Count == 0 ? 0 : lines.Max(MeasureLineWidth);
    }

    private static void AddLineIfMeaningful(ISet<string> lines, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == " ")
        {
            return;
        }

        lines.Add(text);
    }

    private double MeasureLineWidth(string text)
    {
        var dpi = IsLoaded ? VisualTreeHelper.GetDpi(this).PixelsPerDip : 1.0;
        return Math.Max(
            MeasureLineWidthAt(text, _currentFontSize, dpi),
            MeasureLineWidthAt(text, _nextFontSize, dpi));
    }

    private double MeasureLineWidthAt(string text, double fontSize, double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(_fontFamily, FontStyles.Normal, _fontWeight, FontStretches.Normal),
            fontSize,
            _primaryBrush,
            new NumberSubstitution(),
            TextFormattingMode.Display,
            pixelsPerDip);
        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
    }

    private void ApplyTextTrimming()
    {
        var trimming = _autoAdjustWindowWidth ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        CurrentLineText.TextTrimming = trimming;
        NextLineText.TextTrimming = trimming;
        IncomingLineText.TextTrimming = trimming;
    }

    private void NotifyPreferredContentWidthIfChanged(double contentWidth)
    {
        if (Math.Abs(contentWidth - _lastNotifiedPreferredContentWidth) < 4)
        {
            return;
        }

        _lastNotifiedPreferredContentWidth = contentWidth;
        PreferredContentWidth = contentWidth;
        PreferredWidthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetCurrentLine(string line)
    {
        var safe = ToDisplayLine(line, SearchingText);
        if (safe == _displayedCurrent)
        {
            return;
        }

        CurrentLineText.Text = safe;
        _displayedCurrent = safe;
        UpdatePreferredWidth();
    }

    private void SetSecondaryLine(string line)
    {
        var safe = ToDisplayLine(line, " ");
        if (safe == _displayedNext)
        {
            return;
        }

        NextLineText.Text = safe;
        _displayedNext = safe;
        UpdatePreferredWidth();
    }

    private void UpdateSecondaryOpacity(double progress)
    {
        if (_isTransitioning)
        {
            return;
        }

        var target = 0.58 + ((1 - Math.Clamp(progress, 0, 1)) * 0.16);
        _secondaryOpacity += (target - _secondaryOpacity) * 0.28;
        NextLineText.Opacity = _secondaryOpacity;
    }

    private void ApplyLineTypography(TextBlock textBlock, bool isPrimary)
    {
        textBlock.FontFamily = _fontFamily;
        textBlock.FontWeight = _fontWeight;
        textBlock.Foreground = isPrimary ? _primaryBrush : _secondaryBrush;
    }

    private static string ToDisplayLine(string? line, string fallback)
    {
        var text = (line ?? string.Empty).Trim();
        return text.Length > 0 ? text : fallback;
    }

    private static bool IsSearchingLine(string line) =>
        line is SearchingText or "正在匹配歌词...";

    private static double EaseOutCubic(double t)
    {
        var x = 1 - Math.Clamp(t, 0, 1);
        return 1 - (x * x * x);
    }

    private static double GetSizeEase(double t) => EaseOutCubic(Math.Clamp(t / 0.86, 0, 1));

    private static double GetFadeOutEase(double t)
    {
        var normalized = Math.Clamp(t / 0.74, 0, 1);
        return normalized >= 0.97 ? 1 : EaseOutCubic(normalized);
    }

    private static double GetFadeInEase(double t)
    {
        var normalized = Math.Clamp(t / 0.72, 0, 1);
        return normalized >= 0.96 ? 1 : EaseOutCubic(normalized);
    }

    private static System.Windows.FontWeight ParseFontWeight(string? weight) => (weight ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "light" => FontWeights.Light,
        "normal" => FontWeights.Normal,
        "medium" => FontWeights.Medium,
        "semibold" => FontWeights.SemiBold,
        "bold" => FontWeights.Bold,
        _ => FontWeights.Medium
    };

    private sealed record LyricsFrame(string Current, string Next, double Progress, int LineIndex);
}
