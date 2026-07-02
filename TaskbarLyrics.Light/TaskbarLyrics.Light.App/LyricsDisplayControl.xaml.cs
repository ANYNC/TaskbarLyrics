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
using WpfImage = System.Windows.Controls.Image;

namespace TaskbarLyrics.Light.App;

public partial class LyricsDisplayControl : System.Windows.Controls.UserControl
{
    private const string SearchingText = "正在检索歌词...";
    private const int SpectrumBarCount = 24;
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
    private const double SingleBlockTransitionClipBuffer = 8;
    private const double WindowHorizontalMargin = 16;
    private const double SurfaceHorizontalPadding = 8;
    private const double DefaultCoverGap = 8;
    private const double LyricsColumnHorizontalMargin = 6;
    private const double LyricsTextPadding = 24;
    private const double StackedSideMargin = 4;
    private const double MinLyricsContentWidth = 160;
    private const double PrimaryLineYOffset = 1;
    private const double SecondaryLineYOffset = 1;
    private const double IncomingLineFadeStart = 0.08;
    private const double IncomingLineFadeDuration = 0.48;
    private const double TransitionSettlePixelThreshold = 0.75;
    private const int ProgressDotCount = 28;
    private const double TimePillWidthReserve = 92;
    private const int MaxTextWidthCacheEntries = 512;

    private readonly Border[] _spectrumBars = new Border[SpectrumBarCount];
    private readonly double[] _spectrumTargets = new double[SpectrumBarCount];
    private readonly double[] _spectrumVisuals = new double[SpectrumBarCount];
    private readonly Border[] _progressDots = new Border[ProgressDotCount];
    private readonly SolidColorBrush _primaryBrush = CreateMutableBrush(Media.Colors.White);
    private readonly SolidColorBrush _secondaryBrush = CreateMutableBrush(Media.Color.FromArgb(190, 255, 255, 255));
    private readonly SolidColorBrush _primaryTranslationBrush = CreateMutableBrush(Media.Colors.White);
    private readonly SolidColorBrush _secondaryTranslationBrush = CreateMutableBrush(Media.Color.FromArgb(190, 255, 255, 255));
    private readonly SolidColorBrush _progressFillBrush = CreateMutableBrush(Media.Colors.White);
    private readonly SolidColorBrush _progressTrackBrush = CreateMutableBrush(Media.Color.FromArgb(36, 255, 255, 255));
    private readonly SolidColorBrush _backgroundProgressBrush = CreateMutableBrush(Media.Color.FromArgb(46, 255, 255, 255));
    private readonly SolidColorBrush _surfaceBrush = CreateMutableBrush(Media.Colors.Transparent);
    private readonly SolidColorBrush _surfaceBorderBrush = CreateMutableBrush(Media.Color.FromArgb(41, 255, 255, 255));
    private readonly SolidColorBrush _coverGlowBrush = CreateMutableBrush(Media.Colors.Transparent);
    private readonly Dictionary<RowHeightCacheKey, double> _rowHeightCache = new();
    private readonly Dictionary<TextWidthCacheKey, double> _textWidthCache = new();
    private readonly Dictionary<UIElement, bool> _progressVisibilityTargets = new();

    private EventHandler? _spectrumRenderingHandler;
    private EventHandler? _progressRenderingHandler;

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
    private double _currentRowBoxHeight = 14;
    private double _nextRowBoxHeight = 14;
    private double _rowGapPx = 1;
    private double _linePitchPx = 15;
    private double _lyricsTrackTopInset;
    private double _currentFontSize = 13;
    private double _nextFontSize = 12;

    private bool _isTransitioning;
    private bool _transitionFinalized;
    private bool _isSpectrumMode;
    private bool _hasAudioDrivenSpectrum;
    private SpectrumDisplayStyle _spectrumStyle = SpectrumDisplayStyle.Center;
    private LyricTransitionStyle _transitionStyle = LyricTransitionStyle.Slide;
    private SongProgressDisplayStyle _songProgressStyle = SongProgressDisplayStyle.Off;
    private SongProgressColorMode _songProgressColorMode = SongProgressColorMode.Text;
    private string _songProgressColor = "#FFFFFFFF";
    private SongProgressAnchor _songProgressAnchor = SongProgressAnchor.Left;
    private bool _hasSongProgressVisual;
    private double _songProgressVisual;
    private long _progressLastTimestamp;
    private LyricTranslationLayout _translationLayout = LyricTranslationLayout.Inline;
    private TextEffectStyle _textEffectStyle = TextEffectStyle.Shadow;
    private SpectrumColorMode _spectrumColorMode = SpectrumColorMode.Text;
    private AnimationIntensity _animationIntensity = AnimationIntensity.Smooth;
    private CoverTransitionStyle _coverTransitionStyle = CoverTransitionStyle.SlideLeft;
    private bool _showLyricTranslation;
    private bool _useSingleLyricBlock;
    private double _translationFontScale = 0.86;
    private double _translationOpacity = 1;
    private double _songProgressThickness = 2;
    private double _songProgressOpacity = 0.9;
    private bool _useFixedSongProgressWidth;
    private double _songProgressWidth = 180;
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
    private double _transitionTargetSecondaryOpacity = 0.72;
    private int _transitionLineIndex = -1;
    private bool _transitionUsesTranslationPair;

    private bool _useCoverImageA = true;
    private int _coverGeneration;
    private int _trackInfoGeneration;
    private bool _autoAdjustWindowWidth = true;
    private bool _metricsUpdatePending;
    private double _lastNotifiedPreferredHostHeight;
    private double _lastNotifiedPreferredContentWidth;
    private double _lastNotifiedPreferredWindowWidth;

    private SpectrumTuningSettings _spectrumTuning = SpectrumTuningSettings.CreateDefault();
    private Media.FontFamily _fontFamily = new(AppSettings.DefaultFontFamily);
    private System.Windows.FontWeight _fontWeight = FontWeights.Bold;
    private Media.Effects.Effect? _textShadowEffect;
    private Media.Color _resolvedPrimaryColor = Media.Colors.White;
    private Media.Color _resolvedSecondaryColor = Media.Color.FromArgb(190, 255, 255, 255);
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
    private SongProgressDisplayStyle _lastActiveSongProgressStyle = SongProgressDisplayStyle.Off;
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

    private double GetLyricsGridTopInset() => IsStackedCoverLayout ? LyricsGridTopMargin : 0;

    public double PreferredWindowHeight =>
        IsStackedCoverLayout
            ? GetCoverSlotSize() + _stackedCoverLyricsGap + PreferredHostHeight + LyricsGridTopMargin + WindowVerticalMargin +
                (StackedTransitionClipBuffer * 2)
            : PreferredWindowBottomAnchorHeight;

    public double PreferredWindowBottomAnchorHeight =>
        Math.Max(
            (_showCover ? GetCoverSlotSize() : 0) + WindowVerticalMargin,
            PreferredHostHeight + GetLyricsGridTopInset() + WindowVerticalMargin + (GetSingleBlockTransitionClipBuffer() * 2));

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
        CoverImageA.RenderTransform = new TranslateTransform();
        CoverImageB.RenderTransform = new TranslateTransform();
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
        StopProgressRenderer();
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
        var wasSingleLyricBlock = UsesSingleLyricBlock;
        var previousRequestedFontSize = _requestedFontSize;
        var previousAutoAdjustLineGap = _autoAdjustLineGap;
        var previousManualLineGap = _manualLineGap;
        var previousLineGapOffset = _lineGapOffset;
        var previousFontFamily = _fontFamily.Source;
        var previousFontWeight = _fontWeight;
        var previousTransitionStyle = _transitionStyle;
        var previousTranslationLayout = _translationLayout;
        var previousShowLyricTranslation = _showLyricTranslation;
        var previousTranslationFontScale = _translationFontScale;
        _animationIntensity = Enum.IsDefined(settings.AnimationIntensity)
            ? settings.AnimationIntensity
            : AnimationIntensity.Smooth;
        _resolvedPrimaryColor = primary;
        _resolvedSecondaryColor = secondary;
        _coverAccentColor = coverAccentColor;
        _requestedFontSize = Math.Clamp(settings.FontSize, 10, 40);
        _autoAdjustLineGap = settings.AutoAdjustLineGap;
        _manualLineGap = Math.Clamp(settings.LineGap, 0, 24);
        _lineGapOffset = Math.Clamp(settings.LineGapOffset, -12, 24);
        _fontFamily = new Media.FontFamily(BundledFontRegistrar.ResolveFontFamily(settings.FontFamily));
        _fontWeight = ParseFontWeight(settings.FontWeight);
        _transitionStyle = Enum.IsDefined(settings.TransitionStyle)
            ? AppSettings.NormalizeTransitionStyle(settings.TransitionStyle)
            : LyricTransitionStyle.Slide;
        _translationLayout = Enum.IsDefined(settings.TranslationLayout)
            ? settings.TranslationLayout
            : LyricTranslationLayout.Inline;
        _showLyricTranslation = settings.ShowLyricTranslation;
        _translationFontScale = Math.Clamp(settings.TranslationFontScale, 0.62, 1);
        _translationOpacity = Math.Clamp(settings.TranslationOpacity, 0.18, 1);
        ApplyTextBrushColors(primary, secondary);
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
        _songProgressThickness = Math.Clamp(settings.SongProgressThickness, 1, 8);
        _songProgressOpacity = Math.Clamp(settings.SongProgressOpacity, 0.15, 1);
        _songProgressColorMode = Enum.IsDefined(settings.SongProgressColorMode)
            ? settings.SongProgressColorMode
            : SongProgressColorMode.Text;
        _songProgressColor = string.IsNullOrWhiteSpace(settings.SongProgressColor)
            ? "#FFFFFFFF"
            : settings.SongProgressColor;
        _useFixedSongProgressWidth = settings.UseFixedSongProgressWidth;
        _songProgressWidth = Math.Clamp(settings.SongProgressWidth, 40, 900);
        _songProgressAnchor = Enum.IsDefined(settings.SongProgressAnchor)
            ? settings.SongProgressAnchor
            : SongProgressAnchor.Left;
        _rowHeightCache.Clear();
        _textWidthCache.Clear();
        _autoAdjustWindowWidth = settings.AutoAdjustWindowWidth;
        _songProgressStyle = AppSettings.NormalizeSongProgressStyle(settings.SongProgressStyle);
        _coverTransitionStyle = Enum.IsDefined(settings.CoverTransitionStyle)
            ? settings.CoverTransitionStyle
            : CoverTransitionStyle.SlideLeft;
        ApplySpectrumStyle(settings.SpectrumStyle);
        ApplyProgressVisualStyle();
        ApplyCoverStyle(settings);
        ApplyTextTrimming();

        ApplySurfaceBrush(settings, surfaceColorSeed ?? primary);

        SurfaceBorder.BorderBrush = _surfaceBorderBrush;
        AnimateBrushColor(
            _surfaceBorderBrush,
            settings.ShowBorder ? Media.Color.FromArgb(41, 255, 255, 255) : Media.Colors.Transparent);
        SurfaceBorder.BorderThickness = settings.ShowBorder ? new Thickness(1) : new Thickness(0);

        _textShadowEffect = CreateTextEffect();

