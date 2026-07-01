using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskbarLyrics.Core.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace TaskbarLyrics.Light.App;

public partial class SettingsWindow : Window
{
    private static readonly HashSet<string> LocalAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".wma"
    };

    private static readonly TimeSpan SaveDebounceInterval = TimeSpan.FromMilliseconds(250);

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer;
    private bool _isLoading;
    private bool _sidebarCollapsed;
    private bool _isUpdatingNavFromScroll;
    private bool _fontOptionsPopulated;
    private bool _isCheckingUpdate;
    private bool _isInstallingUpdate;
    private UpdateCheckResult? _latestUpdateResult;
    private CancellationTokenSource? _localFoldersStatusCancellation;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        _settings = settings;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = SaveDebounceInterval
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSettings();
        };

        LoadPlayerIcons();
        WireEvents();
        LoadFromSettings();
        Closed += (_, _) =>
        {
            OnSettingChanged();
            FlushPendingSettings();
            _localFoldersStatusCancellation?.Cancel();
            _localFoldersStatusCancellation?.Dispose();
        };
    }

    public void ApplyExternalSettings(AppSettings settings)
    {
        CopySettings(settings, _settings);
        LoadFromSettings();
        if (System.Windows.Application.Current is App app)
        {
            app.SaveSettings(_settings.Clone());
        }
    }

    private void LoadPlayerIcons()
    {
        QQIcon.Source = LoadPlayerIcon("QQ音乐.png");
        NeteaseIcon.Source = LoadPlayerIcon("网易云音乐.png");
        KugouIcon.Source = LoadPlayerIcon("酷狗音乐.png");
        SpotifyIcon.Source = LoadPlayerIcon("spotify.png");
    }

    private static ImageSource? LoadPlayerIcon(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "PlayerIcons", fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void WireEvents()
    {
        EnableQQMusicCheck.Checked += (_, _) => OnSettingChanged();
        EnableQQMusicCheck.Unchecked += (_, _) => OnSettingChanged();
        EnableNeteaseCheck.Checked += (_, _) => OnSettingChanged();
        EnableNeteaseCheck.Unchecked += (_, _) => OnSettingChanged();
        EnableKugouCheck.Checked += (_, _) => OnSettingChanged();
        EnableKugouCheck.Unchecked += (_, _) => OnSettingChanged();
        EnableSpotifyCheck.Checked += (_, _) => OnSettingChanged();
        EnableSpotifyCheck.Unchecked += (_, _) => OnSettingChanged();
        ShowLyricsOnStartupCheck.Checked += (_, _) => OnSettingChanged();
        ShowLyricsOnStartupCheck.Unchecked += (_, _) => OnSettingChanged();
        StartWithWindowsCheck.Checked += (_, _) => OnSettingChanged();
        StartWithWindowsCheck.Unchecked += (_, _) => OnSettingChanged();
        AutoShowLyricsWhenPlayerOpensCheck.Checked += (_, _) => OnSettingChanged();
        AutoShowLyricsWhenPlayerOpensCheck.Unchecked += (_, _) => OnSettingChanged();
        AutoHideLyricsWhenPlayerClosesCheck.Checked += (_, _) => OnSettingChanged();
        AutoHideLyricsWhenPlayerClosesCheck.Unchecked += (_, _) => OnSettingChanged();
        ShowLyricTranslationCheck.Checked += (_, _) => OnSettingChanged();
        ShowLyricTranslationCheck.Unchecked += (_, _) => OnSettingChanged();
        EnablePureMusicSpectrumCheck.Checked += (_, _) => OnSettingChanged();
        EnablePureMusicSpectrumCheck.Unchecked += (_, _) => OnSettingChanged();
        EnableSpectrumCheck.Checked += (_, _) => OnSettingChanged();
        EnableSpectrumCheck.Unchecked += (_, _) => OnSettingChanged();
        ShowSpectrumWhenLyricsNotFoundCheck.Checked += (_, _) => OnSettingChanged();
        ShowSpectrumWhenLyricsNotFoundCheck.Unchecked += (_, _) => OnSettingChanged();
        ShowSpectrumWhenLyricsAvailableCheck.Checked += (_, _) => OnSettingChanged();
        ShowSpectrumWhenLyricsAvailableCheck.Unchecked += (_, _) => OnSettingChanged();
        SpectrumStyleCombo.SelectionChanged += (_, _) => OnSettingChanged();
        LyricOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        QqMusicLyricOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        NeteaseLyricOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        KugouLyricOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        SpotifyLyricOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        EnableLocalLyricsCheck.Checked += (_, _) => OnSettingChanged();
        EnableLocalLyricsCheck.Unchecked += (_, _) => OnSettingChanged();
        ShowCoverImageCheck.Checked += (_, _) => OnSettingChanged();
        ShowCoverImageCheck.Unchecked += (_, _) => OnSettingChanged();
        LocalLyricsModeCombo.SelectionChanged += (_, _) => OnSettingChanged();
        LocalCoverModeCombo.SelectionChanged += (_, _) => OnSettingChanged();
        LocalMusicFoldersBox.LostFocus += (_, _) => OnSettingChanged();
        LocalMusicFoldersBox.TextChanged += (_, _) => RefreshLocalFoldersStatus();
        AddLocalFolderButton.Click += (_, _) => AddLocalMusicFolderFromExplorer();
        ForceAlwaysOnTopCheck.Checked += (_, _) => OnSettingChanged();
        ForceAlwaysOnTopCheck.Unchecked += (_, _) => OnSettingChanged();
        AutoCheckUpdatesCheck.Checked += (_, _) => OnSettingChanged();
        AutoCheckUpdatesCheck.Unchecked += (_, _) => OnSettingChanged();
        ShowBackgroundCheck.Checked += (_, _) => OnSettingChanged();
        ShowBackgroundCheck.Unchecked += (_, _) => OnSettingChanged();
        ShowBorderCheck.Checked += (_, _) => OnSettingChanged();
        ShowBorderCheck.Unchecked += (_, _) => OnSettingChanged();
        ShowTextShadowCheck.Checked += (_, _) => OnSettingChanged();
        ShowTextShadowCheck.Unchecked += (_, _) => OnSettingChanged();
        UseCoverAccentColorCheck.Checked += (_, _) => OnSettingChanged();
        UseCoverAccentColorCheck.Unchecked += (_, _) => OnSettingChanged();
        EnableSmtcTimelineMonitorCheck.Checked += (_, _) => OnSettingChanged();
        EnableSmtcTimelineMonitorCheck.Unchecked += (_, _) => OnSettingChanged();

        FontSizeStepper.ValueChanged += (_, _) => OnSettingChanged();
        LineGapStepper.ValueChanged += (_, _) => OnSettingChanged();
        LineGapOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        AutoAdjustLineGapCheck.Checked += (_, _) => OnAutoAdjustLineGapChanged();
        AutoAdjustLineGapCheck.Unchecked += (_, _) => OnAutoAdjustLineGapChanged();
        BackgroundOpacityStepper.ValueChanged += (_, _) => OnSettingChanged();
        WindowWidthStepper.ValueChanged += (_, _) => OnSettingChanged();
        WindowWidthOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        WindowHeightStepper.ValueChanged += (_, _) => OnSettingChanged();
        WindowHeightOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        XOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        YOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        AutoAdjustWindowWidthCheck.Checked += (_, _) => OnAutoAdjustWindowWidthChanged();
        AutoAdjustWindowWidthCheck.Unchecked += (_, _) => OnAutoAdjustWindowWidthChanged();
        AutoAdjustWindowHeightCheck.Checked += (_, _) => OnAutoAdjustWindowHeightChanged();
        AutoAdjustWindowHeightCheck.Unchecked += (_, _) => OnAutoAdjustWindowHeightChanged();

        FontFamilyCombo.SelectionChanged += (_, _) => OnSettingChanged();
        FontWeightCombo.SelectionChanged += (_, _) => OnSettingChanged();
        ForegroundModeCombo.SelectionChanged += (_, _) => OnForegroundModeChanged();
        CoverStyleCombo.SelectionChanged += (_, _) => OnSettingChanged();
        CoverSizeStepper.ValueChanged += (_, _) => OnSettingChanged();
        CoverLayoutCombo.SelectionChanged += (_, _) => OnSettingChanged();
        ShowStackedTrackInfoCheck.Checked += (_, _) => OnSettingChanged();
        ShowStackedTrackInfoCheck.Unchecked += (_, _) => OnSettingChanged();
        StackedTrackInfoGapStepper.ValueChanged += (_, _) => OnSettingChanged();
        StackedCoverGapStepper.ValueChanged += (_, _) => OnSettingChanged();
        StackedCoverXOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        StackedCoverYOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        StackedContentXOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        StackedContentYOffsetStepper.ValueChanged += (_, _) => OnSettingChanged();
        TransitionStyleCombo.SelectionChanged += (_, _) => OnSettingChanged();
        BackgroundMaterialCombo.SelectionChanged += (_, _) => OnSettingChanged();
        HorizontalAnchorCombo.SelectionChanged += (_, _) => OnSettingChanged();
        SourceOrderList.OrderChanged += (_, _) => OnSettingChanged();

        SpectrumTuningButton.Click += (_, _) =>
        {
            if (System.Windows.Application.Current is App app)
            {
                app.OpenSpectrumTuningWindow();
            }
        };
        ClearCacheButton.Click += (_, _) => ClearLyricCache();
        RefreshLyricDiagnosticsButton.Click += (_, _) => RefreshLyricDiagnostics();
        RematchLyricsButton.Click += (_, _) => RematchLyrics();
        ResetDefaultsButton.Click += (_, _) => ResetDefaults();
        SidebarToggleButton.Click += (_, _) => ToggleSidebar();
    }

    private void ToggleSidebar()
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? 72 : 248);
        BrandTitle.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarToggleButton.RenderTransform = new RotateTransform(_sidebarCollapsed ? 180 : 0);
        SidebarToggleButton.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingNavFromScroll)
        {
            return;
        }

        if (sender is not System.Windows.Controls.RadioButton button)
        {
            return;
        }

        var target = button.Name switch
        {
            nameof(NavPlayers) => SectionPlayers,
            nameof(NavLyrics) => SectionLyrics,
            nameof(NavAppearance) => SectionAppearance,
            nameof(NavLayout) => SectionLayout,
            nameof(NavDebug) => SectionDebug,
            nameof(NavAbout) => SectionAbout,
            _ => null
        };

        if (target is null)
        {
            return;
        }

        if (ReferenceEquals(target, SectionAppearance))
        {
            EnsureFontOptionsPopulated();
        }

        var offset = target.TransformToAncestor(ContentScroll).Transform(new System.Windows.Point(0, 0)).Y;
        ContentScroll.ScrollToVerticalOffset(Math.Max(0, offset - 8));
    }

    private void ContentScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0)
        {
            return;
        }

        var viewportTop = ContentScroll.VerticalOffset + 120;
        var sections = new (FrameworkElement Element, System.Windows.Controls.RadioButton Nav)[]
        {
            (SectionPlayers, NavPlayers),
            (SectionLyrics, NavLyrics),
            (SectionAppearance, NavAppearance),
            (SectionLayout, NavLayout),
            (SectionDebug, NavDebug),
            (SectionAbout, NavAbout)
        };

        var active = sections
            .Select(section =>
            {
                var top = section.Element.TransformToAncestor(ContentScroll).Transform(new System.Windows.Point(0, 0)).Y;
                return (section, Distance: Math.Abs(top - viewportTop));
            })
            .OrderBy(x => x.Distance)
            .FirstOrDefault();

        if (active.section.Nav is null || active.section.Nav.IsChecked == true)
        {
            return;
        }

        if (ReferenceEquals(active.section.Element, SectionAppearance))
        {
            EnsureFontOptionsPopulated();
        }

        _isUpdatingNavFromScroll = true;
        try
        {
            active.section.Nav.IsChecked = true;
        }
        finally
        {
            _isUpdatingNavFromScroll = false;
        }
    }

    private void PopulateFontOptions()
    {
        var options = new List<FontOption>
        {
            new()
            {
                Label = AppSettings.BundledFontFamily,
                Value = AppSettings.DefaultFontFamily
            }
        };
        options.AddRange(GetFontOptions());

        FontFamilyCombo.ItemsSource = options;
        FontFamilyCombo.DisplayMemberPath = nameof(FontOption.Label);
        FontFamilyCombo.SelectedValuePath = nameof(FontOption.Value);
    }

    private void EnsureFontOptionsPopulated()
    {
        if (_fontOptionsPopulated)
        {
            return;
        }

        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            _fontOptionsPopulated = true;
            PopulateFontOptions();
            SelectFontFamily(_settings.FontFamily);
        }
        finally
        {
            _isLoading = wasLoading;
        }
    }

    private void LoadFromSettings()
    {
        _isLoading = true;
        try
        {
            EnableQQMusicCheck.IsChecked = _settings.EnableQQMusic;
            EnableNeteaseCheck.IsChecked = _settings.EnableNetease;
            EnableKugouCheck.IsChecked = _settings.EnableKugou;
            EnableSpotifyCheck.IsChecked = _settings.EnableSpotify;
            ShowLyricsOnStartupCheck.IsChecked = _settings.ShowLyricsOnStartup;
            StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
            AutoShowLyricsWhenPlayerOpensCheck.IsChecked = _settings.AutoShowLyricsWhenPlayerOpens;
            AutoHideLyricsWhenPlayerClosesCheck.IsChecked = _settings.AutoHideLyricsWhenPlayerCloses;
            ShowLyricTranslationCheck.IsChecked = _settings.ShowLyricTranslation;
            EnablePureMusicSpectrumCheck.IsChecked = _settings.EnablePureMusicSpectrum;
            EnableSpectrumCheck.IsChecked = _settings.EnableSpectrum;
            ShowSpectrumWhenLyricsNotFoundCheck.IsChecked = _settings.ShowSpectrumWhenLyricsNotFound;
            ShowSpectrumWhenLyricsAvailableCheck.IsChecked = _settings.ShowSpectrumWhenLyricsAvailable;
            SelectComboByTag(SpectrumStyleCombo, _settings.SpectrumStyle.ToString());
            LyricOffsetStepper.Value = _settings.LyricOffsetMs;
            QqMusicLyricOffsetStepper.Value = _settings.QqMusicLyricOffsetMs;
            NeteaseLyricOffsetStepper.Value = _settings.NeteaseLyricOffsetMs;
            KugouLyricOffsetStepper.Value = _settings.KugouLyricOffsetMs;
            SpotifyLyricOffsetStepper.Value = _settings.SpotifyLyricOffsetMs;
            EnableLocalLyricsCheck.IsChecked = _settings.EnableLocalLyrics;
            SelectComboByTag(LocalLyricsModeCombo, _settings.LocalLyricsSearchMode.ToString());
            SelectComboByTag(LocalCoverModeCombo, _settings.LocalCoverSearchMode.ToString());
            ShowCoverImageCheck.IsChecked = _settings.ShowCoverImage;
            LocalMusicFoldersBox.Text = string.Join(Environment.NewLine, _settings.LocalMusicFolders);
            ShowBackgroundCheck.IsChecked = _settings.ShowBackground;
            ShowBorderCheck.IsChecked = _settings.ShowBorder;
            ShowTextShadowCheck.IsChecked = _settings.ShowTextShadow;
            UseCoverAccentColorCheck.IsChecked = _settings.UseCoverAccentColor;
            EnableSmtcTimelineMonitorCheck.IsChecked = _settings.EnableSmtcTimelineMonitor;

            FontSizeStepper.Value = _settings.FontSize;
            LineGapStepper.Value = _settings.LineGap;
            LineGapOffsetStepper.Value = _settings.LineGapOffset;
            AutoAdjustLineGapCheck.IsChecked = _settings.AutoAdjustLineGap;
            BackgroundOpacityStepper.Value = _settings.BackgroundOpacity;
            WindowWidthStepper.Maximum = WindowWidthLimits.GetMaxForScreen();
            WindowWidthStepper.Value = Math.Min(_settings.WindowWidth, WindowWidthStepper.Maximum);
            WindowWidthOffsetStepper.Value = _settings.WindowWidthOffset;
            AutoAdjustWindowWidthCheck.IsChecked = _settings.AutoAdjustWindowWidth;
            WindowHeightStepper.Value = _settings.WindowHeight;
            WindowHeightOffsetStepper.Value = _settings.WindowHeightOffset;
            AutoAdjustWindowHeightCheck.IsChecked = _settings.AutoAdjustWindowHeight;
            XOffsetStepper.Value = _settings.XOffset;
            YOffsetStepper.Value = _settings.YOffset;
            ForceAlwaysOnTopCheck.IsChecked = _settings.ForceAlwaysOnTop;
            AutoCheckUpdatesCheck.IsChecked = _settings.AutoCheckUpdates;

            SelectComboByTag(FontWeightCombo, NormalizeFontWeight(_settings.FontWeight));
            SelectComboByTag(HorizontalAnchorCombo, _settings.HorizontalAnchor.ToString());
            SelectComboByTag(CoverStyleCombo, NormalizeCoverStyle(_settings.CoverStyle).ToString());
            CoverSizeStepper.Value = _settings.CoverSize;
            SelectComboByTag(CoverLayoutCombo, _settings.CoverLayoutMode.ToString());
            ShowStackedTrackInfoCheck.IsChecked = _settings.ShowStackedTrackInfo;
            StackedTrackInfoGapStepper.Value = _settings.StackedTrackInfoGap;
            StackedCoverGapStepper.Value = _settings.StackedCoverLyricsGap;
            StackedCoverXOffsetStepper.Value = _settings.StackedCoverXOffset;
            StackedCoverYOffsetStepper.Value = _settings.StackedCoverYOffset;
            StackedContentXOffsetStepper.Value = _settings.StackedContentXOffset;
            StackedContentYOffsetStepper.Value = _settings.StackedContentYOffset;
            SelectComboByTag(TransitionStyleCombo, _settings.TransitionStyle.ToString());
            SelectComboByTag(BackgroundMaterialCombo, _settings.BackgroundMaterial.ToString());
            if (_fontOptionsPopulated)
            {
                SelectFontFamily(_settings.FontFamily);
            }
            SelectComboByTag(ForegroundModeCombo, _settings.ForegroundColorMode.ToString());

            SourceOrderList.SetOrder(NormalizeSourceOrder(_settings.SourceRecognitionOrder));
            UpdateColorUi();
            UpdateLineGapControlsState();
            UpdateWindowWidthControlsState();
            UpdateWindowHeightControlsState();
            UpdateDependentControlsState();
            RefreshLocalFoldersStatus();
            RefreshLyricDiagnostics();
            RenderAbout();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnForegroundModeChanged()
    {
        if (_isLoading)
        {
            return;
        }

        var tag = (ForegroundModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Light";
        if (!Enum.TryParse<ForegroundColorMode>(tag, out var mode))
        {
            return;
        }

        _settings.ForegroundColorMode = mode;
        if (mode == ForegroundColorMode.Custom)
        {
            PickForegroundColor();
            return;
        }

        _settings.ForegroundColor = mode == ForegroundColorMode.Dark
            ? AppSettings.DarkForegroundColor
            : AppSettings.LightForegroundColor;
        UpdateColorUi();
        SaveSettings();
    }

    private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ForegroundColorMode != ForegroundColorMode.Custom)
        {
            return;
        }

        PickForegroundColor();
    }

    private void PickForegroundColor()
    {
        using var dialog = new Forms.ColorDialog { FullOpen = true };
        if (TryParseMediaColor(_settings.ForegroundColor, out var currentColor))
        {
            dialog.Color = Drawing.Color.FromArgb(currentColor.R, currentColor.G, currentColor.B);
        }

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        _settings.ForegroundColor = $"#FF{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        _settings.ForegroundColorMode = ForegroundColorMode.Custom;
        SelectComboByTag(ForegroundModeCombo, "Custom");
        UpdateColorUi();
        SaveSettings();
    }

    private void UpdateColorUi()
    {
        if (TryParseMediaColor(_settings.ForegroundColor, out var color))
        {
            ColorSwatch.Background = new SolidColorBrush(color);
        }

        ColorValueText.Text = _settings.ForegroundColor;
        ColorPickerButton.IsEnabled = _settings.ForegroundColorMode == ForegroundColorMode.Custom;
    }

    private void OnAutoAdjustLineGapChanged()
    {
        UpdateLineGapControlsState();
        OnSettingChanged();
    }

    private void UpdateLineGapControlsState()
    {
        var autoAdjust = AutoAdjustLineGapCheck.IsChecked == true;
        LineGapStepper.IsEnabled = !autoAdjust;
        LineGapOffsetStepper.IsEnabled = autoAdjust;
        LineGapLabel.Opacity = autoAdjust ? 0.45 : 1;
        LineGapHint.Opacity = autoAdjust ? 0.45 : 1;
        LineGapOffsetLabel.Opacity = autoAdjust ? 1 : 0.45;
        LineGapOffsetHint.Opacity = autoAdjust ? 1 : 0.45;
    }

    private void OnAutoAdjustWindowWidthChanged()
    {
        UpdateWindowWidthControlsState();
        OnSettingChanged();
    }

    private void UpdateWindowWidthControlsState()
    {
        var autoAdjust = AutoAdjustWindowWidthCheck.IsChecked == true;
        WindowWidthStepper.IsEnabled = !autoAdjust;
        WindowWidthOffsetStepper.IsEnabled = autoAdjust;
        WindowWidthLabel.Opacity = autoAdjust ? 0.45 : 1;
        WindowWidthHint.Opacity = autoAdjust ? 0.45 : 1;
        WindowWidthOffsetLabel.Opacity = autoAdjust ? 1 : 0.45;
        WindowWidthOffsetHint.Opacity = autoAdjust ? 1 : 0.45;
    }

    private void OnAutoAdjustWindowHeightChanged()
    {
        UpdateWindowHeightControlsState();
        OnSettingChanged();
    }

    private void UpdateWindowHeightControlsState()
    {
        var autoAdjust = AutoAdjustWindowHeightCheck.IsChecked == true;
        WindowHeightStepper.IsEnabled = !autoAdjust;
        WindowHeightOffsetStepper.IsEnabled = autoAdjust;
        WindowHeightLabel.Opacity = autoAdjust ? 0.45 : 1;
        WindowHeightHint.Opacity = autoAdjust ? 0.45 : 1;
        WindowHeightOffsetLabel.Opacity = autoAdjust ? 1 : 0.45;
        WindowHeightOffsetHint.Opacity = autoAdjust ? 1 : 0.45;
    }

    private void UpdateDependentControlsState()
    {
        var spectrumEnabled = EnableSpectrumCheck.IsChecked == true;
        EnablePureMusicSpectrumCheck.IsEnabled = spectrumEnabled;
        ShowSpectrumWhenLyricsNotFoundCheck.IsEnabled = spectrumEnabled;
        ShowSpectrumWhenLyricsAvailableCheck.IsEnabled = spectrumEnabled;
        ShowSpectrumWhenLyricsAvailableLabel.Opacity = spectrumEnabled ? 1 : 0.45;
        ShowSpectrumWhenLyricsAvailableHint.Opacity = spectrumEnabled ? 1 : 0.45;
        SpectrumStyleCombo.IsEnabled = spectrumEnabled;
        SpectrumStyleLabel.Opacity = spectrumEnabled ? 1 : 0.45;
        SpectrumStyleHint.Opacity = spectrumEnabled ? 1 : 0.45;
        SpectrumTuningHint.Visibility = spectrumEnabled ? Visibility.Collapsed : Visibility.Visible;

        var localLyricsEnabled = EnableLocalLyricsCheck.IsChecked == true;
        var coverEnabled = ShowCoverImageCheck.IsChecked == true;
        LocalMusicFoldersBox.IsEnabled = localLyricsEnabled;
        AddLocalFolderButton.IsEnabled = localLyricsEnabled;
        LocalLyricsModeCombo.IsEnabled = localLyricsEnabled;
        LocalCoverModeCombo.IsEnabled = localLyricsEnabled && coverEnabled;
        CoverStyleCombo.IsEnabled = coverEnabled;
        var coverStyleTag = (CoverStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "RoundedSquare";
        var coverVisible = coverEnabled && !string.Equals(coverStyleTag, nameof(CoverDisplayStyle.Hidden), StringComparison.OrdinalIgnoreCase);
        var coverLayoutTag = (CoverLayoutCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Inline";
        var stackedCoverLayout = coverVisible &&
            string.Equals(coverLayoutTag, nameof(CoverLayoutMode.Stacked), StringComparison.OrdinalIgnoreCase);
        var stackedTrackInfoEnabled = stackedCoverLayout && ShowStackedTrackInfoCheck.IsChecked == true;
        CoverSizeStepper.IsEnabled = coverVisible;
        CoverLayoutCombo.IsEnabled = coverVisible;
        ShowStackedTrackInfoCheck.IsEnabled = stackedCoverLayout;
        StackedTrackInfoGapStepper.IsEnabled = stackedTrackInfoEnabled;
        StackedCoverGapStepper.IsEnabled = stackedCoverLayout;
        StackedCoverXOffsetStepper.IsEnabled = stackedCoverLayout;
        StackedCoverYOffsetStepper.IsEnabled = stackedCoverLayout;
        StackedContentXOffsetStepper.IsEnabled = stackedCoverLayout;
        StackedContentYOffsetStepper.IsEnabled = stackedCoverLayout;
        CoverSizeLabel.Opacity = coverVisible ? 1 : 0.45;
        CoverSizeHint.Opacity = coverVisible ? 1 : 0.45;
        CoverLayoutLabel.Opacity = coverVisible ? 1 : 0.45;
        CoverLayoutHint.Opacity = coverVisible ? 1 : 0.45;
        ShowStackedTrackInfoLabel.Opacity = stackedCoverLayout ? 1 : 0.45;
        ShowStackedTrackInfoHint.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedTrackInfoGapLabel.Opacity = stackedTrackInfoEnabled ? 1 : 0.45;
        StackedTrackInfoGapHint.Opacity = stackedTrackInfoEnabled ? 1 : 0.45;
        StackedCoverGapLabel.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedCoverGapHint.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedCoverXOffsetLabel.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedCoverXOffsetHint.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedCoverYOffsetLabel.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedCoverYOffsetHint.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedContentXOffsetLabel.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedContentXOffsetHint.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedContentYOffsetLabel.Opacity = stackedCoverLayout ? 1 : 0.45;
        StackedContentYOffsetHint.Opacity = stackedCoverLayout ? 1 : 0.45;
        LocalLyricsModeLabel.Opacity = localLyricsEnabled ? 1 : 0.45;
        LocalLyricsModeHint.Opacity = localLyricsEnabled ? 1 : 0.45;
        LocalCoverModeLabel.Opacity = localLyricsEnabled && coverEnabled ? 1 : 0.45;
        LocalCoverModeHint.Opacity = localLyricsEnabled && coverEnabled ? 1 : 0.45;
        LocalFoldersStatusText.Opacity = localLyricsEnabled ? 1 : 0.48;

        var backgroundEnabled = ShowBackgroundCheck.IsChecked == true;
        BackgroundOpacityStepper.IsEnabled = backgroundEnabled;
        BackgroundMaterialCombo.IsEnabled = backgroundEnabled;
        BackgroundMaterialLabel.Opacity = backgroundEnabled ? 1 : 0.45;
        BackgroundMaterialHint.Opacity = backgroundEnabled ? 1 : 0.45;

        var startupHiddenByAutoHide =
            ShowLyricsOnStartupCheck.IsChecked == true &&
            AutoHideLyricsWhenPlayerClosesCheck.IsChecked == true;
        StartupVisibilityHintRow.Visibility = startupHiddenByAutoHide ? Visibility.Visible : Visibility.Collapsed;
        StartupVisibilityHint.Text = startupHiddenByAutoHide
            ? "已同时开启“启动时显示歌词”和“播放器关闭时隐藏”。启动阶段会先保持隐藏，检测到播放器播放后再自动显示。"
            : string.Empty;
    }

    private void OnSettingChanged()
    {
        if (_isLoading)
        {
            return;
        }

        _settings.EnableQQMusic = EnableQQMusicCheck.IsChecked == true;
        _settings.EnableNetease = EnableNeteaseCheck.IsChecked == true;
        _settings.EnableKugou = EnableKugouCheck.IsChecked == true;
        _settings.EnableSpotify = EnableSpotifyCheck.IsChecked == true;
        _settings.ShowLyricsOnStartup = ShowLyricsOnStartupCheck.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.AutoShowLyricsWhenPlayerOpens = AutoShowLyricsWhenPlayerOpensCheck.IsChecked == true;
        _settings.AutoHideLyricsWhenPlayerCloses = AutoHideLyricsWhenPlayerClosesCheck.IsChecked == true;
        _settings.ShowLyricTranslation = ShowLyricTranslationCheck.IsChecked == true;
        _settings.EnablePureMusicSpectrum = EnablePureMusicSpectrumCheck.IsChecked == true;
        _settings.EnableSpectrum = EnableSpectrumCheck.IsChecked == true;
        _settings.ShowSpectrumWhenLyricsNotFound = ShowSpectrumWhenLyricsNotFoundCheck.IsChecked == true;
        _settings.ShowSpectrumWhenLyricsAvailable = ShowSpectrumWhenLyricsAvailableCheck.IsChecked == true;
        var spectrumStyleTag = (SpectrumStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Center";
        _settings.SpectrumStyle = Enum.TryParse<SpectrumDisplayStyle>(spectrumStyleTag, out var spectrumStyle)
            ? spectrumStyle
            : SpectrumDisplayStyle.Center;
        _settings.LyricOffsetMs = (int)Math.Round(LyricOffsetStepper.Value);
        _settings.QqMusicLyricOffsetMs = (int)Math.Round(QqMusicLyricOffsetStepper.Value);
        _settings.NeteaseLyricOffsetMs = (int)Math.Round(NeteaseLyricOffsetStepper.Value);
        _settings.KugouLyricOffsetMs = (int)Math.Round(KugouLyricOffsetStepper.Value);
        _settings.SpotifyLyricOffsetMs = (int)Math.Round(SpotifyLyricOffsetStepper.Value);
        _settings.EnableLocalLyrics = EnableLocalLyricsCheck.IsChecked == true;
        var localLyricsModeTag = (LocalLyricsModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "PreferLocal";
        _settings.LocalLyricsSearchMode = Enum.TryParse<LocalLyricsSearchMode>(localLyricsModeTag, out var localLyricsMode)
            ? localLyricsMode
            : LocalLyricsSearchMode.PreferLocal;
        var localCoverModeTag = (LocalCoverModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "OnlineFirst";
        _settings.LocalCoverSearchMode = Enum.TryParse<LocalCoverSearchMode>(localCoverModeTag, out var localCoverMode)
            ? localCoverMode
            : LocalCoverSearchMode.OnlineFirst;
        _settings.ShowCoverImage = ShowCoverImageCheck.IsChecked == true;
        _settings.LocalMusicFolders = NormalizeLocalMusicFolders(LocalMusicFoldersBox.Text);
        _settings.ForceAlwaysOnTop = ForceAlwaysOnTopCheck.IsChecked == true;
        _settings.AutoCheckUpdates = AutoCheckUpdatesCheck.IsChecked == true;
        _settings.ShowBackground = ShowBackgroundCheck.IsChecked == true;
        _settings.ShowBorder = ShowBorderCheck.IsChecked == true;
        _settings.ShowTextShadow = ShowTextShadowCheck.IsChecked == true;
        _settings.UseCoverAccentColor = UseCoverAccentColorCheck.IsChecked == true;
        _settings.EnableSmtcTimelineMonitor = EnableSmtcTimelineMonitorCheck.IsChecked == true;

        _settings.FontSize = FontSizeStepper.Value;
        _settings.AutoAdjustLineGap = AutoAdjustLineGapCheck.IsChecked == true;
        _settings.LineGap = LineGapStepper.Value;
        _settings.LineGapOffset = LineGapOffsetStepper.Value;
        _settings.BackgroundOpacity = BackgroundOpacityStepper.Value;
        _settings.WindowWidth = WindowWidthStepper.Value;
        _settings.WindowWidthOffset = WindowWidthOffsetStepper.Value;
        _settings.AutoAdjustWindowWidth = AutoAdjustWindowWidthCheck.IsChecked == true;
        _settings.WindowHeight = WindowHeightStepper.Value;
        _settings.WindowHeightOffset = WindowHeightOffsetStepper.Value;
        _settings.AutoAdjustWindowHeight = AutoAdjustWindowHeightCheck.IsChecked == true;
        _settings.XOffset = XOffsetStepper.Value;
        _settings.YOffset = YOffsetStepper.Value;
        _settings.FontWeight = (FontWeightCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "SemiBold";
        _settings.FontFamily = _fontOptionsPopulated && FontFamilyCombo.SelectedValue is string selectedFont
            ? selectedFont
            : _settings.FontFamily;
        _settings.SourceRecognitionOrder = NormalizeSourceOrder(SourceOrderList.GetOrder());

        var coverStyleTag = (CoverStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "RoundedSquare";
        _settings.CoverStyle = Enum.TryParse<CoverDisplayStyle>(coverStyleTag, out var coverStyle)
            ? NormalizeCoverStyle(coverStyle)
            : CoverDisplayStyle.RoundedSquare;
        _settings.CoverSize = CoverSizeStepper.Value;
        var coverLayoutTag = (CoverLayoutCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Inline";
        _settings.CoverLayoutMode = Enum.TryParse<CoverLayoutMode>(coverLayoutTag, out var coverLayoutMode)
            ? coverLayoutMode
            : CoverLayoutMode.Inline;
        _settings.ShowStackedTrackInfo = ShowStackedTrackInfoCheck.IsChecked == true;
        _settings.StackedTrackInfoGap = StackedTrackInfoGapStepper.Value;
        _settings.StackedCoverLyricsGap = StackedCoverGapStepper.Value;
        _settings.StackedCoverXOffset = StackedCoverXOffsetStepper.Value;
        _settings.StackedCoverYOffset = StackedCoverYOffsetStepper.Value;
        _settings.StackedContentXOffset = StackedContentXOffsetStepper.Value;
        _settings.StackedContentYOffset = StackedContentYOffsetStepper.Value;
        var transitionStyleTag = (TransitionStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Slide";
        _settings.TransitionStyle = Enum.TryParse<LyricTransitionStyle>(transitionStyleTag, out var transitionStyle)
            ? transitionStyle
            : LyricTransitionStyle.Slide;
        var backgroundMaterialTag = (BackgroundMaterialCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Dim";
        _settings.BackgroundMaterial = Enum.TryParse<LyricsBackgroundMaterial>(backgroundMaterialTag, out var backgroundMaterial)
            ? backgroundMaterial
            : LyricsBackgroundMaterial.Dim;

        var anchorTag = (HorizontalAnchorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Left";
        _settings.HorizontalAnchor = Enum.TryParse<LyricsHorizontalAnchor>(anchorTag, out var anchor)
            ? anchor
            : LyricsHorizontalAnchor.Left;

        UpdateDependentControlsState();
        QueueSaveSettings();
    }

    private void SaveSettings()
    {
        if (System.Windows.Application.Current is App app)
        {
            app.SaveSettings(_settings.Clone());
        }
    }

    private void QueueSaveSettings()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushPendingSettings()
    {
        _saveTimer.Stop();
        SaveSettings();
    }

    private void RenderAbout()
    {
        AppVersionText.Text = $"当前版本 {UpdateChecker.GetCurrentVersion()}";
        if (!_isCheckingUpdate)
        {
            UpdateStatusText.Text = string.Empty;
        }

        UpdateInstallButtonState(null);
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdate)
        {
            return;
        }

        _isCheckingUpdate = true;
        _latestUpdateResult = null;
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        CheckUpdateButton.Content = "检查中...";
        UpdateStatusText.Text = "正在检查更新...";

        try
        {
            var result = await UpdateChecker.CheckLatestAsync();
            _latestUpdateResult = result;
            _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _settings.AutoCheckUpdates = AutoCheckUpdatesCheck.IsChecked == true;

            if (result.State == UpdateCheckState.Error && !string.IsNullOrWhiteSpace(result.Message))
            {
                UpdateStatusText.Text = result.Message;
            }
            else
            {
                UpdateStatusText.Text = result.State switch
                {
                    UpdateCheckState.Available when result.Asset is not null =>
                        $"发现新版本 {result.Version}，当前版本 {result.CurrentVersion}。可下载并安装：{result.Asset.Name}",
                    UpdateCheckState.Available =>
                        $"发现新版本 {result.Version}，当前版本 {result.CurrentVersion}，但未找到 Light 版 zip 更新包，请打开 GitHub 发布页手动下载。",
                    UpdateCheckState.Latest =>
                        $"已是最新版本（{result.CurrentVersion}）。",
                    _ => "检查更新失败，请稍后重试。"
                };
            }

            UpdateInstallButtonState(result);
            FlushPendingSettings();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            UpdateStatusText.Text = "检查更新失败，请检查网络连接。";
        }
        finally
        {
            _isCheckingUpdate = false;
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButton.Content = "检查更新";
            UpdateInstallButtonState(_latestUpdateResult);
        }
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstallingUpdate || _latestUpdateResult?.Asset is null)
        {
            return;
        }

        _isInstallingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        InstallUpdateButton.Content = "准备中...";

        try
        {
            var progress = new Progress<UpdateInstallProgress>(state =>
            {
                UpdateStatusText.Text = state.Percent is { } percent
                    ? $"{state.Message}（{percent:P0}）"
                    : state.Message;
            });

            var update = await UpdateInstaller.DownloadAndPrepareAsync(_latestUpdateResult, progress);
            UpdateStatusText.Text = "更新包已下载，即将退出并覆盖安装。";
            InstallUpdateButton.Content = "即将安装...";
            FlushPendingSettings();

            UpdateInstaller.LaunchInstaller(update);
            if (System.Windows.Application.Current is App app)
            {
                app.ShutdownForUpdate();
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = "更新已取消。";
            ResetInstallUpdateUi();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            UpdateStatusText.Text = "已取消管理员授权，未安装更新。";
            ResetInstallUpdateUi();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or JsonException)
        {
            UpdateStatusText.Text = $"安装更新失败：{ex.Message}";
            ResetInstallUpdateUi();
        }
    }

    private void UpdateInstallButtonState(UpdateCheckResult? result)
    {
        var canInstall = result?.HasUpdate == true && result.Asset is not null && !_isCheckingUpdate && !_isInstallingUpdate;
        InstallUpdateButton.Visibility = result?.HasUpdate == true ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = canInstall;
        InstallUpdateButton.Content = result?.Asset is null ? "暂无可安装包" : "下载并安装更新";
    }

    private void ResetInstallUpdateUi()
    {
        _isInstallingUpdate = false;
        CheckUpdateButton.IsEnabled = true;
        CheckUpdateButton.Content = "检查更新";
        UpdateInstallButtonState(_latestUpdateResult);
    }

    private void OpenRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = UpdateChecker.RepositoryUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            UpdateStatusText.Text = "无法打开浏览器。";
        }
    }

    private static List<string> NormalizeLocalMusicFolders(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Trim().Trim('"'))
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddLocalMusicFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择本地音乐目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK ||
            string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        var folders = NormalizeLocalMusicFolders(LocalMusicFoldersBox.Text);
        if (!folders.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(dialog.SelectedPath);
        }

        LocalMusicFoldersBox.Text = string.Join(Environment.NewLine, folders);
        OnSettingChanged();
    }

    private void AddLocalMusicFolderFromExplorer()
    {
        var folders = NormalizeLocalMusicFolders(LocalMusicFoldersBox.Text);
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择本地音乐目录",
            Multiselect = true
        };

        var initialDirectory = folders.FirstOrDefault(Directory.Exists);
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var selectedPath in dialog.FolderNames.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!folders.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(selectedPath);
            }
        }

        LocalMusicFoldersBox.Text = string.Join(Environment.NewLine, folders);
        OnSettingChanged();
    }

    private void RefreshLocalFoldersStatus()
    {
        var folders = NormalizeLocalMusicFolders(LocalMusicFoldersBox.Text);
        var existingFolders = folders.Where(Directory.Exists).ToArray();
        var missingCount = folders.Count - existingFolders.Length;

        _localFoldersStatusCancellation?.Cancel();
        _localFoldersStatusCancellation?.Dispose();
        _localFoldersStatusCancellation = null;

        if (folders.Count == 0)
        {
            LocalFoldersStatusText.Text = "未添加本地音乐目录。";
            return;
        }

        var prefix = missingCount == 0
            ? $"{folders.Count} 个目录均可访问"
            : $"{existingFolders.Length}/{folders.Count} 个目录可访问，{missingCount} 个不可访问";

        if (existingFolders.Length == 0)
        {
            LocalFoldersStatusText.Text = prefix;
            return;
        }

        LocalFoldersStatusText.Text = $"{prefix}，正在统计音频文件...";
        var cts = new CancellationTokenSource();
        _localFoldersStatusCancellation = cts;
        _ = CountLocalAudioFilesAsync(existingFolders, cts.Token).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (cts.IsCancellationRequested || !ReferenceEquals(_localFoldersStatusCancellation, cts))
                {
                    return;
                }

                var countText = task.Status == TaskStatus.RanToCompletion
                    ? $"，约 {task.Result} 首音频"
                    : string.Empty;
                LocalFoldersStatusText.Text = prefix + countText;
            });
        }, TaskScheduler.Default);
    }

    private static Task<int> CountLocalAudioFilesAsync(
        IReadOnlyCollection<string> folders,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var count = 0;
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var file in SafeEnumerateFiles(folder, cancellationToken))
                {
                    if (LocalAudioExtensions.Contains(Path.GetExtension(file)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }, cancellationToken);
    }

    private static IEnumerable<string> SafeEnumerateFiles(string rootFolder, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(rootFolder);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(folder);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    private static List<string> NormalizeLocalMusicFolders(IEnumerable<string>? folders)
    {
        return (folders ?? Enumerable.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ResetDefaults()
    {
        var defaults = new AppSettings();
        App.ApplyStartupForegroundColor(defaults);
        CopySettings(defaults, _settings);
        LoadFromSettings();
        FlushPendingSettings();
    }

    private void ClearLyricCache()
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ClearLyricCaches();
        }
        else
        {
            LyricProviderBase.ClearCache();
            GenericSmtcLyricProvider.ClearCache();
        }

        ClearCacheStatusText.Text = $"已清除缓存（{DateTime.Now:HH:mm:ss}）。";
    }

    private void RefreshLyricDiagnostics()
    {
        var snapshot = LyricResolveDiagnosticsState.Current;
        if (snapshot == LyricResolveDiagnosticsSnapshot.Empty ||
            snapshot.CapturedAtUtc == DateTimeOffset.MinValue)
        {
            LyricDiagnosticsText.Text = "暂无歌词匹配信息。";
            return;
        }

        var selectedSource = string.IsNullOrWhiteSpace(snapshot.SelectedSource)
            ? "未命中"
            : snapshot.SelectedSource;
        LyricDiagnosticsText.Text =
            $"歌曲：{snapshot.TrackTitle} - {snapshot.TrackArtist}\n" +
            $"播放器：{snapshot.TrackSourceApp}，歌词源：{selectedSource}，分数：{snapshot.BestScore}，行数：{snapshot.LineCount}\n" +
            $"位置：{FormatTimeSpan(snapshot.PlaybackPosition)}，偏移：{snapshot.AppliedOffsetMs} ms，行号：{snapshot.CurrentLineIndex}，进度：{snapshot.LineProgress:P0}\n" +
            $"候选：{snapshot.Candidates}";
    }

    private void RematchLyrics()
    {
        if (System.Windows.Application.Current is App app)
        {
            app.RematchCurrentLyrics();
        }

        RefreshLyricDiagnostics();
    }

    private static string FormatTimeSpan(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : string.Empty;
        var abs = value.Duration();
        return $"{sign}{abs:mm\\:ss\\.fff}";
    }

    private void SelectFontFamily(string? fontFamily)
    {
        var resolved = ResolveInstalledFontFamily(fontFamily) ?? AppSettings.DefaultFontFamily;
        FontFamilyCombo.SelectedValue = resolved;
    }

    private static void SelectComboByTag(System.Windows.Controls.ComboBox combo, string? tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem comboItem &&
                string.Equals(comboItem.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static List<string> NormalizeSourceOrder(IEnumerable<string>? order)
    {
        var known = new[] { "QQMusic", "Netease", "Kugou", "Spotify" };
        var result = new List<string>();
        foreach (var source in order ?? Enumerable.Empty<string>())
        {
            if (known.Contains(source) && !result.Contains(source))
            {
                result.Add(source);
            }
        }

        foreach (var source in known)
        {
            if (!result.Contains(source))
            {
                result.Add(source);
            }
        }

        return result;
    }

    private static string NormalizeFontWeight(string? value) => value?.Trim() switch
    {
        "Light" => "Light",
        "Normal" => "Normal",
        "Medium" => "Medium",
        "SemiBold" => "SemiBold",
        "Bold" => "Bold",
        _ => "SemiBold"
    };

    private static CoverDisplayStyle NormalizeCoverStyle(CoverDisplayStyle style) =>
        style == CoverDisplayStyle.Large ? CoverDisplayStyle.RoundedSquare : style;

    private static List<FontOption> GetFontOptions()
    {
        var fonts = Fonts.SystemFontFamilies
            .Select(x => new FontOption
            {
                Value = x.Source,
                Label = GetLocalizedFontName(x)
            })
            .Where(x => !x.Label.Contains(AppSettings.BundledFontFamily, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return fonts;
    }

    private static string GetLocalizedFontName(Media.FontFamily fontFamily)
    {
        var languages = new[]
        {
            XmlLanguage.GetLanguage("zh-CN"),
            XmlLanguage.GetLanguage("zh-Hans"),
            XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag),
            XmlLanguage.GetLanguage("en-US")
        };

        foreach (var language in languages)
        {
            if (fontFamily.FamilyNames.TryGetValue(language, out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return fontFamily.FamilyNames.Values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? fontFamily.Source;
    }

    private string? ResolveInstalledFontFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return null;
        }

        var fonts = FontFamilyCombo.Items.OfType<FontOption>().ToList();
        if (fonts.Count == 0)
        {
            fonts =
            [
                new FontOption
                {
                    Label = AppSettings.BundledFontFamily,
                    Value = AppSettings.DefaultFontFamily
                },
                .. GetFontOptions()
            ];
        }

        var byValue = fonts.ToDictionary(x => x.Value, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var byLabel = fonts
            .GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in fontFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (byValue.TryGetValue(candidate, out var value) ||
                byLabel.TryGetValue(candidate, out value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryParseMediaColor(string? color, out Media.Color parsedColor)
    {
        parsedColor = Colors.White;
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

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.SourceRecognitionOrder = source.SourceRecognitionOrder.ToList();
        target.EnableNetease = source.EnableNetease;
        target.EnableQQMusic = source.EnableQQMusic;
        target.EnableKugou = source.EnableKugou;
        target.EnableSpotify = source.EnableSpotify;
        target.ShowLyricsOnStartup = source.ShowLyricsOnStartup;
        target.StartWithWindows = source.StartWithWindows;
        target.AutoShowLyricsWhenPlayerOpens = source.AutoShowLyricsWhenPlayerOpens;
        target.AutoHideLyricsWhenPlayerCloses = source.AutoHideLyricsWhenPlayerCloses;
        target.ShowLyricTranslation = source.ShowLyricTranslation;
        target.AutoCheckUpdates = source.AutoCheckUpdates;
        target.LastUpdateCheckUtc = source.LastUpdateCheckUtc;
        target.LastNotifiedUpdateVersion = source.LastNotifiedUpdateVersion;
        target.EnableSpectrum = source.EnableSpectrum;
        target.EnablePureMusicSpectrum = source.EnablePureMusicSpectrum;
        target.ShowSpectrumWhenLyricsNotFound = source.ShowSpectrumWhenLyricsNotFound;
        target.ShowSpectrumWhenLyricsAvailable = source.ShowSpectrumWhenLyricsAvailable;
        target.SpectrumStyle = source.SpectrumStyle;
        target.LyricOffsetMs = source.LyricOffsetMs;
        target.QqMusicLyricOffsetMs = source.QqMusicLyricOffsetMs;
        target.NeteaseLyricOffsetMs = source.NeteaseLyricOffsetMs;
        target.KugouLyricOffsetMs = source.KugouLyricOffsetMs;
        target.SpotifyLyricOffsetMs = source.SpotifyLyricOffsetMs;
        target.EnableLocalLyrics = source.EnableLocalLyrics;
        target.LocalLyricsSearchMode = source.LocalLyricsSearchMode;
        target.LocalCoverSearchMode = source.LocalCoverSearchMode;
        target.ShowCoverImage = source.ShowCoverImage;
        target.LocalMusicFolders = NormalizeLocalMusicFolders(source.LocalMusicFolders);
        target.FontSize = source.FontSize;
        target.AutoAdjustLineGap = source.AutoAdjustLineGap;
        target.LineGap = source.LineGap;
        target.LineGapOffset = source.LineGapOffset;
        target.FontFamily = source.FontFamily;
        target.FontWeight = source.FontWeight;
        target.ForegroundColorMode = source.ForegroundColorMode;
        target.ForegroundColor = source.ForegroundColor;
        target.UseCoverAccentColor = source.UseCoverAccentColor;
        target.CoverStyle = NormalizeCoverStyle(source.CoverStyle);
        target.CoverSize = source.CoverSize;
        target.CoverLayoutMode = source.CoverLayoutMode;
        target.ShowStackedTrackInfo = source.ShowStackedTrackInfo;
        target.StackedTrackInfoGap = source.StackedTrackInfoGap;
        target.StackedCoverLyricsGap = source.StackedCoverLyricsGap;
        target.StackedCoverXOffset = source.StackedCoverXOffset;
        target.StackedCoverYOffset = source.StackedCoverYOffset;
        target.StackedContentXOffset = source.StackedContentXOffset;
        target.StackedContentYOffset = source.StackedContentYOffset;
        target.TransitionStyle = source.TransitionStyle;
        target.ShowBackground = source.ShowBackground;
        target.BackgroundMaterial = source.BackgroundMaterial;
        target.BackgroundOpacity = source.BackgroundOpacity;
        target.ShowBorder = source.ShowBorder;
        target.ShowTextShadow = source.ShowTextShadow;
        target.WindowWidth = source.WindowWidth;
        target.WindowWidthOffset = source.WindowWidthOffset;
        target.AutoAdjustWindowWidth = source.AutoAdjustWindowWidth;
        target.WindowHeight = source.WindowHeight;
        target.WindowHeightOffset = source.WindowHeightOffset;
        target.AutoAdjustWindowHeight = source.AutoAdjustWindowHeight;
        target.HorizontalAnchor = source.HorizontalAnchor;
        target.XOffset = source.XOffset;
        target.YOffset = source.YOffset;
        target.ForceAlwaysOnTop = source.ForceAlwaysOnTop;
        target.EnableSmtcTimelineMonitor = source.EnableSmtcTimelineMonitor;
    }

    internal sealed class FontOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
