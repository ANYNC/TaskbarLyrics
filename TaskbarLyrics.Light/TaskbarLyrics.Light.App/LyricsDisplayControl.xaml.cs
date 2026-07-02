using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using SixLabors.ImageSharp.Formats.Png;
using TaskbarLyrics.Light.App.Ui;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Media = System.Windows.Media;
using ImageSharpImage = SixLabors.ImageSharp.Image;

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
    private const double DefaultCoverSize = 34;
    private const double LyricsGridTopMargin = 3;
    private const double WindowVerticalMargin = 6;
    private const double StackedTransitionClipBuffer = 8;
    private const double WindowHorizontalMargin = 16;
    private const double SurfaceHorizontalPadding = 8;
    private const double DefaultCoverGap = 8;
    private const double LyricsColumnHorizontalMargin = 6;
    private const double LyricsTextPadding = 24;
    private const double StackedSideMargin = 4;
    private const double MinLyricsContentWidth = 160;
    private const double SpectrumContentWidth = 230;
    private const int ProgressDotCount = 28;
    private const double TimePillWidthReserve = 92;
    private const int MaxTextWidthCacheEntries = 512;

    private readonly Border[] _spectrumBars = new Border[SpectrumBarCount];
    private readonly double[] _spectrumTargets = new double[SpectrumBarCount];
    private readonly double[] _spectrumVisuals = new double[SpectrumBarCount];
    private readonly Border[] _progressDots = new Border[ProgressDotCount];
    private SolidColorBrush _primaryBrush = CreateFrozenBrush(Media.Colors.White);
    private SolidColorBrush _secondaryBrush = CreateFrozenBrush(Media.Color.FromArgb(190, 255, 255, 255));
    private SolidColorBrush _progressFillBrush = CreateFrozenBrush(Media.Colors.White);
    private SolidColorBrush _progressTrackBrush = CreateFrozenBrush(Media.Color.FromArgb(36, 255, 255, 255));
    private readonly CubicBezierEasing _slideEase = new(0.22, 0.72, 0.24, 1);
    private readonly Dictionary<RowHeightCacheKey, double> _rowHeightCache = new();
    private readonly Dictionary<TextWidthCacheKey, double> _textWidthCache = new();

    private EventHandler? _spectrumRenderingHandler;

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
    private SpectrumDisplayStyle _spectrumStyle = SpectrumDisplayStyle.Center;
    private LyricTransitionStyle _transitionStyle = LyricTransitionStyle.Slide;
    private SongProgressDisplayStyle _songProgressStyle = SongProgressDisplayStyle.Off;
    private LyricTranslationLayout _translationLayout = LyricTranslationLayout.Inline;
    private LyricKaraokeEffect _karaokeEffect = LyricKaraokeEffect.Off;
    private TextEffectStyle _textEffectStyle = TextEffectStyle.Shadow;
    private SpectrumColorMode _spectrumColorMode = SpectrumColorMode.Text;
    private AnimationIntensity _animationIntensity = AnimationIntensity.Standard;
    private double _translationFontScale = 0.86;
    private double _translationOpacity = 0.72;
    private double _songProgressThickness = 2;
    private double _songProgressOpacity = 0.9;
    private int _transitionGeneration;
    private LyricsFrame? _queuedFrame;
    private DateTime _trackSwitchSearchStartedAt;
    private DispatcherTimer? _searchDwellTimer;
    private DispatcherTimer? _transitionFallbackTimer;

    private EventHandler? _renderingHandler;
    private long _transitionStartTimestamp;
    private double _transitionBaseNextOpacity;
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
    private double _lastNotifiedPreferredWindowWidth;

    private SpectrumTuningSettings _spectrumTuning = SpectrumTuningSettings.CreateDefault();
    private Media.FontFamily _fontFamily = new(AppSettings.DefaultFontFamily);
    private System.Windows.FontWeight _fontWeight = FontWeights.Bold;
    private Media.Effects.Effect? _textShadowEffect;
    private Media.Color? _coverAccentColor;
    private double _coverSize = DefaultCoverSize;
    private double _coverCornerRadius = 6;
    private double _coverGap = DefaultCoverGap;
    private CoverDisplayStyle _coverDisplayStyle = CoverDisplayStyle.RoundedSquare;
    private bool _showCoverGlow;
    private double _coverGlowOpacity = 0.22;
    private double _stackedCoverLyricsGap = 6;
    private double _stackedCoverXOffset;
    private double _stackedCoverYOffset;
    private bool _showStackedTrackInfo;
    private double _stackedTrackInfoGap = 8;
    private double _stackedContentXOffset;
    private double _stackedContentYOffset;
    private CoverLayoutMode _coverLayoutMode = CoverLayoutMode.Inline;
    private bool _showCover = true;
    private bool _hasSongProgress;
    private bool _isSongProgressPlaying;
    private double _songProgress;
    private TimeSpan _songProgressPosition;
    private TimeSpan _songProgressDuration;
    private string _trackTitle = string.Empty;
    private string _trackArtist = string.Empty;

    public double PreferredHostHeight { get; private set; }

    public double PreferredContentWidth { get; private set; }

    private bool IsStackedCoverLayout => _showCover && _coverLayoutMode == CoverLayoutMode.Stacked;

    private bool IsStackedTrackInfoVisible =>
        IsStackedCoverLayout &&
        _showStackedTrackInfo &&
        (!string.IsNullOrWhiteSpace(_trackTitle) || !string.IsNullOrWhiteSpace(_trackArtist));

    public double PreferredWindowHeight =>
        IsStackedCoverLayout
            ? _coverSize + _stackedCoverLyricsGap + PreferredHostHeight + LyricsGridTopMargin + WindowVerticalMargin +
                (StackedTransitionClipBuffer * 2)
            : PreferredWindowBottomAnchorHeight;

    public double PreferredWindowBottomAnchorHeight =>
        Math.Max(
            (_showCover ? _coverSize : 0) + WindowVerticalMargin,
            PreferredHostHeight + LyricsGridTopMargin + WindowVerticalMargin);

    public double PreferredWindowWidth =>
        WindowWidthLimits.Clamp(
            WindowHorizontalMargin +
            SurfaceHorizontalPadding +
            GetPreferredLayoutContentWidth() +
            GetSongProgressWidthReserve());

    public void NotifyScreenMetricsChanged()
    {
        _lastNotifiedPreferredContentWidth = -1;
        _lastNotifiedPreferredWindowWidth = -1;
        UpdatePreferredWidth();
    }

    public event EventHandler? PreferredHeightChanged;

    public event EventHandler? PreferredWidthChanged;

    public LyricsDisplayControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        RenderOptions.SetBitmapScalingMode(TrackPanel, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(CoverImageA, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(CoverImageB, BitmapScalingMode.HighQuality);
        CoverBorder.SizeChanged += (_, _) =>
        {
            ApplyCoverClip();
            UpdateCoverProgressGeometry();
        };
        SizeChanged += (_, _) => UpdateSongProgressVisuals();

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
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            _spectrumBars[i] = bar;
            SpectrumPanel.Children.Add(bar);
        }

        for (var i = 0; i < ProgressDotCount; i++)
        {
            var dot = new Border
            {
                Width = 4,
                Height = 4,
                CornerRadius = new CornerRadius(999),
                Background = _progressFillBrush,
                Opacity = 0.18,
                Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            _progressDots[i] = dot;
            ProgressDotsPanel.Children.Add(dot);
        }

        ApplySpectrumBarMetrics();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeChanged += (_, _) => UpdateMetrics();
        UpdateMetrics();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopSpectrumRenderer();
        StopTransitionAnimations();
        _searchDwellTimer?.Stop();
    }

    public void ApplyStyle(
        AppSettings settings,
        Media.Color primary,
        Media.Color secondary,
        Media.Color? coverAccentColor = null,
        Media.Color? surfaceColorSeed = null)
    {
        _primaryBrush = CreateFrozenBrush(primary);
        _secondaryBrush = CreateFrozenBrush(secondary);
        _coverAccentColor = coverAccentColor;
        _requestedFontSize = Math.Clamp(settings.FontSize, 10, 40);
        _autoAdjustLineGap = settings.AutoAdjustLineGap;
        _manualLineGap = Math.Clamp(settings.LineGap, 0, 24);
        _lineGapOffset = Math.Clamp(settings.LineGapOffset, -12, 24);
        _fontFamily = new Media.FontFamily(BundledFontRegistrar.ResolveFontFamily(settings.FontFamily));
        _fontWeight = ParseFontWeight(settings.FontWeight);
        _transitionStyle = Enum.IsDefined(settings.TransitionStyle)
            ? settings.TransitionStyle
            : LyricTransitionStyle.Slide;
        _translationLayout = Enum.IsDefined(settings.TranslationLayout)
            ? settings.TranslationLayout
            : LyricTranslationLayout.Inline;
        _translationFontScale = Math.Clamp(settings.TranslationFontScale, 0.62, 1);
        _translationOpacity = Math.Clamp(settings.TranslationOpacity, 0.18, 1);
        _karaokeEffect = Enum.IsDefined(settings.KaraokeEffect)
            ? settings.KaraokeEffect
            : LyricKaraokeEffect.Off;
        _textEffectStyle = Enum.IsDefined(settings.TextEffectStyle)
            ? settings.TextEffectStyle
            : (settings.ShowTextShadow ? TextEffectStyle.Shadow : TextEffectStyle.None);
        if (!settings.ShowTextShadow)
        {
            _textEffectStyle = TextEffectStyle.None;
        }
        _spectrumColorMode = Enum.IsDefined(settings.SpectrumColorMode)
            ? settings.SpectrumColorMode
            : SpectrumColorMode.Text;
        _animationIntensity = Enum.IsDefined(settings.AnimationIntensity)
            ? settings.AnimationIntensity
            : AnimationIntensity.Standard;
        _songProgressThickness = Math.Clamp(settings.SongProgressThickness, 1, 8);
        _songProgressOpacity = Math.Clamp(settings.SongProgressOpacity, 0.15, 1);
        _rowHeightCache.Clear();
        _textWidthCache.Clear();
        _autoAdjustWindowWidth = settings.AutoAdjustWindowWidth;
        _songProgressStyle = Enum.IsDefined(settings.SongProgressStyle)
            ? settings.SongProgressStyle
            : SongProgressDisplayStyle.Off;
        ApplySpectrumStyle(settings.SpectrumStyle);
        ApplyProgressVisualStyle();
        ApplyCoverStyle(settings);
        ApplyTextTrimming();

        SurfaceBorder.Background = CreateSurfaceBrush(settings, surfaceColorSeed ?? primary);

        SurfaceBorder.BorderBrush = settings.ShowBorder
            ? CreateFrozenBrush(Media.Color.FromArgb(41, 255, 255, 255))
            : Media.Brushes.Transparent;
        SurfaceBorder.BorderThickness = settings.ShowBorder ? new Thickness(1) : new Thickness(0);

        _textShadowEffect = CreateTextEffect();

        CurrentLineText.Effect = _textShadowEffect;
        NextLineText.Effect = _textShadowEffect;
        IncomingLineText.Effect = _textShadowEffect;
        TrackTitleText.Effect = _textShadowEffect;
        TrackArtistText.Effect = _textShadowEffect;

        ApplyLineTypography(CurrentLineText, true);
        ApplyLineTypography(NextLineText, false);
        ApplyLineTypography(IncomingLineText, false);
        ApplyTrackInfoTypography();
        ApplyDisplayLine(CurrentLineText, _displayedCurrent, true, _lastLineProgress);
        ApplyDisplayLine(NextLineText, _displayedNext, false, 0);

        for (var i = 0; i < _spectrumBars.Length; i++)
        {
            var bar = _spectrumBars[i];
            bar.Background = GetSpectrumBrush(i);
            bar.Effect = _textShadowEffect;
        }

        UpdateMetrics();
        UpdatePreferredWidth();
        UpdateSongProgressVisuals();
    }

    private Media.Brush CreateSurfaceBrush(AppSettings settings, Media.Color surfaceColorSeed)
    {
        if (!settings.ShowBackground)
        {
            return Media.Brushes.Transparent;
        }

        var alpha = (byte)Math.Clamp(settings.BackgroundOpacity * 255, 0, 255);
        var color = settings.BackgroundMaterial switch
        {
            LyricsBackgroundMaterial.CoverTint when _coverAccentColor is { } accent =>
                Media.Color.FromArgb(alpha, accent.R, accent.G, accent.B),
            LyricsBackgroundMaterial.Solid =>
                Media.Color.FromArgb(alpha, surfaceColorSeed.R, surfaceColorSeed.G, surfaceColorSeed.B),
            _ => Media.Color.FromArgb(alpha, 18, 18, 24)
        };

        return CreateFrozenBrush(color);
    }

    private void ApplyCoverStyle(AppSettings settings)
    {
        var style = settings.ShowCoverImage
            ? NormalizeCoverStyle(settings.CoverStyle)
            : CoverDisplayStyle.Hidden;
        _coverDisplayStyle = style;
        _coverLayoutMode = Enum.IsDefined(settings.CoverLayoutMode)
            ? settings.CoverLayoutMode
            : CoverLayoutMode.Inline;
        _showCover = style != CoverDisplayStyle.Hidden;
        _showCoverGlow = settings.ShowCoverGlow && _showCover;
        _coverGlowOpacity = Math.Clamp(settings.CoverGlowOpacity, 0, 0.8);
        _stackedCoverLyricsGap = Math.Clamp(settings.StackedCoverLyricsGap, 0, 40);
        _stackedCoverXOffset = Math.Clamp(settings.StackedCoverXOffset, -120, 120);
        _stackedCoverYOffset = Math.Clamp(settings.StackedCoverYOffset, -80, 80);
        _showStackedTrackInfo = settings.ShowStackedTrackInfo;
        _stackedTrackInfoGap = Math.Clamp(settings.StackedTrackInfoGap, 0, 80);
        _stackedContentXOffset = Math.Clamp(settings.StackedContentXOffset, -160, 160);
        _stackedContentYOffset = Math.Clamp(settings.StackedContentYOffset, -80, 80);
        _coverSize = Math.Clamp(settings.CoverSize, 24, 96);
        _coverGap = _showCover && !IsStackedCoverLayout ? DefaultCoverGap : 0;

        CoverBorder.Visibility = _showCover ? Visibility.Visible : Visibility.Collapsed;
        CoverBorder.Width = _coverSize;
        CoverBorder.Height = _coverSize;
        CoverGlowBorder.Visibility = _showCoverGlow ? Visibility.Visible : Visibility.Collapsed;
        CoverGlowBorder.Width = _coverSize;
        CoverGlowBorder.Height = _coverSize;
        _coverCornerRadius = style switch
        {
            CoverDisplayStyle.Circle => _coverSize / 2,
            CoverDisplayStyle.Square => 0,
            _ => Math.Clamp(Math.Round(_coverSize * 0.18), 6, 12)
        };
        CoverBorder.CornerRadius = new CornerRadius(_coverCornerRadius);
        CoverGlowBorder.CornerRadius = new CornerRadius(Math.Max(_coverCornerRadius, 8));
        ApplyCoverLayout();
        ApplyCoverClip();
        ApplyCoverGlow();
        UpdateCoverProgressGeometry();
    }

    private static CoverDisplayStyle NormalizeCoverStyle(CoverDisplayStyle style) =>
        style == CoverDisplayStyle.Large ? CoverDisplayStyle.RoundedSquare : style;

    private void ApplyCoverLayout()
    {
        if (IsStackedCoverLayout)
        {
            CoverRow.Height = new GridLength(_coverSize);
            CoverStackGapRow.Height = new GridLength(_stackedCoverLyricsGap);
            ContentRow.Height = new GridLength(1, GridUnitType.Star);
            CoverColumn.Width = new GridLength(0);
            CoverGapColumn.Width = new GridLength(0);
            ContentColumn.Width = new GridLength(1, GridUnitType.Star);

            Grid.SetRow(CoverBorder, 0);
            Grid.SetColumn(CoverBorder, 0);
            Grid.SetColumnSpan(CoverBorder, 3);
            Grid.SetRow(CoverGlowBorder, 0);
            Grid.SetColumn(CoverGlowBorder, 0);
            Grid.SetColumnSpan(CoverGlowBorder, 3);
            Grid.SetRow(ContentGrid, 2);
            Grid.SetColumn(ContentGrid, 0);
            Grid.SetColumnSpan(ContentGrid, 3);

            LayoutGrid.Margin = new Thickness(GetStackedLayoutLeftInset(), 0, 0, 0);
            CoverBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            CoverBorder.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            CoverBorder.Margin = new Thickness(StackedSideMargin, 0, 0, 0);
            CoverGlowBorder.HorizontalAlignment = CoverBorder.HorizontalAlignment;
            CoverGlowBorder.VerticalAlignment = CoverBorder.VerticalAlignment;
            CoverGlowBorder.Margin = CoverBorder.Margin;
            ContentGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            ContentGrid.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            ContentGrid.Margin = new Thickness(
                StackedSideMargin,
                LyricsGridTopMargin,
                StackedSideMargin + GetTimePillContentInset(),
                0);
            LyricsViewport.Margin = new Thickness(0, -StackedTransitionClipBuffer, 0, -2 - StackedTransitionClipBuffer);
            CoverBorder.RenderTransform = new TranslateTransform(_stackedCoverXOffset, _stackedCoverYOffset);
            CoverGlowBorder.RenderTransform = new TranslateTransform(_stackedCoverXOffset, _stackedCoverYOffset);
            StackedTrackInfoPanel.Visibility = IsStackedTrackInfoVisible ? Visibility.Visible : Visibility.Collapsed;
            StackedTrackInfoPanel.Margin = new Thickness(StackedSideMargin + _coverSize + _stackedTrackInfoGap, 0, StackedSideMargin, 0);
            StackedTrackInfoPanel.MaxWidth = MeasureStackedTrackInfoWidth();
            StackedTrackInfoPanel.RenderTransform = new TranslateTransform(_stackedCoverXOffset, _stackedCoverYOffset);
            ContentGrid.RenderTransform = new TranslateTransform(_stackedContentXOffset, _stackedContentYOffset);
            ApplyCoverProgressLayout();
            return;
        }

        CoverRow.Height = new GridLength(1, GridUnitType.Star);
        CoverStackGapRow.Height = new GridLength(0);
        ContentRow.Height = new GridLength(0);
        CoverColumn.Width = new GridLength(_showCover ? _coverSize : 0);
        CoverGapColumn.Width = new GridLength(_coverGap);
        ContentColumn.Width = new GridLength(1, GridUnitType.Star);

        Grid.SetRow(CoverBorder, 0);
        Grid.SetColumn(CoverBorder, 0);
        Grid.SetColumnSpan(CoverBorder, 1);
        Grid.SetRow(CoverGlowBorder, 0);
        Grid.SetColumn(CoverGlowBorder, 0);
        Grid.SetColumnSpan(CoverGlowBorder, 1);
        Grid.SetRow(ContentGrid, 0);
        Grid.SetColumn(ContentGrid, 2);
        Grid.SetColumnSpan(ContentGrid, 1);

        LayoutGrid.Margin = new Thickness(0);
        CoverBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        CoverBorder.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        CoverBorder.Margin = new Thickness(0);
        CoverGlowBorder.HorizontalAlignment = CoverBorder.HorizontalAlignment;
        CoverGlowBorder.VerticalAlignment = CoverBorder.VerticalAlignment;
        CoverGlowBorder.Margin = CoverBorder.Margin;
        StackedTrackInfoPanel.Visibility = Visibility.Collapsed;
        StackedTrackInfoPanel.Margin = new Thickness(0);
        StackedTrackInfoPanel.RenderTransform = null;
        ContentGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        ContentGrid.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        ContentGrid.Margin = new Thickness(2, LyricsGridTopMargin, 4 + GetTimePillContentInset(), 0);
        LyricsViewport.Margin = new Thickness(0, 0, 0, -2);
        CoverBorder.RenderTransform = null;
        CoverGlowBorder.RenderTransform = null;
        ContentGrid.RenderTransform = null;
        ApplyCoverProgressLayout();
    }

    private double GetPreferredLayoutContentWidth()
    {
        if (!IsStackedCoverLayout)
        {
            return (_showCover ? _coverSize + _coverGap : 0) + LyricsColumnHorizontalMargin + PreferredContentWidth;
        }

        var (left, right) = GetStackedLayoutHorizontalSpan();
        return Math.Max(MinLyricsContentWidth, right - left);
    }

    private double GetStackedLayoutLeftInset()
    {
        var (left, _) = GetStackedLayoutHorizontalSpan();
        return Math.Max(0, -left);
    }

    private (double Left, double Right) GetStackedLayoutHorizontalSpan()
    {
        var minLeft = 0d;
        var maxRight = 0d;

        IncludeSpan(StackedSideMargin + _stackedCoverXOffset, _coverSize);
        if (IsStackedTrackInfoVisible)
        {
            IncludeSpan(
                StackedSideMargin + _coverSize + _stackedTrackInfoGap + _stackedCoverXOffset,
                MeasureStackedTrackInfoWidth() + StackedSideMargin);
        }

        IncludeSpan(
            StackedSideMargin + _stackedContentXOffset,
            PreferredContentWidth + StackedSideMargin);

        return (Math.Min(0, minLeft), maxRight);

        void IncludeSpan(double left, double width)
        {
            minLeft = Math.Min(minLeft, left);
            maxRight = Math.Max(maxRight, left + Math.Max(0, width));
        }
    }

    private void ApplyCoverClip()
    {
        if (!_showCover)
        {
            CoverBorder.Clip = null;
            return;
        }

        var width = CoverBorder.ActualWidth > 0 ? CoverBorder.ActualWidth : _coverSize;
        var height = CoverBorder.ActualHeight > 0 ? CoverBorder.ActualHeight : _coverSize;
        var radius = Math.Min(_coverCornerRadius, Math.Min(width, height) / 2);
        var clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        if (clip.CanFreeze)
        {
            clip.Freeze();
        }

        CoverBorder.Clip = clip;
    }

    private void ApplyCoverGlow()
    {
        if (!_showCoverGlow)
        {
            CoverGlowBorder.Opacity = 0;
            CoverGlowBorder.Background = Media.Brushes.Transparent;
            return;
        }

        var color = _coverAccentColor ?? _primaryBrush.Color;
        CoverGlowBorder.Background = CreateFrozenBrush(WithAlpha(color, 220));
        CoverGlowBorder.Opacity = _coverGlowOpacity;
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

            if (!_isSpectrumMode || _isTransitioning || _queuedFrame is not null)
            {
                CancelActiveTransition();
            }

            _trackSwitchSearchStartedAt = DateTime.MinValue;
            SetCurrentLine(safeCurrent, p);
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

    public void SetSongProgress(TimeSpan position, TimeSpan duration, bool isPlaying)
    {
        _isSongProgressPlaying = isPlaying;
        if (duration <= TimeSpan.Zero || duration.TotalMilliseconds < 1000)
        {
            _hasSongProgress = false;
            _songProgress = 0;
            _songProgressPosition = TimeSpan.Zero;
            _songProgressDuration = TimeSpan.Zero;
            UpdateSongProgressVisuals();
            return;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }
        else if (position > duration)
        {
            position = duration;
        }

        _hasSongProgress = true;
        _songProgressPosition = position;
        _songProgressDuration = duration;
        _songProgress = Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        UpdateSongProgressVisuals();
    }

    public void SetTrackInfo(string? title, string? artist)
    {
        var safeTitle = NormalizeTrackInfoText(title, "Unknown Title");
        var safeArtist = NormalizeTrackInfoText(artist, "Unknown Artist");
        if (safeTitle == _trackTitle && safeArtist == _trackArtist)
        {
            return;
        }

        _trackTitle = safeTitle;
        _trackArtist = safeArtist;
        TrackTitleText.Text = string.IsNullOrWhiteSpace(_trackTitle) ? " " : _trackTitle;
        TrackArtistText.Text = string.IsNullOrWhiteSpace(_trackArtist) ? " " : _trackArtist;
        ApplyCoverLayout();
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
        ApplySpectrumBarMetrics();
        if (_isSpectrumMode)
        {
            StartSpectrumRenderer();
        }
    }

    private void ApplySpectrumStyle(SpectrumDisplayStyle style)
    {
        _spectrumStyle = Enum.IsDefined(style) ? style : SpectrumDisplayStyle.Center;
        ApplySpectrumBarMetrics();
    }

    private System.Windows.Point GetSpectrumTransformOrigin() => _spectrumStyle switch
    {
        SpectrumDisplayStyle.Bottom => new System.Windows.Point(0.5, 1),
        SpectrumDisplayStyle.Dots => new System.Windows.Point(0.5, 0.5),
        _ => new System.Windows.Point(0.5, 0.5)
    };

    private double GetSpectrumMaxHeight() => _spectrumStyle switch
    {
        SpectrumDisplayStyle.Dots => 5,
        SpectrumDisplayStyle.Pulse => Math.Max(5, _spectrumTuning.MinBarHeight + (_spectrumTuning.BarHeightRange * 0.42)),
        _ => Math.Max(1, _spectrumTuning.MinBarHeight + _spectrumTuning.BarHeightRange)
    };

    private double GetSpectrumBarWidth() => _spectrumStyle switch
    {
        SpectrumDisplayStyle.Thin => 1.5,
        SpectrumDisplayStyle.Dots => 4,
        SpectrumDisplayStyle.Pulse => 5,
        _ => 3
    };

    private Thickness GetSpectrumBarMargin(int index) => _spectrumStyle switch
    {
        SpectrumDisplayStyle.Thin => new Thickness(index == 0 ? 0 : 2, 0, 2, 0),
        SpectrumDisplayStyle.Dots => new Thickness(index == 0 ? 0 : 2, 0, 2, 0),
        SpectrumDisplayStyle.Pulse => new Thickness(index == 0 ? 0 : 1, 0, 1, 0),
        _ => new Thickness(index == 0 ? 0 : 1.5, 0, 1.5, 0)
    };

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
            var bitmap = DecodeCoverBitmap(imageBytes);
            if (bitmap is null)
            {
                ShowCoverFallback();
                return false;
            }

            CrossfadeCover(bitmap, generation);
            return true;
        }
        catch
        {
            ShowCoverFallback();
            return false;
        }
    }

    private static BitmapSource? DecodeCoverBitmap(byte[] imageBytes)
    {
        return TryDecodeWpfBitmap(imageBytes) ??
            TryDecodeWithWindowsBitmapDecoder(imageBytes) ??
            TryDecodeWithImageSharp(imageBytes);
    }

    private static BitmapSource? TryDecodeWpfBitmap(byte[] imageBytes)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes);
            return DecodeWpfBitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TryDecodeWithWindowsBitmapDecoder(byte[] imageBytes)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(imageBytes);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            }

            stream.Seek(0);
            var decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
            var pixelData = decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            var pixels = pixelData.DetachPixelData();
            var width = checked((int)decoder.PixelWidth);
            var height = checked((int)decoder.PixelHeight);
            var bitmap = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32,
                null,
                pixels,
                width * 4);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TryDecodeWithImageSharp(byte[] imageBytes)
    {
        try
        {
            using var image = ImageSharpImage.Load(imageBytes);
            using var pngStream = new MemoryStream();
            image.Save(pngStream, new PngEncoder());
            pngStream.Position = 0;
            return DecodeWpfBitmap(pngStream);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource DecodeWpfBitmap(Stream stream)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
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

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(GetCoverFadeDurationMs()))
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

        var fadeOut = new DoubleAnimation(outgoing.Opacity, 0, TimeSpan.FromMilliseconds(GetCoverFadeDurationMs()))
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
                    SetCurrentLine(safeCurrent, progress);
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
                    SetCurrentLine(safeCurrent, progress);
                }
                else
                {
                    ApplyDisplayLine(CurrentLineText, safeCurrent, true, progress);
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
            ApplyDisplayLine(CurrentLineText, safeCurrent, true, progress);
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
        if (_transitionStyle == LyricTransitionStyle.None)
        {
            StopTransitionAnimations();
            SetNoAnimState();
            SetCurrentLine(newCurrent, progress);
            SetSecondaryLine(newNext);
            IncomingLineText.Text = " ";
            TrackTransform.Y = 0;
            CurrentLineText.Opacity = 0.98;
            NextLineText.Opacity = _secondaryOpacity;
            _lastLineProgress = Math.Clamp(progress, 0, 1);
            if (currentLineIndex >= 0)
            {
                _lastCurrentLineIndex = currentLineIndex;
            }

            ClearNoAnimState();
            UpdateSecondaryOpacity(progress);
            return;
        }

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
        SetNoAnimState();
        NextLineText.Text = promoted;
        SetLineRowHeight(NextLineText, _rowHeightPx);
        NextLineText.Foreground = _primaryBrush;
        NextLineText.FontSize = _currentFontSize;
        IncomingLineText.Text = upcoming;
        SetLineRowHeight(IncomingLineText, _nextRowHeightPx);
        IncomingLineText.FontSize = _nextFontSize;
        IncomingLineText.Opacity = _secondaryOpacity;
        CurrentLineText.Opacity = 0.98;
        NextLineText.Opacity = _transitionBaseNextOpacity;
        ClearNoAnimState();
        UpdatePreferredWidth();

        _transitionStartTimestamp = Stopwatch.GetTimestamp();
        StartTransitionRendering(generation);

        var durationMs = GetTransitionDurationMs();
        _transitionFallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs + 120) };
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

            var elapsed = Stopwatch.GetElapsedTime(_transitionStartTimestamp).TotalMilliseconds;
            var t = Math.Clamp(elapsed / GetTransitionDurationMs(), 0, 1);
            var fadeOutE = GetFadeOutEase(t);
            var fadeInE = GetFadeInEase(t);
            var slideE = _slideEase.Ease(t);
            var slideDistance = _transitionStyle switch
            {
                LyricTransitionStyle.Fade => 0,
                LyricTransitionStyle.CompactSlide => _linePitchPx * 0.48,
                _ => _linePitchPx
            };

            TrackTransform.Y = -slideDistance * slideE;
            CurrentLineText.Opacity = 0.98 + ((0.16 - 0.98) * fadeOutE);
            NextLineText.Opacity = _transitionBaseNextOpacity + ((0.98 - _transitionBaseNextOpacity) * fadeInE);
            IncomingLineText.Opacity = _secondaryOpacity;

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
        SetCurrentLine(_transitionPromoted, _transitionProgress);
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
        UpdateSongProgressVisuals();
    }

    private void AnimateLayerOpacity(UIElement layer, double target)
    {
        layer.BeginAnimation(UIElement.OpacityProperty, null);
        var animation = new DoubleAnimation(layer.Opacity, target, TimeSpan.FromMilliseconds(GetLayerFadeDurationMs()))
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
            SetSpectrumBarLevel(_spectrumBars[i], 0);
        }

        StopSpectrumRenderer();
    }

    private void StartSpectrumRenderer()
    {
        if (_spectrumRenderingHandler is not null)
        {
            return;
        }

        _spectrumRenderingHandler = (_, _) => OnSpectrumRenderFrame();
        CompositionTarget.Rendering += _spectrumRenderingHandler;
    }

    private void StopSpectrumRenderer()
    {
        if (_spectrumRenderingHandler is null)
        {
            return;
        }

        CompositionTarget.Rendering -= _spectrumRenderingHandler;
        _spectrumRenderingHandler = null;
    }

    private void OnSpectrumRenderFrame()
    {
        var isSettled = true;
        var averageLevel = _spectrumStyle == SpectrumDisplayStyle.Pulse
            ? _spectrumVisuals.Average()
            : 0;
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

            var level = _spectrumStyle == SpectrumDisplayStyle.Pulse
                ? averageLevel
                : _spectrumVisuals[i];
            SetSpectrumBarLevel(_spectrumBars[i], level);
            _spectrumBars[i].Opacity = _spectrumTuning.BarOpacity;
        }

        if (!_hasAudioDrivenSpectrum && isSettled)
        {
            StopSpectrumRenderer();
        }
    }

    private Media.Effects.Effect? CreateTextEffect()
    {
        return _textEffectStyle switch
        {
            TextEffectStyle.None => null,
            TextEffectStyle.Outline => new Media.Effects.DropShadowEffect
            {
                Color = Media.Colors.Black,
                Opacity = 0.72,
                BlurRadius = 0,
                ShadowDepth = 0,
                RenderingBias = Media.Effects.RenderingBias.Performance
            },
            TextEffectStyle.Glow => new Media.Effects.DropShadowEffect
            {
                Color = _primaryBrush.Color,
                Opacity = 0.42,
                BlurRadius = 8,
                ShadowDepth = 0,
                RenderingBias = Media.Effects.RenderingBias.Performance
            },
            _ => new Media.Effects.DropShadowEffect
            {
                Color = Media.Colors.Black,
                Opacity = 0.36,
                BlurRadius = 2,
                ShadowDepth = 1,
                Direction = 270,
                RenderingBias = Media.Effects.RenderingBias.Performance
            }
        };
    }

    private Media.Brush GetSpectrumBrush(int index)
    {
        var color = _spectrumColorMode switch
        {
            SpectrumColorMode.CoverAccent when _coverAccentColor is { } accent =>
                CreateReadableSpectrumColor(accent),
            SpectrumColorMode.Gradient =>
                CreateGradientSpectrumColor(index),
            _ => _primaryBrush.Color
        };

        return CreateFrozenBrush(color);
    }

    private Media.Color CreateGradientSpectrumColor(int index)
    {
        var amount = SpectrumBarCount <= 1
            ? 0
            : Math.Clamp(index / (double)(SpectrumBarCount - 1), 0, 1);
        var left = _coverAccentColor.HasValue
            ? CreateReadableSpectrumColor(_coverAccentColor.Value)
            : Media.Color.FromRgb(56, 189, 248);
        var right = MixColors(_primaryBrush.Color, Media.Color.FromRgb(251, 146, 60), 0.36);
        return MixColors(left, right, amount);
    }

    private static Media.Color CreateReadableSpectrumColor(Media.Color color)
    {
        var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;
        return luminance < 0.34
            ? MixColors(color, Media.Colors.White, 0.52)
            : MixColors(color, Media.Color.FromRgb(15, 23, 42), 0.12);
    }

    private void ApplyProgressVisualStyle()
    {
        var primary = _primaryBrush.Color;
        _progressFillBrush = CreateFrozenBrush(WithAlpha(primary, (byte)Math.Clamp(Math.Round(255 * _songProgressOpacity), 0, 255)));
        _progressTrackBrush = CreateFrozenBrush(WithAlpha(primary, (byte)Math.Clamp(Math.Round(54 * _songProgressOpacity), 8, 120)));

        BottomProgressTrack.Height = _songProgressThickness;
        BottomProgressTrack.Background = _progressTrackBrush;
        BottomProgressFill.Background = _progressFillBrush;
        LyricUnderlineTrack.Height = Math.Max(1, _songProgressThickness);
        LyricUnderlineTrack.Background = _progressTrackBrush;
        LyricUnderlineFill.Background = _progressFillBrush;
        SpectrumProgressTrack.Height = Math.Max(1, _songProgressThickness * 0.75);
        SpectrumProgressTrack.Background = _progressTrackBrush;
        SpectrumProgressFill.Background = _progressFillBrush;
        CoverBottomProgressTrack.Height = Math.Max(2, _songProgressThickness + 1);
        CoverBottomProgressFill.Background = _progressFillBrush;
        CoverProgressRingTrack.StrokeThickness = Math.Max(1, _songProgressThickness);
        CoverProgressRingValue.StrokeThickness = Math.Max(1, _songProgressThickness + 0.2);
        CoverProgressRingTrack.Stroke = _progressTrackBrush;
        CoverProgressRingValue.Stroke = _progressFillBrush;
        TimePillText.Foreground = _primaryBrush;
        TimePillBorder.BorderBrush = _progressTrackBrush;

        foreach (var dot in _progressDots)
        {
            dot.Background = _progressFillBrush;
        }
    }

    private void UpdateSongProgressVisuals()
    {
        var activeStyle = ResolveActiveSongProgressStyle();
        UpdateSongProgressVisibility(activeStyle);
        if (activeStyle == SongProgressDisplayStyle.Off)
        {
            return;
        }

        var progress = Math.Clamp(_songProgress, 0, 1);
        switch (activeStyle)
        {
            case SongProgressDisplayStyle.BottomLine:
                SetProgressFill(BottomProgressTrack, BottomProgressFill, BottomProgressScale, progress);
                break;
            case SongProgressDisplayStyle.LyricUnderline:
                LyricUnderlineOffset.Y = Math.Max(2, _rowHeightPx - 2);
                SetProgressFill(LyricUnderlineTrack, LyricUnderlineFill, LyricUnderlineScale, progress);
                break;
            case SongProgressDisplayStyle.CoverRing:
                UpdateCoverProgressGeometry();
                break;
            case SongProgressDisplayStyle.CoverBottomBar:
                SetProgressFill(CoverBottomProgressTrack, CoverBottomProgressFill, CoverBottomProgressScale, progress);
                break;
            case SongProgressDisplayStyle.SpectrumBaseline:
                SetProgressFill(SpectrumProgressTrack, SpectrumProgressFill, SpectrumProgressScale, progress);
                break;
            case SongProgressDisplayStyle.TimePill:
                TimePillText.Text = $"{FormatProgressTime(_songProgressPosition)} / {FormatProgressTime(_songProgressDuration)}";
                break;
            case SongProgressDisplayStyle.Dots:
                UpdateProgressDots(progress);
                break;
        }
    }

    private void UpdateSongProgressVisibility(SongProgressDisplayStyle activeStyle)
    {
        SetVisibility(BottomProgressTrack, activeStyle == SongProgressDisplayStyle.BottomLine);
        SetVisibility(LyricUnderlineTrack, activeStyle == SongProgressDisplayStyle.LyricUnderline);
        SetVisibility(CoverProgressRingTrack, activeStyle == SongProgressDisplayStyle.CoverRing);
        SetVisibility(CoverProgressRingValue, activeStyle == SongProgressDisplayStyle.CoverRing);
        SetVisibility(CoverBottomProgressTrack, activeStyle == SongProgressDisplayStyle.CoverBottomBar);
        SetVisibility(SpectrumProgressTrack, activeStyle == SongProgressDisplayStyle.SpectrumBaseline);
        SetVisibility(TimePillBorder, activeStyle == SongProgressDisplayStyle.TimePill);
        SetVisibility(ProgressDotsPanel, activeStyle == SongProgressDisplayStyle.Dots);
    }

    private static void SetVisibility(UIElement element, bool visible)
    {
        var next = visible ? Visibility.Visible : Visibility.Collapsed;
        if (element.Visibility != next)
        {
            element.Visibility = next;
        }
    }

    private SongProgressDisplayStyle ResolveActiveSongProgressStyle()
    {
        if (!_hasSongProgress || _songProgressStyle == SongProgressDisplayStyle.Off)
        {
            return SongProgressDisplayStyle.Off;
        }

        if (_songProgressStyle is SongProgressDisplayStyle.CoverRing or SongProgressDisplayStyle.CoverBottomBar)
        {
            return _showCover ? _songProgressStyle : SongProgressDisplayStyle.BottomLine;
        }

        if (_songProgressStyle == SongProgressDisplayStyle.SpectrumBaseline)
        {
            return _isSpectrumMode ? SongProgressDisplayStyle.SpectrumBaseline : SongProgressDisplayStyle.BottomLine;
        }

        return _songProgressStyle;
    }

    private static void SetProgressFill(Border track, Border fill, ScaleTransform scale, double progress)
    {
        var width = track.ActualWidth > 0 ? track.ActualWidth : track.RenderSize.Width;
        if (width > 0)
        {
            fill.Width = width;
        }

        scale.ScaleX = Math.Clamp(progress, 0, 1);
    }

    private void UpdateProgressDots(double progress)
    {
        var litPosition = Math.Clamp(progress, 0, 1) * (ProgressDotCount - 1);
        var pulse = _isSongProgressPlaying
            ? 1 + (Math.Sin(Environment.TickCount64 / 260.0) * 0.10)
            : 1;

        for (var i = 0; i < _progressDots.Length; i++)
        {
            var dot = _progressDots[i];
            var distance = Math.Abs(i - litPosition);
            var played = i <= litPosition;
            dot.Opacity = distance < 0.72
                ? 0.92
                : played ? 0.68 : 0.20;

            if (dot.RenderTransform is ScaleTransform scale)
            {
                var baseScale = distance < 0.72 ? 1.18 * pulse : 1;
                scale.ScaleX = baseScale;
                scale.ScaleY = baseScale;
            }
        }
    }

    private void ApplyCoverProgressLayout()
    {
        var columnSpan = IsStackedCoverLayout ? 3 : 1;
        foreach (var path in new[] { CoverProgressRingTrack, CoverProgressRingValue })
        {
            Grid.SetRow(path, 0);
            Grid.SetColumn(path, 0);
            Grid.SetColumnSpan(path, columnSpan);
            path.Width = _coverSize + 4;
            path.Height = _coverSize + 4;
            path.HorizontalAlignment = CoverBorder.HorizontalAlignment;
            path.VerticalAlignment = CoverBorder.VerticalAlignment;
            path.Margin = CoverBorder.Margin;
            path.RenderTransform = IsStackedCoverLayout
                ? new TranslateTransform(_stackedCoverXOffset - 2, _stackedCoverYOffset)
                : null;
        }

        UpdateCoverProgressGeometry();
    }

    private void UpdateCoverProgressGeometry()
    {
        var trackGeometry = CreateCoverProgressGeometry(1);
        var valueGeometry = CreateCoverProgressGeometry(_songProgress);
        CoverProgressRingTrack.Data = trackGeometry;
        CoverProgressRingValue.Data = valueGeometry;
    }

    private Media.Geometry CreateCoverProgressGeometry(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (progress <= 0)
        {
            return Media.Geometry.Empty;
        }

        var size = Math.Max(8, _coverSize + 4);
        return _coverDisplayStyle == CoverDisplayStyle.Circle
            ? CreateCircularProgressGeometry(size, progress)
            : CreateRectangularProgressGeometry(size, progress);
    }

    private static Media.Geometry CreateCircularProgressGeometry(double size, double progress)
    {
        var radius = Math.Max(1, (size / 2) - 1.4);
        var center = new System.Windows.Point(size / 2, size / 2);
        if (progress >= 0.999)
        {
            var full = new EllipseGeometry(center, radius, radius);
            if (full.CanFreeze)
            {
                full.Freeze();
            }

            return full;
        }

        var start = -90d;
        var end = start + (360d * progress);
        var startPoint = PointOnCircle(center, radius, start);
        var endPoint = PointOnCircle(center, radius, end);
        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment(
            endPoint,
            new System.Windows.Size(radius, radius),
            0,
            progress > 0.5,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry(new[] { figure });
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private static Media.Geometry CreateRectangularProgressGeometry(double size, double progress)
    {
        var inset = 1.4;
        var left = inset;
        var top = inset;
        var right = size - inset;
        var bottom = size - inset;
        var points = new[]
        {
            new System.Windows.Point(size / 2, top),
            new System.Windows.Point(right, top),
            new System.Windows.Point(right, bottom),
            new System.Windows.Point(left, bottom),
            new System.Windows.Point(left, top),
            new System.Windows.Point(size / 2, top)
        };

        return CreatePartialPolylineGeometry(points, progress);
    }

    private static Media.Geometry CreatePartialPolylineGeometry(IReadOnlyList<System.Windows.Point> points, double progress)
    {
        if (points.Count < 2)
        {
            return Media.Geometry.Empty;
        }

        var lengths = new double[points.Count - 1];
        var total = 0d;
        for (var i = 0; i < points.Count - 1; i++)
        {
            lengths[i] = Distance(points[i], points[i + 1]);
            total += lengths[i];
        }

        var remaining = total * Math.Clamp(progress, 0, 1);
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false
        };

        for (var i = 0; i < lengths.Length && remaining > 0; i++)
        {
            var segmentLength = lengths[i];
            if (remaining >= segmentLength)
            {
                figure.Segments.Add(new LineSegment(points[i + 1], true));
                remaining -= segmentLength;
                continue;
            }

            var ratio = segmentLength <= 0 ? 0 : remaining / segmentLength;
            figure.Segments.Add(new LineSegment(Interpolate(points[i], points[i + 1], ratio), true));
            remaining = 0;
        }

        var geometry = new PathGeometry(new[] { figure });
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private double GetSongProgressWidthReserve() =>
        _songProgressStyle == SongProgressDisplayStyle.TimePill ? TimePillWidthReserve : 0;

    private double GetTimePillContentInset() =>
        _songProgressStyle == SongProgressDisplayStyle.TimePill ? TimePillWidthReserve : 0;

    private static string FormatProgressTime(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}";
        }

        return $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    private static Media.Color WithAlpha(Media.Color color, byte alpha) =>
        Media.Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Media.Color MixColors(Media.Color a, Media.Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var inverse = 1 - amount;
        return Media.Color.FromArgb(
            (byte)Math.Clamp(Math.Round((a.A * inverse) + (b.A * amount)), 0, 255),
            (byte)Math.Clamp(Math.Round((a.R * inverse) + (b.R * amount)), 0, 255),
            (byte)Math.Clamp(Math.Round((a.G * inverse) + (b.G * amount)), 0, 255),
            (byte)Math.Clamp(Math.Round((a.B * inverse) + (b.B * amount)), 0, 255));
    }

    private static System.Windows.Point PointOnCircle(System.Windows.Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        return new System.Windows.Point(
            center.X + (Math.Cos(radians) * radius),
            center.Y + (Math.Sin(radians) * radius));
    }

    private static System.Windows.Point Interpolate(System.Windows.Point start, System.Windows.Point end, double amount) =>
        new(
            start.X + ((end.X - start.X) * amount),
            start.Y + ((end.Y - start.Y) * amount));

    private static double Distance(System.Windows.Point a, System.Windows.Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private void ApplySpectrumBarMetrics()
    {
        var maxHeight = GetSpectrumMaxHeight();
        SpectrumPanel.Height = Math.Ceiling(maxHeight);
        var origin = GetSpectrumTransformOrigin();
        for (var i = 0; i < _spectrumBars.Length; i++)
        {
            var bar = _spectrumBars[i];
            bar.Width = GetSpectrumBarWidth();
            bar.Height = maxHeight;
            bar.Margin = GetSpectrumBarMargin(i);
            bar.CornerRadius = new CornerRadius(_spectrumStyle == SpectrumDisplayStyle.Thin ? 2 : 999);
            bar.RenderTransformOrigin = origin;
            bar.Background = GetSpectrumBrush(i);
            SetSpectrumBarLevel(bar, 0);
        }
    }

    private void SetSpectrumBarLevel(Border bar, double level)
    {
        var maxHeight = GetSpectrumMaxHeight();
        var clamped = Math.Clamp(level, 0, 1);
        var visualHeight = _spectrumStyle switch
        {
            SpectrumDisplayStyle.Dots => 3 + (clamped * 2),
            SpectrumDisplayStyle.Pulse => 2 + (clamped * Math.Max(2, maxHeight - 2)),
            _ => Math.Clamp(_spectrumTuning.MinBarHeight + (clamped * _spectrumTuning.BarHeightRange), 1, maxHeight)
        };
        if (bar.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleY = visualHeight / maxHeight;
            scale.ScaleX = _spectrumStyle == SpectrumDisplayStyle.Dots
                ? 0.85 + (clamped * 0.35)
                : 1;
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

        var layoutHeight = IsStackedCoverLayout
            ? ContentGrid.ActualHeight > 0
                ? ContentGrid.ActualHeight
                : Math.Max(0, ActualHeight - _coverSize - _stackedCoverLyricsGap)
            : ActualHeight;
        var availableHost = Math.Max(MinHostHeight, layoutHeight - DescenderBuffer - LyricsGridTopMargin);
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

        if (Math.Abs(contentWidth - _lastNotifiedPreferredContentWidth) >= 4)
        {
            _lastNotifiedPreferredContentWidth = contentWidth;
            PreferredContentWidth = contentWidth;
        }

        NotifyPreferredWindowWidthIfChanged();
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

    private double MeasureStackedTrackInfoWidth()
    {
        var lines = new HashSet<string>();
        AddLineIfMeaningful(lines, _trackTitle);
        AddLineIfMeaningful(lines, _trackArtist);
        if (lines.Count == 0)
        {
            return 0;
        }

        var dpi = IsLoaded ? VisualTreeHelper.GetDpi(this).PixelsPerDip : 1.0;
        var titleSize = GetTrackTitleFontSize();
        var artistSize = GetTrackArtistFontSize();
        var width = lines.Max(line => Math.Max(
            MeasureLineWidthAt(line, titleSize, dpi),
            MeasureLineWidthAt(line, artistSize, dpi)));
        return Math.Ceiling(width + 8);
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
        var cacheKey = new TextWidthCacheKey(text, _fontFamily.Source, fontSize, _fontWeight, pixelsPerDip);
        if (_textWidthCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

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
        var measured = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
        if (_textWidthCache.Count >= MaxTextWidthCacheEntries)
        {
            _textWidthCache.Clear();
        }

        _textWidthCache[cacheKey] = measured;
        return measured;
    }

    private void ApplyTextTrimming()
    {
        var trimming = _autoAdjustWindowWidth ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        CurrentLineText.TextTrimming = trimming;
        NextLineText.TextTrimming = trimming;
        IncomingLineText.TextTrimming = trimming;
        TrackTitleText.TextTrimming = trimming;
        TrackArtistText.TextTrimming = trimming;
    }

    private void NotifyPreferredWindowWidthIfChanged()
    {
        var preferredWidth = PreferredWindowWidth;
        if (Math.Abs(preferredWidth - _lastNotifiedPreferredWindowWidth) < 4)
        {
            return;
        }

        _lastNotifiedPreferredWindowWidth = preferredWidth;
        PreferredWidthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetCurrentLine(string line, double progress = -1)
    {
        var safe = ToDisplayLine(line, SearchingText);
        if (safe == _displayedCurrent)
        {
            ApplyDisplayLine(CurrentLineText, safe, true, progress >= 0 ? progress : _lastLineProgress);
            return;
        }

        ApplyDisplayLine(CurrentLineText, safe, true, progress >= 0 ? progress : _lastLineProgress);
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

        ApplyDisplayLine(NextLineText, safe, false, 0);
        _displayedNext = safe;
        UpdatePreferredWidth();
    }

    private void ApplyDisplayLine(TextBlock textBlock, string text, bool isPrimary, double progress)
    {
        textBlock.Inlines.Clear();
        var (main, translation) = SplitTranslation(text);
        var brush = isPrimary ? _primaryBrush : _secondaryBrush;
        if (isPrimary && _karaokeEffect != LyricKaraokeEffect.Off && !IsSearchingLine(main))
        {
            AddKaraokeRuns(textBlock, main, progress);
        }
        else
        {
            textBlock.Inlines.Add(new Run(main) { Foreground = brush });
        }

        if (!string.IsNullOrWhiteSpace(translation))
        {
            if (_translationLayout == LyricTranslationLayout.NewLine)
            {
                textBlock.Inlines.Add(new LineBreak());
            }
            else
            {
                textBlock.Inlines.Add(new Run(" "));
            }

            textBlock.Inlines.Add(new Run(translation)
            {
                Foreground = CreateFrozenBrush(WithAlpha(brush.Color, (byte)Math.Clamp(Math.Round(255 * _translationOpacity), 0, 255))),
                FontSize = Math.Max(8, textBlock.FontSize * _translationFontScale),
                FontWeight = FontWeights.Medium
            });
        }
    }

    private void AddKaraokeRuns(TextBlock textBlock, string text, double progress)
    {
        var safeProgress = Math.Clamp(progress, 0, 1);
        var active = text.Length == 0 ? -1 : Math.Clamp((int)Math.Floor(safeProgress * text.Length), 0, text.Length - 1);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i].ToString();
            var passed = i < active;
            var current = i == active;
            var color = _primaryBrush.Color;
            var fontWeight = _fontWeight;
            var alpha = 255;

            switch (_karaokeEffect)
            {
                case LyricKaraokeEffect.Sweep:
                    alpha = i <= active ? 255 : 96;
                    break;
                case LyricKaraokeEffect.FadePassed:
                    alpha = passed ? 118 : 255;
                    break;
                case LyricKaraokeEffect.DisappearPassed:
                    alpha = passed ? 34 : 255;
                    break;
                case LyricKaraokeEffect.PulseCurrent:
                    alpha = passed ? 150 : current ? 255 : 110;
                    fontWeight = current ? FontWeights.ExtraBold : _fontWeight;
                    break;
                case LyricKaraokeEffect.GhostTrail:
                    var distance = Math.Abs(i - active);
                    alpha = distance switch
                    {
                        0 => 255,
                        1 => 210,
                        2 => 160,
                        _ => passed ? 92 : 116
                    };
                    break;
                case LyricKaraokeEffect.ParticleFade:
                    if (passed)
                    {
                        var noise = Math.Abs(Math.Sin((i + 1) * 12.9898 + active * 0.78));
                        alpha = (int)Math.Clamp(36 + (noise * 96), 24, 150);
                    }
                    else
                    {
                        alpha = current ? 255 : 125;
                    }
                    break;
            }

            textBlock.Inlines.Add(new Run(ch)
            {
                Foreground = CreateFrozenBrush(WithAlpha(color, (byte)Math.Clamp(alpha, 0, 255))),
                FontWeight = fontWeight
            });
        }
    }

    private static (string Main, string? Translation) SplitTranslation(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            return (text, null);
        }

        var start = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
        if (start <= 0 || start >= trimmed.Length - 2)
        {
            return (text, null);
        }

        var translation = trimmed[(start + 2)..^1].Trim();
        if (translation.Length == 0)
        {
            return (text, null);
        }

        return (trimmed[..start].TrimEnd(), translation);
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

    private void ApplyTrackInfoTypography()
    {
        TrackTitleText.FontFamily = _fontFamily;
        TrackTitleText.FontWeight = _fontWeight;
        TrackTitleText.Foreground = _primaryBrush;
        TrackTitleText.FontSize = GetTrackTitleFontSize();

        TrackArtistText.FontFamily = _fontFamily;
        TrackArtistText.FontWeight = FontWeights.SemiBold;
        TrackArtistText.Foreground = _secondaryBrush;
        TrackArtistText.FontSize = GetTrackArtistFontSize();
    }

    private double GetTrackTitleFontSize() =>
        Math.Clamp(Math.Min(_requestedFontSize * 0.92, _coverSize * 0.36), 10, 16);

    private double GetTrackArtistFontSize() =>
        Math.Clamp(Math.Min(_requestedFontSize * 0.78, _coverSize * 0.28), 9, 13);

    private static string ToDisplayLine(string? line, string fallback)
    {
        var text = (line ?? string.Empty).Trim();
        return text.Length > 0 ? text : fallback;
    }

    private static string NormalizeTrackInfoText(string? value, string weakValue)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text.Equals(weakValue, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return text;
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

    private int GetTransitionDurationMs() => _animationIntensity switch
    {
        AnimationIntensity.Reduced => 260,
        AnimationIntensity.Smooth => 720,
        _ => TransitionDurationMs
    };

    private int GetLayerFadeDurationMs() => _animationIntensity switch
    {
        AnimationIntensity.Reduced => 160,
        AnimationIntensity.Smooth => 420,
        _ => 300
    };

    private int GetCoverFadeDurationMs() => _animationIntensity switch
    {
        AnimationIntensity.Reduced => 220,
        AnimationIntensity.Smooth => 620,
        _ => 440
    };

    private static System.Windows.FontWeight ParseFontWeight(string? weight) => (weight ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "light" => FontWeights.Light,
        "normal" => FontWeights.Normal,
        "medium" => FontWeights.Medium,
        "semibold" => FontWeights.SemiBold,
        "bold" => FontWeights.Bold,
        _ => FontWeights.Medium
    };

    private readonly record struct TextWidthCacheKey(
        string Text,
        string FontFamily,
        double FontSize,
        FontWeight Weight,
        double PixelsPerDip);

    private sealed record LyricsFrame(string Current, string Next, double Progress, int LineIndex);
}