        var shouldUseSingleLyricBlock = ShouldUseSingleLyricBlockForLine(GetActivePrimaryLineForBlockMode());
        var singleLyricBlockChanged = wasSingleLyricBlock != shouldUseSingleLyricBlock;
        _useSingleLyricBlock = shouldUseSingleLyricBlock;
        var transitionLayoutChanged =
            _isTransitioning &&
            (Math.Abs(previousRequestedFontSize - _requestedFontSize) > 0.1 ||
             previousAutoAdjustLineGap != _autoAdjustLineGap ||
             Math.Abs(previousManualLineGap - _manualLineGap) > 0.1 ||
             Math.Abs(previousLineGapOffset - _lineGapOffset) > 0.1 ||
             !string.Equals(previousFontFamily, _fontFamily.Source, StringComparison.Ordinal) ||
             previousFontWeight != _fontWeight ||
             previousTransitionStyle != _transitionStyle ||
             previousTranslationLayout != _translationLayout ||
             previousShowLyricTranslation != _showLyricTranslation ||
             Math.Abs(previousTranslationFontScale - _translationFontScale) > 0.01);
        if (singleLyricBlockChanged || transitionLayoutChanged)
        {
            CancelActiveTransition();
        }

        if (singleLyricBlockChanged)
        {
            _transitionUpcoming = " ";
            _displayedNext = " ";
            ApplyDisplayLine(NextLineText, " ", false, 0);
            ApplyDisplayLine(OutgoingLineText, " ", false, 0);
            ApplyDisplayLine(OutgoingTranslationText, " ", false, 0);
            ApplyDisplayLine(IncomingLineText, " ", false, 0);
            ApplyDisplayLine(IncomingTranslationText, " ", false, 0);
            NextLineText.Opacity = UsesSingleLyricBlock ? 1 : _secondaryOpacity;
            OutgoingLineText.Opacity = 0;
            OutgoingTranslationText.Opacity = 0;
            IncomingLineText.Opacity = 0;
            IncomingTranslationText.Opacity = 0;
        }

        CurrentLineText.Effect = _textShadowEffect;
        NextLineText.Effect = _textShadowEffect;
        OutgoingLineText.Effect = _textShadowEffect;
        OutgoingTranslationText.Effect = _textShadowEffect;
        IncomingLineText.Effect = _textShadowEffect;
        IncomingTranslationText.Effect = _textShadowEffect;
        TrackTitleText.Effect = _textShadowEffect;
        TrackArtistText.Effect = _textShadowEffect;

        ApplyLineTypography(CurrentLineText, true);
        ApplyLineTypography(NextLineText, false);
        ApplyLineTypography(OutgoingLineText, true);
        ApplyLineTypography(OutgoingTranslationText, false);
        ApplyLineTypography(IncomingLineText, false);
        ApplyLineTypography(IncomingTranslationText, false);
        ApplyTrackInfoTypography();
        ApplyCurrentDisplayLine(_displayedCurrent, _lastLineProgress);
        if (!UsesSingleLyricBlock)
        {
            ApplyDisplayLine(NextLineText, _displayedNext, false, 0);
        }
        if (_isTransitioning && !string.IsNullOrWhiteSpace(_transitionUpcoming))
        {
            if (_transitionUsesTranslationPair)
            {
                var incomingUsesTranslationPair = ShouldUseSingleLyricBlockForLine(_transitionPromoted);
                var incomingRows = GetTwoRowTransitionRows(
                    _transitionPromoted,
                    _transitionUpcoming,
                    incomingUsesTranslationPair);
                ApplyTwoRowTransitionPrimary(
                    IncomingLineText,
                    incomingRows.Primary,
                    incomingRows.SecondaryIsTranslation);
                ApplyTwoRowTransitionSecondary(
                    IncomingTranslationText,
                    incomingRows.Secondary,
                    incomingRows.SecondaryIsTranslation);
            }
            else
            {
                ApplyDisplayLine(IncomingLineText, _transitionUpcoming, false, 0);
            }
        }

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

    private void ApplyTextBrushColors(Media.Color primary, Media.Color secondary)
    {
        AnimateBrushColor(_primaryBrush, primary);
        AnimateBrushColor(_secondaryBrush, secondary);
        AnimateBrushColor(_primaryTranslationBrush, WithAlpha(primary, GetTranslationAlpha()));
        AnimateBrushColor(_secondaryTranslationBrush, WithAlpha(secondary, GetTranslationAlpha()));
    }

    private void ApplySurfaceBrush(AppSettings settings, Media.Color surfaceColorSeed)
    {
        SurfaceBorder.Background = _surfaceBrush;
        AnimateBrushColor(_surfaceBrush, ResolveSurfaceColor(settings, surfaceColorSeed));
    }

    private Media.Color ResolveSurfaceColor(AppSettings settings, Media.Color surfaceColorSeed)
    {
        if (!settings.ShowBackground)
        {
            return Media.Colors.Transparent;
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

        return color;
    }

    private void ApplyCoverStyle(AppSettings settings)
    {
        var previousStyle = _coverDisplayStyle;
        var previousLayoutMode = _coverLayoutMode;
        var previousShowCover = _showCover;
        var previousSize = _coverSize;

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

        var coverSurfaceChanged =
            previousStyle != style ||
            previousLayoutMode != _coverLayoutMode ||
            previousShowCover != _showCover ||
            Math.Abs(previousSize - _coverSize) > 0.5;
        if (coverSurfaceChanged)
        {
            SettleCoverAnimationState();
        }

        CoverBorder.Visibility = _showCover ? Visibility.Visible : Visibility.Collapsed;
        CoverBorder.Width = _coverSize;
        CoverBorder.Height = _coverSize;
        CoverGlowBorder.Visibility = _showCover ? Visibility.Visible : Visibility.Collapsed;
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
            CoverRow.Height = new GridLength(GetCoverSlotSize());
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
            var transitionClipBuffer = GetLyricsTransitionClipBuffer();
            LyricsViewport.Margin = new Thickness(0, -transitionClipBuffer, 0, -transitionClipBuffer);
            LyricsViewport.Padding = new Thickness(0, transitionClipBuffer, 0, transitionClipBuffer);
            CoverBorder.RenderTransform = new TranslateTransform(_stackedCoverXOffset, _stackedCoverYOffset);
            CoverGlowBorder.RenderTransform = new TranslateTransform(_stackedCoverXOffset, _stackedCoverYOffset);
            StackedTrackInfoPanel.Visibility = IsStackedTrackInfoVisible ? Visibility.Visible : Visibility.Collapsed;
            StackedTrackInfoPanel.Margin = new Thickness(StackedSideMargin + GetCoverSlotSize() + _stackedTrackInfoGap, 0, StackedSideMargin, 0);
            StackedTrackInfoPanel.MaxWidth = MeasureStackedTrackInfoWidth();
            StackedTrackInfoPanel.RenderTransform = new TranslateTransform(_stackedCoverXOffset, _stackedCoverYOffset);
            ContentGrid.RenderTransform = new TranslateTransform(_stackedContentXOffset, _stackedContentYOffset);
            ApplyCoverProgressLayout();
            return;
        }

        CoverRow.Height = new GridLength(1, GridUnitType.Star);
        CoverStackGapRow.Height = new GridLength(0);
        ContentRow.Height = new GridLength(0);
        CoverColumn.Width = new GridLength(_showCover ? GetCoverSlotSize() : 0);
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
        ContentGrid.Margin = new Thickness(2, 0, 4 + GetTimePillContentInset(), 0);
        var singleBlockClipBuffer = GetLyricsTransitionClipBuffer();
        LyricsViewport.Margin = new Thickness(0, -singleBlockClipBuffer, 0, -singleBlockClipBuffer);
        LyricsViewport.Padding = new Thickness(0, singleBlockClipBuffer, 0, singleBlockClipBuffer);
        CoverBorder.RenderTransform = null;
        CoverGlowBorder.RenderTransform = null;
        ContentGrid.RenderTransform = null;
        ApplyCoverProgressLayout();
    }

    private double GetPreferredLayoutContentWidth()
    {
        if (!IsStackedCoverLayout)
        {
            return (_showCover ? GetCoverSlotSize() + _coverGap : 0) + LyricsColumnHorizontalMargin + PreferredContentWidth;
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

        var coverPadding = GetCoverProgressOuterPadding();
        IncludeSpan(StackedSideMargin + _stackedCoverXOffset - coverPadding, GetCoverSlotSize());
        if (IsStackedTrackInfoVisible)
        {
            IncludeSpan(
                StackedSideMargin + GetCoverSlotSize() + _stackedTrackInfoGap + _stackedCoverXOffset,
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
        CoverGlowBorder.Background = _coverGlowBrush;
        if (!_showCoverGlow)
        {
            AnimateElementOpacity(CoverGlowBorder, 0, LightMotion.ColorTransitionMs(_animationIntensity));
            AnimateBrushColor(_coverGlowBrush, Media.Colors.Transparent);
            return;
        }

        var color = _coverAccentColor ?? _resolvedPrimaryColor;
        AnimateBrushColor(_coverGlowBrush, WithAlpha(color, 220));
        AnimateElementOpacity(CoverGlowBorder, _coverGlowOpacity, LightMotion.ColorTransitionMs(_animationIntensity));
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
        ApplySingleLyricBlockModeForLine(safeCurrent);
        var displayNext = GetSecondaryDisplayLine(safeNext);

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
            ApplyDisplayLine(IncomingLineText, " ", false, 0);
            ApplyDisplayLine(IncomingTranslationText, " ", false, 0);
            IncomingTranslationText.Opacity = 0;
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
            ResetForTrackSwitch(safeCurrent, displayNext, p, currentLineIndex, normalizedTrackId);
            return;
        }

        if (normalizedTrackId.Length > 0)
        {
            _lastTrackId = normalizedTrackId;
        }

        ApplyFrame(safeCurrent, displayNext, p, currentLineIndex);
        UpdatePreferredWidth();
    }

    public void SetSongProgress(TimeSpan position, TimeSpan duration, bool isPlaying)
    {
        _isSongProgressPlaying = isPlaying;
        var hadSongProgress = _hasSongProgress;
        if (duration <= TimeSpan.Zero || duration.TotalMilliseconds < 1000)
        {
            _hasSongProgress = false;
            _songProgress = 0;
            _songProgressPosition = TimeSpan.Zero;
            _songProgressDuration = TimeSpan.Zero;
            UpdateSongProgressVisuals();
            StartProgressRenderer();
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
        if (!_hasSongProgressVisual || !hadSongProgress)
        {
            _songProgressVisual = _songProgress;
            _hasSongProgressVisual = true;
        }

        UpdateSongProgressVisuals();
        StartProgressRenderer();
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
        if (IsLoaded && IsStackedTrackInfoVisible && _animationIntensity != AnimationIntensity.Reduced)
        {
            AnimateTrackInfoChange(safeTitle, safeArtist);
            return;
        }

        ApplyTrackInfoText(safeTitle, safeArtist);
        ApplyCoverLayout();
        UpdatePreferredWidth();
    }

    private void AnimateTrackInfoChange(string title, string artist)
    {
        var generation = ++_trackInfoGeneration;
        var duration = Math.Max(120, LightMotion.LayerFadeMs(_animationIntensity) / 2);
        AnimateElementOpacity(TrackTitleText, 0, duration, () =>
        {
            if (generation != _trackInfoGeneration)
            {
                return;
            }

            ApplyTrackInfoText(title, artist);
            ApplyCoverLayout();
            UpdatePreferredWidth();
            AnimateElementOpacity(TrackTitleText, 1, duration);
        });
        AnimateElementOpacity(TrackArtistText, 0, duration, () =>
        {
            if (generation != _trackInfoGeneration)
            {
                return;
            }

            AnimateElementOpacity(TrackArtistText, 0.74, duration);
        });
    }

    private void ApplyTrackInfoText(string title, string artist)
    {
        TrackTitleText.Text = string.IsNullOrWhiteSpace(title) ? " " : title;
        TrackArtistText.Text = string.IsNullOrWhiteSpace(artist) ? " " : artist;
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
        UpdatePreferredWidth();
        if (_isSpectrumMode)
        {
            StartSpectrumRenderer();
        }
    }

    private void ApplySpectrumStyle(SpectrumDisplayStyle style)
    {
        _spectrumStyle = Enum.IsDefined(style) ? style : SpectrumDisplayStyle.Center;
        ApplySpectrumBarMetrics();
        UpdatePreferredWidth();
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

    private double MeasureSpectrumContentWidth()
    {
        var width = 0d;
        for (var i = 0; i < SpectrumBarCount; i++)
        {
            var margin = GetSpectrumBarMargin(i);
            width += GetSpectrumBarWidth() + margin.Left + margin.Right;
        }

        return Math.Max(MinLyricsContentWidth, Math.Ceiling(width + LyricsTextPadding));
    }

    public bool SetCover(byte[]? imageBytes, string fallbackText, Media.Color fallbackColor)
    {
        var generation = ++_coverGeneration;

        if (imageBytes is not { Length: > 0 })
        {
            ApplyCoverFallbackVisual(fallbackText, fallbackColor);
            ShowCoverFallback();
            return false;
        }

        try
        {
            var bitmap = DecodeCoverBitmap(imageBytes);
            if (bitmap is null)
            {
                ApplyCoverFallbackVisual(fallbackText, fallbackColor);
                ShowCoverFallback();
                return false;
            }

            CrossfadeCover(bitmap, generation);
            return true;
        }
        catch
        {
            ApplyCoverFallbackVisual(fallbackText, fallbackColor);
            ShowCoverFallback();
            return false;
        }
    }

    private void ApplyCoverFallbackVisual(string fallbackText, Media.Color fallbackColor)
    {
        CoverBorder.Background = CreateFrozenBrush(fallbackColor);
        CoverFallbackText.Text = string.IsNullOrWhiteSpace(fallbackText)
            ? "N"
            : fallbackText[..1].ToUpperInvariant();
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
        switch (_coverTransitionStyle)
        {
            case CoverTransitionStyle.None:
                ShowCoverImmediately(incoming, outgoing, bitmap, generation);
                return;
            case CoverTransitionStyle.Fade:
                FadeCover(incoming, outgoing, bitmap, generation);
                return;
            default:
                SlideCoverLeft(incoming, outgoing, bitmap, generation);
                return;
        }
    }

    private void FadeCover(WpfImage incoming, WpfImage outgoing, BitmapSource bitmap, int generation)
    {
        var outgoingOpacity = CoerceOpacity(outgoing.Opacity);
        incoming.BeginAnimation(OpacityProperty, null);
        outgoing.BeginAnimation(OpacityProperty, null);
        ResetCoverTranslate(incoming);
        ResetCoverTranslate(outgoing);
        incoming.Source = bitmap;
        incoming.Opacity = 0;
        outgoing.Opacity = outgoingOpacity;
        AnimateCoverOpacity(CoverFallbackText, 0, generation);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(GetCoverFadeDurationMs()))
        {
            EasingFunction = LightMotion.CreateFadeEase(),
            FillBehavior = FillBehavior.Stop
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

        var fadeOut = new DoubleAnimation(outgoingOpacity, 0, TimeSpan.FromMilliseconds(GetCoverFadeDurationMs()))
        {
            EasingFunction = LightMotion.CreateFadeEase(),
            FillBehavior = FillBehavior.Stop
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
            ResetCoverTranslate(outgoing);
            _useCoverImageA = !_useCoverImageA;
        };

        incoming.BeginAnimation(OpacityProperty, fadeIn);
        outgoing.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void SlideCoverLeft(WpfImage incoming, WpfImage outgoing, BitmapSource bitmap, int generation)
    {
        var distance = GetCoverSlideDistance();
        var incomingTranslate = EnsureCoverTranslate(incoming);
        var outgoingTranslate = EnsureCoverTranslate(outgoing);
        var outgoingOpacity = CoerceOpacity(outgoing.Opacity);
        var hasOutgoingVisual = outgoing.Source is not null && outgoingOpacity > 0.01;

        incoming.BeginAnimation(OpacityProperty, null);
        outgoing.BeginAnimation(OpacityProperty, null);
        incomingTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        outgoingTranslate.BeginAnimation(TranslateTransform.XProperty, null);

        incoming.Source = bitmap;
        incoming.Opacity = 1;
        outgoing.Opacity = outgoingOpacity;
        incomingTranslate.X = distance;
        outgoingTranslate.X = 0;
        if (hasOutgoingVisual)
        {
            AnimateCoverOpacity(CoverFallbackText, 0, generation);
        }

        AnimateCoverTranslateX(incoming, distance, 0, generation);
        AnimateCoverTranslateX(outgoing, outgoingTranslate.X, -distance, generation, () =>
        {
            outgoing.Source = null;
            outgoing.Opacity = 0;
            ResetCoverTranslate(outgoing);
            incoming.Opacity = 1;
            ResetCoverTranslate(incoming);
            CoverFallbackText.BeginAnimation(OpacityProperty, null);
            CoverFallbackText.Opacity = 0;
            _useCoverImageA = !_useCoverImageA;
        });
    }

    private void ShowCoverImmediately(WpfImage incoming, WpfImage outgoing, BitmapSource bitmap, int generation)
    {
        incoming.BeginAnimation(OpacityProperty, null);
        outgoing.BeginAnimation(OpacityProperty, null);
        CoverFallbackText.BeginAnimation(OpacityProperty, null);
        ResetCoverTranslate(incoming);
        ResetCoverTranslate(outgoing);

        incoming.Source = bitmap;
        incoming.Opacity = 1;
        outgoing.Source = null;
        outgoing.Opacity = 0;
        CoverFallbackText.Opacity = 0;
        if (generation == _coverGeneration)
        {
            _useCoverImageA = !_useCoverImageA;
        }
    }

    private void SettleCoverAnimationState()
    {
        var visible = ResolveVisibleCoverImage();
        foreach (var image in new[] { CoverImageA, CoverImageB })
        {
            image.BeginAnimation(OpacityProperty, null);
            ResetCoverTranslate(image);
        }

        CoverFallbackText.BeginAnimation(OpacityProperty, null);
        if (visible is null)
        {
            CoverImageA.Source = null;
            CoverImageB.Source = null;
            CoverImageA.Opacity = 0;
            CoverImageB.Opacity = 0;
            CoverFallbackText.Opacity = 1;
            _useCoverImageA = true;
            return;
        }

        var hidden = ReferenceEquals(visible, CoverImageA) ? CoverImageB : CoverImageA;
        visible.Opacity = 1;
        hidden.Source = null;
        hidden.Opacity = 0;
        CoverFallbackText.Opacity = 0;
        _useCoverImageA = ReferenceEquals(visible, CoverImageB);
    }

    private WpfImage? ResolveVisibleCoverImage()
    {
        var hasA = CoverImageA.Source is not null && CoverImageA.Opacity > 0.01;
        var hasB = CoverImageB.Source is not null && CoverImageB.Opacity > 0.01;
        if (hasA && hasB)
        {
            return CoverImageA.Opacity >= CoverImageB.Opacity ? CoverImageA : CoverImageB;
        }

        if (hasA)
        {
            return CoverImageA;
        }

        return hasB ? CoverImageB : null;
    }

    private void ShowCoverFallback()
    {
        var generation = _coverGeneration;
        if (_coverTransitionStyle == CoverTransitionStyle.None)
        {
            CoverImageA.BeginAnimation(OpacityProperty, null);
            CoverImageB.BeginAnimation(OpacityProperty, null);
            CoverFallbackText.BeginAnimation(OpacityProperty, null);
            CoverImageA.Source = null;
            CoverImageB.Source = null;
            CoverImageA.Opacity = 0;
            CoverImageB.Opacity = 0;
            CoverFallbackText.Opacity = 1;
            ResetCoverTranslate(CoverImageA);
            ResetCoverTranslate(CoverImageB);
            _useCoverImageA = true;
            return;
        }

        if (_coverTransitionStyle == CoverTransitionStyle.SlideLeft)
        {
            var distance = GetCoverSlideDistance();
            _useCoverImageA = true;
            SlideCoverImageOut(CoverImageA, distance, generation);
            SlideCoverImageOut(CoverImageB, distance, generation);
        }
        else
        {
            AnimateCoverOpacity(CoverImageA, 0, generation, () => CoverImageA.Source = null);
            AnimateCoverOpacity(CoverImageB, 0, generation, () => CoverImageB.Source = null);
        }

        AnimateCoverOpacity(CoverFallbackText, 1, generation);
    }

    private void SlideCoverImageOut(WpfImage image, double distance, int generation)
    {
        if (image.Source is null && image.Opacity <= 0.01)
        {
            image.Source = null;
            image.Opacity = 0;
            ResetCoverTranslate(image);
            return;
        }

        var translate = EnsureCoverTranslate(image);
        var from = translate.X;
        AnimateCoverOpacity(image, 0, generation);
        AnimateCoverTranslateX(image, from, -distance, generation, () =>
        {
            image.Source = null;
            image.Opacity = 0;
            ResetCoverTranslate(image);
            _useCoverImageA = true;
        });
    }

    private void AnimateCoverOpacity(UIElement element, double target, int generation, Action? completed = null)
    {
        target = CoerceOpacity(target);
        var from = CoerceOpacity(element.Opacity);
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = from;
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(GetCoverFadeDurationMs()))
        {
            EasingFunction = LightMotion.CreateFadeEase(),
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (generation != _coverGeneration)
            {
                return;
            }

            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = target;
            completed?.Invoke();
        };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void AnimateCoverTranslateX(
        UIElement element,
        double from,
        double target,
        int generation,
        Action? completed = null)
    {
        var transform = EnsureCoverTranslate(element);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = from;
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(GetCoverFadeDurationMs()))
        {
            EasingFunction = LightMotion.CreateMoveEase(),
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (generation != _coverGeneration)
            {
                return;
            }

            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = target;
            completed?.Invoke();
        };
        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private double GetCoverSlideDistance() =>
        Math.Max(1, Math.Max(_coverSize, CoverBorder.ActualWidth));

    private static TranslateTransform EnsureCoverTranslate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform translate)
        {
            return translate;
        }

        translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }

    private static void ResetCoverTranslate(UIElement element)
    {
        var translate = EnsureCoverTranslate(element);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.X = 0;
    }

    private void ApplyFrame(
        string safeCurrent,
        string safeNext,
        double progress,
        int currentLineIndex)
    {
        ApplySingleLyricBlockModeForLine(safeCurrent);

        if (_isTransitioning && UpdateTransitionFrame(safeCurrent, safeNext, progress, currentLineIndex))
        {
            return;
        }

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
                    ApplyCurrentDisplayLine(safeCurrent, progress);
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
            ApplyCurrentDisplayLine(safeCurrent, progress);
            UpdateSecondaryOpacity(progress);
        }

        _lastLineProgress = progress;
    }

    private bool UpdateTransitionFrame(
        string safeCurrent,
        string safeNext,
        double progress,
        int currentLineIndex)
    {
        if (!_isTransitioning)
        {
            return false;
        }

        var sameIndexedLine = currentLineIndex >= 0 && currentLineIndex == _transitionLineIndex;
        var sameUnindexedLine =
            currentLineIndex < 0 &&
            _transitionLineIndex < 0 &&
            string.Equals(safeCurrent, _transitionPromoted, StringComparison.Ordinal);

        if (!sameIndexedLine && !sameUnindexedLine)
        {
            return false;
        }

        var promotedChanged = !string.Equals(safeCurrent, _transitionPromoted, StringComparison.Ordinal);
        var upcomingChanged = !string.Equals(safeNext, _transitionUpcoming, StringComparison.Ordinal);
        _transitionPromoted = safeCurrent;
        _transitionUpcoming = safeNext;
        _transitionProgress = progress;
        _transitionTargetSecondaryOpacity = ResolveSecondaryOpacity(progress);
        _lastLineProgress = progress;

        if (_transitionUsesTranslationPair)
        {
            var incomingUsesTranslationPair = ShouldUseSingleLyricBlockForLine(_transitionPromoted);
            var incomingRows = GetTwoRowTransitionRows(
                _transitionPromoted,
                _transitionUpcoming,
                incomingUsesTranslationPair);
            ApplyTwoRowTransitionPrimary(
                IncomingLineText,
                incomingRows.Primary,
                incomingRows.SecondaryIsTranslation);
            ApplyTwoRowTransitionSecondary(
                IncomingTranslationText,
                incomingRows.Secondary,
                incomingRows.SecondaryIsTranslation);
        }
        else
        {
            ApplyDisplayLine(NextLineText, _transitionPromoted, true, progress);
            if (upcomingChanged)
            {
                ApplyDisplayLine(IncomingLineText, _transitionUpcoming, false, 0);
            }
        }

        if (promotedChanged || upcomingChanged)
        {
            UpdatePreferredWidth();
        }

        return true;
    }

    private void ResetForTrackSwitch(
        string safeCurrent,
        string safeNext,
        double progress,
        int lineIndex,
        string trackId)
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

    private void StartTransition(
        string newCurrent,
        string newNext,
        double progress,
        int currentLineIndex)
    {
        var outgoingCurrent = _displayedCurrent;
        var outgoingNext = _displayedNext;
        var outgoingUsesTranslationPair = UsesSingleLyricBlock;
        var incomingUsesTranslationPair = ShouldUseSingleLyricBlockForLine(newCurrent);
        var shouldUseTwoRowOverlayTransition = outgoingUsesTranslationPair || incomingUsesTranslationPair;

        if (_useSingleLyricBlock != incomingUsesTranslationPair)
        {
            _useSingleLyricBlock = incomingUsesTranslationPair;
            _textWidthCache.Clear();
            if (!shouldUseTwoRowOverlayTransition)
            {
                UpdateMetrics();
            }
        }
        else if (!shouldUseTwoRowOverlayTransition)
        {
            ApplySingleLyricBlockModeForLine(newCurrent);
        }

        if (_transitionStyle == LyricTransitionStyle.None)
        {
            StopTransitionAnimations();
            SetNoAnimState();
            SetCurrentLine(newCurrent, progress);
            SetSecondaryLine(newNext);
            ApplyDisplayLine(OutgoingLineText, " ", false, 0);
            ApplyDisplayLine(OutgoingTranslationText, " ", false, 0);
            ApplyDisplayLine(IncomingLineText, " ", false, 0);
            ApplyDisplayLine(IncomingTranslationText, " ", false, 0);
            TrackTransform.Y = 0;
            CurrentLineText.Opacity = 0.98;
            NextLineText.Opacity = UsesSingleLyricBlock ? 1 : _secondaryOpacity;
            OutgoingLineText.Opacity = 0;
            OutgoingTranslationText.Opacity = 0;
            IncomingLineText.Opacity = 0;
            IncomingTranslationText.Opacity = 0;
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

        if (shouldUseTwoRowOverlayTransition)
        {
            StartTranslationPairTransition(
                newCurrent,
                newNext,
                progress,
                currentLineIndex,
                outgoingCurrent,
                outgoingNext,
                outgoingUsesTranslationPair,
                incomingUsesTranslationPair);
            return;
        }

        StopTransitionAnimations();
        _isTransitioning = true;
        _transitionFinalized = false;
        var generation = ++_transitionGeneration;

        var promoted = ToDisplayLine(newCurrent, SearchingText);
        var upcoming = GetSecondaryDisplayLine(newNext);
        _transitionPromoted = promoted;
        _transitionUpcoming = upcoming;
        _transitionProgress = progress;
        _transitionLineIndex = currentLineIndex;
        _transitionBaseNextOpacity = UsesSingleLyricBlock ? 0 : _secondaryOpacity;
        _transitionTargetSecondaryOpacity = ResolveSecondaryOpacity(progress);
        SetNoAnimState();
        SetLineRowHeight(NextLineText, _rowHeightPx);
        NextLineText.Foreground = _primaryBrush;
        NextLineText.FontSize = _currentFontSize;
        ApplyDisplayLine(NextLineText, promoted, true, progress);
        SetLineRowHeight(IncomingLineText, _nextRowHeightPx);
        IncomingLineText.FontSize = _nextFontSize;
        ApplyDisplayLine(IncomingLineText, upcoming, false, 0);
        ApplyDisplayLine(IncomingTranslationText, " ", false, 0);
        IncomingLineText.Opacity = 0;
        IncomingTranslationText.Opacity = 0;
        CurrentLineText.Opacity = 0.98;
        NextLineText.Opacity = _transitionBaseNextOpacity;
        ClearNoAnimState();
        PositionPromotedLineForTransition();
        UpdateIncomingLineTransitionOffset(0);
        UpdatePreferredWidth();

        _transitionStartTimestamp = Stopwatch.GetTimestamp();
        SetMotionTextFormatting(true);
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

    private (string Primary, string Secondary, bool SecondaryIsTranslation) GetTwoRowTransitionRows(
        string current,
        string next,
        bool currentUsesTranslationPair)
    {
        var primary = ToDisplayLine(current, SearchingText);
        if (currentUsesTranslationPair)
        {
            var (main, translation) = SplitTranslation(primary);
            return (main, translation ?? " ", true);
        }

        return (primary, ToDisplayLine(next, " "), false);
    }

    private void ApplyTwoRowTransitionPrimary(
        TextBlock textBlock,
        string text,
        bool pairHasTranslation)
    {
        if (pairHasTranslation)
        {
            ApplyPlainDisplayLine(textBlock, text, true, false);
            return;
        }

        ApplyDisplayLine(textBlock, text, true, _transitionProgress);
    }

    private void ApplyTwoRowTransitionSecondary(
        TextBlock textBlock,
        string text,
        bool isTranslation)
    {
        if (isTranslation)
        {
            ApplyPlainDisplayLine(textBlock, text, false, true);
            return;
        }

        ApplyDisplayLine(textBlock, text, false, 0);
    }

    private void StartTranslationPairTransition(
        string newCurrent,
        string newNext,
        double progress,
        int currentLineIndex,
        string outgoingCurrent,
        string outgoingNext,
        bool outgoingUsesTranslationPair,
        bool incomingUsesTranslationPair)
    {
        StopTransitionAnimations();
        _transitionUsesTranslationPair = true;
        UpdateMetrics();
        _isTransitioning = true;
        _transitionFinalized = false;
        var generation = ++_transitionGeneration;

        var promoted = ToDisplayLine(newCurrent, SearchingText);
        _transitionPromoted = promoted;
        _transitionUpcoming = GetSecondaryDisplayLine(newNext);
        _transitionProgress = progress;
        _transitionLineIndex = currentLineIndex;
        _transitionBaseNextOpacity = outgoingUsesTranslationPair ? 1 : _secondaryOpacity;
        _transitionTargetSecondaryOpacity = incomingUsesTranslationPair ? 1 : ResolveSecondaryOpacity(progress);

        SetNoAnimState();
        TrackTransform.Y = 0;
        CurrentLineText.Opacity = 0;
        NextLineText.Opacity = 0;
        var outgoingRows = GetTwoRowTransitionRows(outgoingCurrent, outgoingNext, outgoingUsesTranslationPair);
        var incomingRows = GetTwoRowTransitionRows(promoted, newNext, incomingUsesTranslationPair);
        SetLineRowHeight(OutgoingLineText, _rowHeightPx);
        OutgoingLineText.FontSize = _currentFontSize;
        OutgoingLineText.Foreground = _primaryBrush;
        ApplyTwoRowTransitionPrimary(OutgoingLineText, outgoingRows.Primary, outgoingRows.SecondaryIsTranslation);
        SetLineRowHeight(OutgoingTranslationText, _nextRowHeightPx);
        OutgoingTranslationText.FontSize = _nextFontSize;
        OutgoingTranslationText.Foreground = outgoingRows.SecondaryIsTranslation ? _secondaryTranslationBrush : _secondaryBrush;
        ApplyTwoRowTransitionSecondary(OutgoingTranslationText, outgoingRows.Secondary, outgoingRows.SecondaryIsTranslation);
        SetLineRowHeight(IncomingLineText, _rowHeightPx);
        IncomingLineText.FontSize = _currentFontSize;
        IncomingLineText.Foreground = _primaryBrush;
        ApplyTwoRowTransitionPrimary(IncomingLineText, incomingRows.Primary, incomingRows.SecondaryIsTranslation);
        SetLineRowHeight(IncomingTranslationText, _nextRowHeightPx);
        IncomingTranslationText.FontSize = _nextFontSize;
        IncomingTranslationText.Foreground = incomingRows.SecondaryIsTranslation ? _secondaryTranslationBrush : _secondaryBrush;
        ApplyTwoRowTransitionSecondary(IncomingTranslationText, incomingRows.Secondary, incomingRows.SecondaryIsTranslation);
        OutgoingLineText.Opacity = 0.98;
        OutgoingTranslationText.Opacity = 0.98;
        IncomingLineText.Opacity = 0;
        IncomingTranslationText.Opacity = 0;
        ClearNoAnimState();
        UpdateTranslationPairOutgoingOffsets(0);
        UpdateTranslationPairIncomingOffsets(0);
        UpdatePreferredWidth();

        _transitionStartTimestamp = Stopwatch.GetTimestamp();
        SetMotionTextFormatting(true);
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
            var opacityE = LightMotion.FadeEase(t);
            var slideE = LightMotion.MoveEase(t);
            var slideDistance = GetTransitionSlideDistance();

            var slideOffset = ResolveTransitionSlideOffset(slideDistance, slideDistance * slideE);
            if (_transitionUsesTranslationPair)
            {
                TrackTransform.Y = 0;
                UpdateTranslationPairOutgoingOffsets(slideOffset);
                UpdateTranslationPairIncomingOffsets(slideOffset);
                var outgoingT = Math.Clamp(t / 0.72, 0, 1);
                var fadeOut = 0.98 * (1 - LightMotion.FadeEase(outgoingT));
                OutgoingLineText.Opacity = fadeOut;
                OutgoingTranslationText.Opacity = fadeOut;
                CurrentLineText.Opacity = 0;
                NextLineText.Opacity = 0;
                var incomingT = Math.Clamp((t - IncomingLineFadeStart) / IncomingLineFadeDuration, 0, 1);
                var incomingOpacity = LightMotion.FadeEase(incomingT);
                IncomingLineText.Opacity = 0.98 * incomingOpacity;
                IncomingTranslationText.Opacity = _transitionTargetSecondaryOpacity * incomingOpacity;
            }
            else
            {
                TrackTransform.Y = -slideOffset;
                UpdateIncomingLineTransitionOffset(slideOffset);
                CurrentLineText.Opacity = 0.98 + ((0.16 - 0.98) * opacityE);
                NextLineText.Opacity = _transitionBaseNextOpacity + ((0.98 - _transitionBaseNextOpacity) * opacityE);
                var incomingT = Math.Clamp((t - IncomingLineFadeStart) / IncomingLineFadeDuration, 0, 1);
                IncomingLineText.Opacity = _transitionTargetSecondaryOpacity * LightMotion.FadeEase(incomingT);
                IncomingTranslationText.Opacity = 0;
            }

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
        SetMotionTextFormatting(false);

        SetNoAnimState();
        TrackTransform.Y = 0;
        NextLineText.Foreground = _secondaryBrush;
        CurrentLineText.Opacity = 0.98;
        var finalSecondaryOpacity = UsesSingleLyricBlock
            ? 1
            : double.IsNaN(_transitionTargetSecondaryOpacity) || _transitionTargetSecondaryOpacity <= 0.01
                ? 0.72
                : _transitionTargetSecondaryOpacity;
        ApplyLineMetrics();
        SetCurrentLine(_transitionPromoted, _transitionProgress);
        SetSecondaryLine(_transitionUpcoming);
        ApplyDisplayLine(OutgoingLineText, " ", false, 0);
        ApplyDisplayLine(OutgoingTranslationText, " ", false, 0);
        ApplyDisplayLine(IncomingLineText, " ", false, 0);
        ApplyDisplayLine(IncomingTranslationText, " ", false, 0);
        OutgoingLineText.Opacity = 0;
        OutgoingTranslationText.Opacity = 0;
        IncomingLineText.Opacity = 0;
        IncomingTranslationText.Opacity = 0;
        _secondaryOpacity = finalSecondaryOpacity;
        NextLineText.Opacity = finalSecondaryOpacity;

        ClearNoAnimState();

        _isTransitioning = false;
        _transitionUsesTranslationPair = false;
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
        ResetLineOffsets();
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
        OutgoingLineText.BeginAnimation(OpacityProperty, null);
        OutgoingTranslationText.BeginAnimation(OpacityProperty, null);
        IncomingLineText.BeginAnimation(OpacityProperty, null);
        IncomingTranslationText.BeginAnimation(OpacityProperty, null);
    }

    private void SetMotionTextFormatting(bool isMoving)
    {
        TextOptions.SetTextFormattingMode(
            LyricsLayer,
            TextFormattingMode.Display);
    }

    private void CancelActiveTransition()
    {
        _transitionGeneration++;
        _searchDwellTimer?.Stop();
        StopTransitionAnimations();
        SetMotionTextFormatting(false);
        _isTransitioning = false;
        _transitionFinalized = false;
        _transitionUsesTranslationPair = false;
        _queuedFrame = null;
        TrackTransform.Y = 0;
        CurrentLineText.Opacity = 0.98;
        NextLineText.Opacity = UsesSingleLyricBlock ? 1 : _secondaryOpacity;
        NextLineText.Foreground = _secondaryBrush;
        ApplyLineMetrics();
        OutgoingLineText.Opacity = 0;
        OutgoingTranslationText.Opacity = 0;
        IncomingLineText.Opacity = 0;
        IncomingTranslationText.Opacity = 0;
        ResetLineOffsets();
    }

    private void UpdateIncomingLineTransitionOffset(double slideOffset)
    {
        var secondaryLineTop = _lyricsTrackTopInset + _currentRowBoxHeight + _rowGapPx;
        SetTextBlockYOffset(IncomingLineText, secondaryLineTop + SecondaryLineYOffset);
    }

    private void PositionPromotedLineForTransition()
    {
        var y = _lyricsTrackTopInset + (_transitionStyle == LyricTransitionStyle.Fade
            ? PrimaryLineYOffset - _linePitchPx
            : PrimaryLineYOffset);
        SetTextBlockYOffset(NextLineText, y);
    }

    private double GetTransitionSlideDistance() =>
        _transitionStyle == LyricTransitionStyle.Fade
            ? 0
            : _transitionUsesTranslationPair
                ? GetTranslationPairSlideDistance()
                : _linePitchPx;

    private double GetTranslationPairSlideDistance() =>
        _currentRowBoxHeight + _rowGapPx + _nextRowBoxHeight;

    private void UpdateTranslationPairOutgoingOffsets(double slideOffset)
    {
        var outgoingTop = _lyricsTrackTopInset - slideOffset;
        SetTextBlockYOffset(OutgoingLineText, outgoingTop + PrimaryLineYOffset);
        SetTextBlockYOffset(
            OutgoingTranslationText,
            outgoingTop + _currentRowBoxHeight + _rowGapPx + SecondaryLineYOffset);
    }

    private void UpdateTranslationPairIncomingOffsets(double slideOffset)
    {
        var incomingTop = _lyricsTrackTopInset + GetTranslationPairSlideDistance() - slideOffset;
        SetTextBlockYOffset(IncomingLineText, incomingTop + PrimaryLineYOffset);
        SetTextBlockYOffset(
            IncomingTranslationText,
            incomingTop + _currentRowBoxHeight + _rowGapPx + SecondaryLineYOffset);
    }

    private double ResolveTransitionSlideOffset(double targetOffset, double easedOffset)
    {
        if (targetOffset <= 0)
        {
            return 0;
        }

        var remaining = Math.Abs(targetOffset - easedOffset);
        var pixelStep = GetDevicePixelStepY();
        if (remaining <= Math.Max(TransitionSettlePixelThreshold, pixelStep))
        {
            return targetOffset;
        }

        return SnapToDevicePixelY(easedOffset);
    }

    private double SnapToDevicePixelY(double value)
    {
        var pixelStep = GetDevicePixelStepY();
        return Math.Round(value / pixelStep) * pixelStep;
    }

    private double GetDevicePixelStepY()
    {
        var dpiScale = IsLoaded ? VisualTreeHelper.GetDpi(this).DpiScaleY : 1;
        return 1.0 / Math.Max(0.1, dpiScale);
    }

    private void ResetLineOffsets()
    {
        SetTextBlockYOffset(CurrentLineText, _lyricsTrackTopInset + PrimaryLineYOffset);
        SetTextBlockYOffset(NextLineText, _lyricsTrackTopInset + SecondaryLineYOffset);
        SetTextBlockYOffset(OutgoingLineText, _lyricsTrackTopInset + PrimaryLineYOffset);
        SetTextBlockYOffset(
            OutgoingTranslationText,
            _lyricsTrackTopInset + _currentRowBoxHeight + _rowGapPx + SecondaryLineYOffset);
        SetTextBlockYOffset(IncomingLineText, _lyricsTrackTopInset + SecondaryLineYOffset);
        SetTextBlockYOffset(
            IncomingTranslationText,
            _lyricsTrackTopInset + _currentRowBoxHeight + _rowGapPx + SecondaryLineYOffset);
    }

    private static void SetTextBlockYOffset(TextBlock textBlock, double y)
    {
        if (textBlock.RenderTransform is TranslateTransform transform)
        {
            transform.Y = y;
            return;
        }

        textBlock.RenderTransform = new TranslateTransform(0, y);
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
            _hasAudioDrivenSpectrum = false;
            SetSpectrumTargetValues(null);
        }

        UpdatePreferredWidth();
        UpdateSongProgressVisuals();
    }

    private void AnimateLayerOpacity(UIElement layer, double target)
    {
        AnimateElementOpacity(layer, target, GetLayerFadeDurationMs());
    }

    private void AnimateElementOpacity(UIElement element, double target, int durationMs, Action? completed = null)
    {
        target = CoerceOpacity(target);
        var from = CoerceOpacity(element.Opacity);
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = from;
        if (!IsLoaded || Math.Abs(from - target) < 0.001 || durationMs <= 0)
        {
            element.Opacity = target;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = LightMotion.CreateFadeEase(),
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = target;
            completed?.Invoke();
        };
        element.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
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
        SetSpectrumTargetValues(null);
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
                Color = _resolvedPrimaryColor,
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
        if (_spectrumColorMode == SpectrumColorMode.Text)
        {
            return _primaryBrush;
        }

        var color = _spectrumColorMode switch
        {
            SpectrumColorMode.CoverAccent when _coverAccentColor is { } accent =>
                CreateReadableSpectrumColor(accent),
            SpectrumColorMode.Gradient =>
                CreateGradientSpectrumColor(index),
            _ => _resolvedPrimaryColor
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
        var right = MixColors(_resolvedPrimaryColor, Media.Color.FromRgb(251, 146, 60), 0.36);
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
        var primary = ResolveSongProgressColor();
        AnimateBrushColor(_progressFillBrush, WithAlpha(primary, (byte)Math.Clamp(Math.Round(255 * _songProgressOpacity), 0, 255)));
        AnimateBrushColor(_progressTrackBrush, WithAlpha(primary, (byte)Math.Clamp(Math.Round(54 * _songProgressOpacity), 8, 120)));
        AnimateBrushColor(_backgroundProgressBrush, WithAlpha(primary, (byte)Math.Clamp(Math.Round(46 * _songProgressOpacity), 10, 92)));

        BottomProgressTrack.Height = _songProgressThickness;
        BottomProgressTrack.Background = _progressTrackBrush;
        BottomProgressFill.Background = _progressFillBrush;
        LyricUnderlineTrack.Height = Math.Max(1, _songProgressThickness);
        LyricUnderlineTrack.Background = _progressTrackBrush;
        LyricUnderlineFill.Background = _progressFillBrush;
        SpectrumProgressTrack.Height = Math.Max(1, _songProgressThickness * 0.75);
        SpectrumProgressTrack.Background = _progressTrackBrush;
        SpectrumProgressFill.Background = _progressFillBrush;
        BackgroundProgressFill.Background = _backgroundProgressBrush;
        BackgroundProgressTrack.CornerRadius = SurfaceBorder.CornerRadius;
        BackgroundProgressFill.CornerRadius = SurfaceBorder.CornerRadius;
        ApplyProgressSurfaceInsets();
        ApplyBackgroundProgressClip();
        CoverBottomProgressTrack.Height = Math.Max(2, _songProgressThickness + 1);
        CoverBottomProgressTrack.Background = _progressTrackBrush;
        CoverBottomProgressTrack.CornerRadius = new CornerRadius(CoverBottomProgressTrack.Height / 2);
        CoverBottomProgressFill.Background = _progressFillBrush;
        CoverBottomProgressFill.CornerRadius = CoverBottomProgressTrack.CornerRadius;
        CoverProgressRingTrack.StrokeThickness = Math.Max(1, _songProgressThickness);
        CoverProgressRingValue.StrokeThickness = Math.Max(1, _songProgressThickness + 0.2);
        CoverProgressRingTrack.Stroke = _progressTrackBrush;
        CoverProgressRingValue.Stroke = _progressFillBrush;
        BorderProgressRingTrack.StrokeThickness = Math.Max(1, _songProgressThickness);
        BorderProgressRingValue.StrokeThickness = Math.Max(1, _songProgressThickness + 0.2);
        BorderProgressRingTrack.Stroke = _progressTrackBrush;
        BorderProgressRingValue.Stroke = _progressFillBrush;
        TimePillText.Foreground = _progressFillBrush;
        TimePillBorder.BorderBrush = _progressTrackBrush;
        ApplySongProgressWidth();

        foreach (var dot in _progressDots)
        {
            dot.Background = _progressFillBrush;
        }
    }

    private Media.Color ResolveSongProgressColor()
    {
        if (_songProgressColorMode == SongProgressColorMode.CoverAccent &&
            _coverAccentColor is { } accent)
        {
            return CreateReadableSpectrumColor(accent);
        }

        if (_songProgressColorMode == SongProgressColorMode.Custom &&
            TryParseMediaColor(_songProgressColor, out var custom))
        {
            return custom;
        }

        return _resolvedPrimaryColor;
    }

    private void ApplySongProgressWidth()
    {
        var width = _useFixedSongProgressWidth ? _songProgressWidth : double.NaN;
        var contentAlignment = _useFixedSongProgressWidth
            ? GetFixedSongProgressAlignment()
            : System.Windows.HorizontalAlignment.Stretch;
        foreach (var track in new[] { LyricUnderlineTrack, SpectrumProgressTrack })
        {
            track.Width = width;
            track.HorizontalAlignment = contentAlignment;
            track.Margin = new Thickness(0);
        }

        ApplySongProgressFillAnchor();

        BottomProgressTrack.Width = width;
        BottomProgressTrack.HorizontalAlignment = _useFixedSongProgressWidth
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Stretch;
        BottomProgressTrack.Margin = _useFixedSongProgressWidth
            ? new Thickness(GetFixedSongProgressLeftOffsetInSurface(), 0, 0, 0)
            : new Thickness(0);
    }

    private System.Windows.HorizontalAlignment GetFixedSongProgressAlignment() => _songProgressAnchor switch
    {
        SongProgressAnchor.Center => System.Windows.HorizontalAlignment.Center,
        SongProgressAnchor.Right => System.Windows.HorizontalAlignment.Right,
        _ => System.Windows.HorizontalAlignment.Left
    };

    private void ApplySongProgressFillAnchor()
    {
        var reverse = _useFixedSongProgressWidth && _songProgressAnchor == SongProgressAnchor.Right;
        var fillAlignment = reverse
            ? System.Windows.HorizontalAlignment.Right
            : System.Windows.HorizontalAlignment.Left;
        var origin = reverse
            ? new System.Windows.Point(1, 0.5)
            : new System.Windows.Point(0, 0.5);

        foreach (var fill in new[] { BottomProgressFill, LyricUnderlineFill, SpectrumProgressFill })
        {
            fill.HorizontalAlignment = fillAlignment;
            fill.RenderTransformOrigin = origin;
        }
    }

    private double GetFixedSongProgressLeftOffsetInSurface()
    {
        var contentLeft = GetLyricsLeftOffsetInSurface();
        if (_songProgressAnchor == SongProgressAnchor.Left)
        {
            return contentLeft;
        }

        var contentWidth = ContentGrid.ActualWidth > 0 ? ContentGrid.ActualWidth : Math.Max(0, SurfaceRoot.ActualWidth - contentLeft);
        var trackWidth = _songProgressWidth;
        return _songProgressAnchor == SongProgressAnchor.Right
            ? contentLeft + contentWidth - trackWidth
            : contentLeft + ((contentWidth - trackWidth) / 2);
    }

    private double GetLyricsLeftOffsetInSurface()
    {
        if (SurfaceRoot.ActualWidth <= 0 || ContentGrid.ActualWidth <= 0)
        {
            return IsStackedCoverLayout
                ? Math.Max(0, LayoutGrid.Margin.Left + ContentGrid.Margin.Left + _stackedContentXOffset)
                : Math.Max(0, (_showCover ? _coverSize + _coverGap : 0) + ContentGrid.Margin.Left);
        }

        try
        {
            var point = ContentGrid.TransformToAncestor(SurfaceRoot).Transform(new System.Windows.Point(0, 0));
            return Math.Max(0, point.X);
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private void UpdateSongProgressVisuals()
    {
        var activeStyle = ResolveActiveSongProgressStyle();
        if (activeStyle != _lastActiveSongProgressStyle)
        {
            _lastActiveSongProgressStyle = activeStyle;
            ApplyCoverLayout();
            UpdatePreferredWidth();
        }

        ApplySongProgressWidth();
        ApplyProgressSurfaceInsets();
        UpdateSongProgressVisibility(activeStyle);
        ApplyBackgroundProgressClip();
        if (activeStyle == SongProgressDisplayStyle.Off)
        {
            return;
        }

        var progress = Math.Clamp(_hasSongProgressVisual ? _songProgressVisual : _songProgress, 0, 1);
        switch (activeStyle)
        {
            case SongProgressDisplayStyle.BottomLine:
                SetProgressFill(BottomProgressTrack, BottomProgressFill, BottomProgressScale, progress);
                break;
            case SongProgressDisplayStyle.LyricUnderline:
                LyricUnderlineOffset.Y = _lyricsTrackTopInset + Math.Max(2, _currentRowBoxHeight - 2);
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
            case SongProgressDisplayStyle.BorderRing:
                UpdateBorderProgressGeometry();
                break;
            case SongProgressDisplayStyle.BackgroundFill:
                SetProgressFill(BackgroundProgressTrack, BackgroundProgressFill, BackgroundProgressScale, progress, GetProgressSurfaceWidth());
                break;
        }
    }

    private void UpdateSongProgressVisibility(SongProgressDisplayStyle activeStyle)
    {
        SetProgressVisibility(BottomProgressTrack, activeStyle == SongProgressDisplayStyle.BottomLine);
        SetProgressVisibility(LyricUnderlineTrack, activeStyle == SongProgressDisplayStyle.LyricUnderline);
        SetProgressVisibility(CoverProgressRingTrack, activeStyle == SongProgressDisplayStyle.CoverRing);
        SetProgressVisibility(CoverProgressRingValue, activeStyle == SongProgressDisplayStyle.CoverRing);
        SetProgressVisibility(CoverBottomProgressTrack, activeStyle == SongProgressDisplayStyle.CoverBottomBar);
        SetProgressVisibility(SpectrumProgressTrack, activeStyle == SongProgressDisplayStyle.SpectrumBaseline);
        SetProgressVisibility(TimePillBorder, activeStyle == SongProgressDisplayStyle.TimePill);
        SetProgressVisibility(ProgressDotsPanel, activeStyle == SongProgressDisplayStyle.Dots);
        SetProgressVisibility(BorderProgressRingTrack, activeStyle == SongProgressDisplayStyle.BorderRing);
        SetProgressVisibility(BorderProgressRingValue, activeStyle == SongProgressDisplayStyle.BorderRing);
        SetProgressVisibility(BackgroundProgressTrack, activeStyle == SongProgressDisplayStyle.BackgroundFill);
    }

    private void SetProgressVisibility(UIElement element, bool visible)
    {
        if (_progressVisibilityTargets.TryGetValue(element, out var currentTarget) &&
            currentTarget == visible)
        {
            return;
        }

        _progressVisibilityTargets[element] = visible;
        var wasVisible = element.Visibility == Visibility.Visible;
        var from = CoerceOpacity(element.Opacity);
        element.BeginAnimation(OpacityProperty, null);

        if (visible)
        {
            element.Visibility = Visibility.Visible;
            element.Opacity = wasVisible && from > 0 ? from : 0;
            AnimateProgressOpacity(element, element.Opacity, 1, visible);
            return;
        }

        if (element.Visibility != Visibility.Visible)
        {
            element.Opacity = 1;
            return;
        }

        element.Opacity = from;
        AnimateProgressOpacity(element, from, 0, visible);
    }

    private void AnimateProgressOpacity(UIElement element, double from, double target, bool visibleTarget)
    {
        from = CoerceOpacity(from);
        target = CoerceOpacity(target);
        var duration = LightMotion.ProgressVisibilityMs(_animationIntensity);
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = LightMotion.CreateFadeEase(),
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (!_progressVisibilityTargets.TryGetValue(element, out var currentTarget) ||
                currentTarget != visibleTarget)
            {
                return;
            }

            element.BeginAnimation(OpacityProperty, null);
            if (visibleTarget)
            {
                element.Visibility = Visibility.Visible;
                element.Opacity = 1;
            }
            else
            {
                element.Visibility = Visibility.Collapsed;
                element.Opacity = 1;
            }
        };
        element.BeginAnimation(OpacityProperty, animation);
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

    private void StartProgressRenderer()
    {
        if (_progressRenderingHandler is not null)
        {
            return;
        }

        _progressLastTimestamp = Stopwatch.GetTimestamp();
        _progressRenderingHandler = (_, _) => OnProgressRenderFrame();
        CompositionTarget.Rendering += _progressRenderingHandler;
    }

    private void StopProgressRenderer()
    {
        if (_progressRenderingHandler is null)
        {
            return;
        }

        CompositionTarget.Rendering -= _progressRenderingHandler;
        _progressRenderingHandler = null;
    }

    private void OnProgressRenderFrame()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = Math.Clamp(
            (now - _progressLastTimestamp) / (double)Stopwatch.Frequency,
            0,
            0.08);
        _progressLastTimestamp = now;

        if (!_hasSongProgressVisual)
        {
            _songProgressVisual = _songProgress;
            _hasSongProgressVisual = true;
        }

        var activeStyle = ResolveActiveSongProgressStyle();
        var target = activeStyle == SongProgressDisplayStyle.Off ? 0 : Math.Clamp(_songProgress, 0, 1);
        var rate = LightMotion.ProgressFollowRate(_animationIntensity);
        if (target < _songProgressVisual)
        {
            rate *= 1.35;
        }

        var follow = 1 - Math.Exp(-rate * elapsedSeconds);
        _songProgressVisual += (target - _songProgressVisual) * follow;
        if (Math.Abs(_songProgressVisual - target) < 0.0005)
        {
            _songProgressVisual = target;
        }

        UpdateSongProgressVisuals();

        var keepRendering =
            Math.Abs(_songProgressVisual - target) >= 0.0005 ||
            (activeStyle == SongProgressDisplayStyle.Dots && _isSongProgressPlaying);
        if (!keepRendering)
        {
            StopProgressRenderer();
        }
    }

    private static void SetProgressFill(Border track, Border fill, ScaleTransform scale, double progress, double fallbackWidth = 0)
    {
        var width = track.ActualWidth > 0 ? track.ActualWidth : track.RenderSize.Width;
        if (width <= 0 && fallbackWidth > 0)
        {
            width = fallbackWidth;
        }

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
        Grid.SetRow(CoverProgressOverlay, 0);
        Grid.SetColumn(CoverProgressOverlay, 0);
        Grid.SetColumnSpan(CoverProgressOverlay, columnSpan);
        CoverProgressOverlay.Width = _coverSize;
        CoverProgressOverlay.Height = _coverSize;
        CoverProgressOverlay.HorizontalAlignment = CoverBorder.HorizontalAlignment;
        CoverProgressOverlay.VerticalAlignment = CoverBorder.VerticalAlignment;
        CoverProgressOverlay.Margin = CoverBorder.Margin;
        CoverProgressOverlay.RenderTransform = IsStackedCoverLayout
            ? new TranslateTransform(_stackedCoverXOffset, _stackedCoverYOffset)
            : null;

        var ringPadding = GetCoverProgressRingPadding();
        var ringSize = GetCoverProgressRingSize();
        foreach (var path in new[] { CoverProgressRingTrack, CoverProgressRingValue })
        {
            Grid.SetRow(path, 0);
            Grid.SetColumn(path, 0);
            Grid.SetColumnSpan(path, columnSpan);
            path.Width = ringSize;
            path.Height = ringSize;
            path.HorizontalAlignment = CoverBorder.HorizontalAlignment;
            path.VerticalAlignment = CoverBorder.VerticalAlignment;
            path.Margin = CoverBorder.Margin;
            path.RenderTransform = IsStackedCoverLayout
                ? new TranslateTransform(_stackedCoverXOffset - ringPadding, _stackedCoverYOffset)
                : null;
        }

        UpdateCoverProgressGeometry();
    }

    private double GetCoverProgressOuterPadding() =>
        _showCover && _songProgressStyle == SongProgressDisplayStyle.CoverRing
            ? GetCoverProgressRingPadding()
            : 0;

    private double GetCoverSlotSize() => _coverSize + (GetCoverProgressOuterPadding() * 2);

    private void UpdateCoverProgressGeometry()
    {
        var progress = Math.Clamp(_hasSongProgressVisual ? _songProgressVisual : _songProgress, 0, 1);
        var trackGeometry = CreateCoverProgressGeometry(1);
        var valueGeometry = CreateCoverProgressGeometry(progress);
        CoverProgressRingTrack.Data = trackGeometry;
        CoverProgressRingValue.Data = valueGeometry;
    }

    private void UpdateBorderProgressGeometry()
    {
        var width = GetProgressSurfaceWidth();
        var height = GetProgressSurfaceHeight();
        if (width <= 4 || height <= 4)
        {
            BorderProgressRingTrack.Data = Media.Geometry.Empty;
            BorderProgressRingValue.Data = Media.Geometry.Empty;
            return;
        }

        var inset = Math.Max(1, _songProgressThickness / 2);
        var radius = GetMaxCornerRadius(SurfaceBorder.CornerRadius);
        var progress = Math.Clamp(_hasSongProgressVisual ? _songProgressVisual : _songProgress, 0, 1);
        BorderProgressRingTrack.Data = CreateRectangularProgressGeometry(width, height, 1, inset, radius);
        BorderProgressRingValue.Data = CreateRectangularProgressGeometry(width, height, progress, inset, radius);
    }

    private void ApplyBackgroundProgressClip()
    {
        var width = GetProgressSurfaceWidth();
        var height = GetProgressSurfaceHeight();
        if (width <= 0 || height <= 0)
        {
            BackgroundProgressTrack.Clip = null;
            return;
        }

        var radius = Math.Clamp(GetMaxCornerRadius(SurfaceBorder.CornerRadius), 0, Math.Min(width, height) / 2);
        var clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        if (clip.CanFreeze)
        {
            clip.Freeze();
        }

        BackgroundProgressTrack.Clip = clip;
    }

    private void ApplyProgressSurfaceInsets()
    {
        var padding = SurfaceBorder.Padding;
        var margin = new Thickness(-padding.Left, -padding.Top, -padding.Right, -padding.Bottom);
        BackgroundProgressTrack.Margin = margin;
        BorderProgressRingTrack.Margin = margin;
        BorderProgressRingValue.Margin = margin;
    }

    private double GetProgressSurfaceWidth()
    {
        var padding = SurfaceBorder.Padding;
        if (SurfaceRoot.ActualWidth > 0)
        {
            return SurfaceRoot.ActualWidth + padding.Left + padding.Right;
        }

        if (SurfaceBorder.ActualWidth > 0)
        {
            return SurfaceBorder.ActualWidth;
        }

        return ActualWidth;
    }

    private double GetProgressSurfaceHeight()
    {
        var padding = SurfaceBorder.Padding;
        if (SurfaceRoot.ActualHeight > 0)
        {
            return SurfaceRoot.ActualHeight + padding.Top + padding.Bottom;
        }

        if (SurfaceBorder.ActualHeight > 0)
        {
            return SurfaceBorder.ActualHeight;
        }

        return ActualHeight;
    }

    private static double GetMaxCornerRadius(CornerRadius radius) =>
        Math.Max(Math.Max(radius.TopLeft, radius.TopRight), Math.Max(radius.BottomRight, radius.BottomLeft));

    private Media.Geometry CreateCoverProgressGeometry(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (progress <= 0)
        {
            return Media.Geometry.Empty;
        }

        var inset = GetCoverProgressRingPadding();
        var size = GetCoverProgressRingSize();
        return _coverDisplayStyle == CoverDisplayStyle.Circle
            ? CreateCircularProgressGeometry(size, progress, inset)
            : CreateRectangularProgressGeometry(size, size, progress, inset, _coverCornerRadius);
    }

    private double GetCoverProgressRingPadding()
    {
        var configuredStroke = Math.Max(1, _songProgressThickness + 0.2);
        var actualStroke = Math.Max(CoverProgressRingTrack.StrokeThickness, CoverProgressRingValue.StrokeThickness);
        var stroke = Math.Max(configuredStroke, actualStroke);
        return Math.Ceiling(Math.Max(3, (stroke / 2) + 2));
    }

    private double GetCoverProgressRingSize() => Math.Max(8, _coverSize + (GetCoverProgressRingPadding() * 2));

    private static Media.Geometry CreateCircularProgressGeometry(double size, double progress, double inset)
    {
        var radius = Math.Max(1, (size / 2) - Math.Max(1, inset));
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

    private static Media.Geometry CreateRectangularProgressGeometry(
        double width,
        double height,
        double progress,
        double inset,
        double cornerRadius)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (progress <= 0)
        {
            return Media.Geometry.Empty;
        }

        width = Math.Max(2, width);
        height = Math.Max(2, height);
        inset = Math.Clamp(inset, 0.5, Math.Min(width, height) / 2);
        var maxRadius = Math.Max(0, (Math.Min(width, height) / 2) - inset);
        var radius = Math.Clamp(cornerRadius - inset, 0, maxRadius);
        var left = inset;
        var top = inset;
        var right = width - inset;
        var bottom = height - inset;
        var points = radius <= 0
            ? CreateSharpRectangleProgressPoints(width, height, left, top, right, bottom)
            : CreateRoundedRectangleProgressPoints(width, height, left, top, right, bottom, radius);

        return CreatePartialPolylineGeometry(points, progress);
    }

    private static IReadOnlyList<System.Windows.Point> CreateSharpRectangleProgressPoints(
        double width,
        double height,
        double left,
        double top,
        double right,
        double bottom) =>
        new[]
        {
            new System.Windows.Point(width / 2, top),
            new System.Windows.Point(right, top),
            new System.Windows.Point(right, bottom),
            new System.Windows.Point(left, bottom),
            new System.Windows.Point(left, top),
            new System.Windows.Point(width / 2, top)
        };

    private static IReadOnlyList<System.Windows.Point> CreateRoundedRectangleProgressPoints(
        double width,
        double height,
        double left,
        double top,
        double right,
        double bottom,
        double radius)
    {
        const int ArcSteps = 9;
        var points = new List<System.Windows.Point>
        {
            new(width / 2, top),
            new(right - radius, top)
        };

        AddArc(points, new System.Windows.Point(right - radius, top + radius), radius, -90, 0, ArcSteps);
        AddPoint(points, new System.Windows.Point(right, bottom - radius));
        AddArc(points, new System.Windows.Point(right - radius, bottom - radius), radius, 0, 90, ArcSteps);
        AddPoint(points, new System.Windows.Point(left + radius, bottom));
        AddArc(points, new System.Windows.Point(left + radius, bottom - radius), radius, 90, 180, ArcSteps);
        AddPoint(points, new System.Windows.Point(left, top + radius));
        AddArc(points, new System.Windows.Point(left + radius, top + radius), radius, 180, 270, ArcSteps);
        AddPoint(points, new System.Windows.Point(width / 2, top));
        return points;
    }

    private static void AddArc(
        ICollection<System.Windows.Point> points,
        System.Windows.Point center,
        double radius,
        double startDegrees,
        double endDegrees,
        int steps)
    {
        for (var i = 1; i <= steps; i++)
        {
            var amount = i / (double)steps;
            var angle = startDegrees + ((endDegrees - startDegrees) * amount);
            AddPoint(points, PointOnCircle(center, radius, angle));
        }
    }

    private static void AddPoint(ICollection<System.Windows.Point> points, System.Windows.Point point)
    {
        if (points.LastOrDefault() != point)
        {
            points.Add(point);
        }
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
        ResolveActiveSongProgressStyle() == SongProgressDisplayStyle.TimePill ? TimePillWidthReserve : 0;

    private double GetTimePillContentInset() =>
        ResolveActiveSongProgressStyle() == SongProgressDisplayStyle.TimePill ? TimePillWidthReserve : 0;

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

    private static bool TryParseMediaColor(string? color, out Media.Color parsedColor)
    {
        parsedColor = Media.Colors.White;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        try
        {
            if (Media.ColorConverter.ConvertFromString(color.Trim()) is Media.Color mediaColor)
            {
                parsedColor = mediaColor;
                return true;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        return false;
    }

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
            SetSpectrumBarLevel(bar, _spectrumVisuals[i]);
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
        var availableHost = Math.Max(MinHostHeight, layoutHeight - DescenderBuffer - GetLyricsGridTopInset());
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

    private static SolidColorBrush CreateMutableBrush(Media.Color color) => new(color);

    private static double CoerceOpacity(double opacity) =>
        double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 0;

    private void AnimateBrushColor(SolidColorBrush brush, Media.Color target)
    {
        var current = brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = target;

        if (!IsLoaded ||
            current == target)
        {
            return;
        }

        var durationMs = LightMotion.ColorTransitionMs(_animationIntensity);
        if (durationMs <= 0)
        {
            return;
        }

        var animation = new ColorAnimation(current, target, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = LightMotion.CreateFadeEase(),
            FillBehavior = FillBehavior.Stop
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private byte GetTranslationAlpha() =>
        (byte)Math.Clamp(Math.Round(255 * _translationOpacity), 0, 255);

    private readonly record struct RowHeightCacheKey(string FontFamily, double FontSize, FontWeight Weight);

    private (double HostHeight, double PrimaryRow, double NextRow, double Gap) ComputeMetricsFromFont(double fontSize)
    {
        var nextSize = Math.Max(9, fontSize * 0.92);
        var primaryRow = MeasureFontRowHeight(fontSize);
        var nextRow = MeasureFontRowHeight(nextSize);
        var gap = ResolveLineGap(fontSize);
        var hostHeight = primaryRow + gap + nextRow;
        return (hostHeight, primaryRow, nextRow, gap);
    }

    private bool AllowsNewLineTranslation =>
        _showLyricTranslation &&
        _translationLayout == LyricTranslationLayout.NewLine;

    private bool ShouldReserveTranslationLine => UsesSingleLyricBlock;

    private bool UsesSingleLyricBlock => _useSingleLyricBlock;

    private bool ShouldUseSingleLyricBlockForLine(string line) =>
        AllowsNewLineTranslation && HasTranslation(line);

    private string GetActivePrimaryLineForBlockMode() =>
        _isTransitioning ? _transitionPromoted : _displayedCurrent;

    private bool ApplySingleLyricBlockModeForLine(string line)
    {
        var shouldUseSingleBlock = ShouldUseSingleLyricBlockForLine(line);
        if (_useSingleLyricBlock == shouldUseSingleBlock)
        {
            return false;
        }

        _useSingleLyricBlock = shouldUseSingleBlock;
        _textWidthCache.Clear();

        if (_isTransitioning)
        {
            CancelActiveTransition();
            return true;
        }

        UpdateMetrics();
        return true;
    }

    private double GetSingleBlockTransitionClipBuffer() =>
        AllowsNewLineTranslation || _transitionUsesTranslationPair
            ? SingleBlockTransitionClipBuffer
            : 0;

    private double GetLyricsTransitionClipBuffer() =>
        IsStackedCoverLayout
            ? Math.Max(StackedTransitionClipBuffer, GetSingleBlockTransitionClipBuffer())
            : GetSingleBlockTransitionClipBuffer();

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
        _currentRowBoxHeight = GetLineRowBoxHeight(_rowHeightPx);
        _nextRowBoxHeight = GetLineRowBoxHeight(_nextRowHeightPx);
        _rowGapPx = metrics.Gap;
        _linePitchPx = _currentRowBoxHeight + _rowGapPx;
    }

    private static double GetLineRowBoxHeight(double minHeight) =>
        Math.Max(MinRowHeight, Math.Ceiling(minHeight));

    private static void SetLineRowHeight(TextBlock textBlock, double minHeight)
    {
        var height = GetLineRowBoxHeight(minHeight);
        textBlock.MinHeight = height;
        textBlock.Height = height;
    }

    private void ApplyLineMetrics()
    {
        SetLineRowHeight(CurrentLineText, _rowHeightPx);
        CurrentLineText.FontSize = _currentFontSize;
        NextLineText.Margin = new Thickness(0, _rowGapPx, 0, 0);
        SetLineRowHeight(NextLineText, _nextRowHeightPx);
        NextLineText.FontSize = _nextFontSize;
        OutgoingLineText.Margin = new Thickness(0);
        SetLineRowHeight(OutgoingLineText, _rowHeightPx);
        OutgoingLineText.FontSize = _currentFontSize;
        OutgoingTranslationText.Margin = new Thickness(0);
        SetLineRowHeight(OutgoingTranslationText, _nextRowHeightPx);
        OutgoingTranslationText.FontSize = _nextFontSize;
        IncomingLineText.Margin = new Thickness(0);
        SetLineRowHeight(IncomingLineText, _nextRowHeightPx);
        IncomingLineText.FontSize = _nextFontSize;
        IncomingTranslationText.Margin = new Thickness(0);
        SetLineRowHeight(IncomingTranslationText, _nextRowHeightPx);
        IncomingTranslationText.FontSize = _nextFontSize;
        ApplyLyricsStageMetrics();
    }

    private void ApplyLyricsStageMetrics()
    {
        var clipBuffer = GetLyricsTransitionClipBuffer();
        var stableHostHeight = GetLyricsStableHostHeight();
        var transitionHostHeight = GetLyricsTransitionHostHeight();
        var stageHeight = Math.Max(MinHostHeight, Math.Ceiling(transitionHostHeight + (clipBuffer * 2)));
        _lyricsTrackTopInset = clipBuffer + Math.Max(0, (transitionHostHeight - stableHostHeight) / 2);
        LyricsStage.Height = stageHeight;
        LyricsLayer.Height = stageHeight;
        SpectrumLayer.Height = stageHeight;
        TrackPanel.Margin = new Thickness(0);
        ResetLineOffsets();
    }

    private double GetLyricsStableHostHeight() =>
        _currentRowBoxHeight + _rowGapPx + _nextRowBoxHeight;

    private double GetLyricsTransitionHostHeight() =>
        _currentRowBoxHeight + _rowGapPx + Math.Max(_currentRowBoxHeight, _nextRowBoxHeight);

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
            ? MeasureSpectrumContentWidth()
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
        AddLineIfMeaningful(lines, IncomingTranslationText.Text);
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
        var (main, translation) = SplitTranslation(text);
        if (UsesSingleLyricBlock && !string.IsNullOrWhiteSpace(translation))
        {
            var translationSize = Math.Max(8, _currentFontSize * _translationFontScale);
            return Math.Max(
                MeasureLineWidthAt(main, _currentFontSize, dpi),
                MeasureLineWidthAt(translation, translationSize, dpi));
        }

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
            TextFormattingMode.Ideal,
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
        OutgoingLineText.TextTrimming = trimming;
        OutgoingTranslationText.TextTrimming = trimming;
        IncomingLineText.TextTrimming = trimming;
        IncomingTranslationText.TextTrimming = trimming;
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

    private void SetCurrentLine(
        string line,
        double progress = -1)
    {
        var safe = ToDisplayLine(line, SearchingText);
        var effectiveProgress = progress >= 0 ? progress : _lastLineProgress;
        if (safe == _displayedCurrent)
        {
            ApplyCurrentDisplayLine(safe, effectiveProgress);
            return;
        }

        ApplyCurrentDisplayLine(safe, effectiveProgress);
        _displayedCurrent = safe;
        UpdatePreferredWidth();
    }

    private void SetSecondaryLine(string line)
    {
        if (UsesSingleLyricBlock)
        {
            return;
        }

        var safe = GetSecondaryDisplayLine(line);
        if (safe == _displayedNext)
        {
            ApplyDisplayLine(NextLineText, safe, false, 0);
            return;
        }

        ApplyDisplayLine(NextLineText, safe, false, 0);
        _displayedNext = safe;
        UpdatePreferredWidth();
    }

    private void ApplyCurrentDisplayLine(string text, double progress)
    {
        if (UsesSingleLyricBlock)
        {
            var (main, translation) = SplitTranslation(text);
            ApplyPlainDisplayLine(CurrentLineText, main, true, false);
            ApplyPlainDisplayLine(NextLineText, translation ?? " ", false, true);
            _displayedNext = translation ?? " ";
            NextLineText.Opacity = 1;
            return;
        }

        ApplyDisplayLine(CurrentLineText, text, true, progress);
    }

    private void ApplyPlainDisplayLine(
        TextBlock textBlock,
        string text,
        bool isPrimary,
        bool isTranslation)
    {
        textBlock.Inlines.Clear();
        var brush = isTranslation
            ? (isPrimary ? _primaryTranslationBrush : _secondaryTranslationBrush)
            : (isPrimary ? _primaryBrush : _secondaryBrush);
        var run = new Run(text)
        {
            Foreground = brush,
            FontFamily = _fontFamily,
            FontWeight = _fontWeight
        };
        if (isTranslation)
        {
            var baseFontSize = isPrimary ? _currentFontSize : _nextFontSize;
            run.FontSize = Math.Max(8, baseFontSize * _translationFontScale);
        }

        textBlock.Inlines.Add(run);
    }

    private void ApplyDisplayLine(
        TextBlock textBlock,
        string text,
        bool isPrimary,
        double progress)
    {
        textBlock.Inlines.Clear();
        var (main, translation) = SplitTranslation(text);
        var brush = isPrimary ? _primaryBrush : _secondaryBrush;
        var translationBrush = isPrimary ? _primaryTranslationBrush : _secondaryTranslationBrush;
        var lineFontSize = isPrimary ? _currentFontSize : _nextFontSize;
        textBlock.Inlines.Add(new Run(main)
        {
            Foreground = brush,
            FontFamily = _fontFamily,
            FontWeight = _fontWeight
        });

        if (!string.IsNullOrWhiteSpace(translation))
        {
            if (ShouldRenderTranslationAsNewLine(isPrimary))
            {
                textBlock.Inlines.Add(new LineBreak());
            }
            else
            {
                textBlock.Inlines.Add(new Run(" "));
            }

            textBlock.Inlines.Add(new Run(translation)
            {
                Foreground = translationBrush,
                FontFamily = _fontFamily,
                FontSize = Math.Max(8, lineFontSize * _translationFontScale),
                FontWeight = _fontWeight
            });
        }
    }

    private bool ShouldRenderTranslationAsNewLine(bool isPrimary) =>
        false;

    private static (string Main, string? Translation) SplitTranslation(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 3)
        {
            return (text, null);
        }

        var close = trimmed[^1];
        var open = close switch
        {
            ')' => '(',
            '）' => '（',
            ']' => '[',
            '】' => '【',
            _ => '\0'
        };
        if (open == '\0')
        {
            return (text, null);
        }

        var start = trimmed.LastIndexOf(open);
        if (start <= 0 || start >= trimmed.Length - 2)
        {
            return (text, null);
        }

        var translation = trimmed[(start + 1)..^1].Trim();
        if (translation.Length == 0)
        {
            return (text, null);
        }

        var main = trimmed[..start].TrimEnd();
        main = RemoveTrailingDuplicateTranslation(main, translation);
        return (main, translation);
    }

    private static bool HasTranslation(string text) =>
        !string.IsNullOrWhiteSpace(SplitTranslation(text).Translation);

    private static string RemoveTrailingDuplicateTranslation(string main, string translation)
    {
        var normalizedMain = main.TrimEnd();
        if (translation.Length == 0 ||
            !normalizedMain.EndsWith(translation, StringComparison.Ordinal))
        {
            return main;
        }

        var next = normalizedMain[..^translation.Length].TrimEnd();
        if (next.Length == 0)
        {
            return main;
        }

        return next;
    }

    private void UpdateSecondaryOpacity(double progress)
    {
        if (_isTransitioning)
        {
            return;
        }

        if (UsesSingleLyricBlock)
        {
            _secondaryOpacity = 1;
            NextLineText.Opacity = 1;
            OutgoingLineText.Opacity = 0;
            OutgoingTranslationText.Opacity = 0;
            IncomingLineText.Opacity = 0;
            IncomingTranslationText.Opacity = 0;
            return;
        }

        var target = ResolveSecondaryOpacity(progress);
        _secondaryOpacity += (target - _secondaryOpacity) * 0.28;
        NextLineText.Opacity = _secondaryOpacity;
        OutgoingLineText.Opacity = 0;
        OutgoingTranslationText.Opacity = 0;
    }

    private double ResolveSecondaryOpacity(double progress) =>
        UsesSingleLyricBlock
            ? 1
            : 0.58 + ((1 - Math.Clamp(progress, 0, 1)) * 0.16);

    private void ApplyLineTypography(TextBlock textBlock, bool isPrimary)
    {
        textBlock.FontFamily = _fontFamily;
        textBlock.FontWeight = _fontWeight;
        textBlock.Foreground = isPrimary ? _primaryBrush : _secondaryBrush;
        textBlock.LineStackingStrategy = LineStackingStrategy.MaxHeight;
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

    private string GetSecondaryDisplayLine(string line) =>
        UsesSingleLyricBlock ? " " : ToDisplayLine(line, " ");

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

    private int GetTransitionDurationMs() => LightMotion.LyricsTransitionMs(_animationIntensity);

    private int GetLayerFadeDurationMs() => LightMotion.LayerFadeMs(_animationIntensity);

    private int GetCoverFadeDurationMs() => LightMotion.CoverFadeMs(_animationIntensity);

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

    private sealed record LyricsFrame(
        string Current,
        string Next,
        double Progress,
        int LineIndex);
}
