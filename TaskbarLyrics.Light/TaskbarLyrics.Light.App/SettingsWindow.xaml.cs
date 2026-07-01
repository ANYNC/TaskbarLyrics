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
using TaskbarLyrics.Core.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace TaskbarLyrics.Light.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _isLoading;
    private bool _sidebarCollapsed;
    private bool _isUpdatingNavFromScroll;
    private bool _fontOptionsPopulated;
    private bool _isCheckingUpdate;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        _settings = settings;

        LoadPlayerIcons();
        WireEvents();
        EnsureFontOptionsPopulated();
        LoadFromSettings();
        Closed += (_, _) => SaveSettings();
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
        EnablePureMusicSpectrumCheck.Checked += (_, _) => OnSettingChanged();
        EnablePureMusicSpectrumCheck.Unchecked += (_, _) => OnSettingChanged();
        EnableSpectrumCheck.Checked += (_, _) => OnSettingChanged();
        EnableSpectrumCheck.Unchecked += (_, _) => OnSettingChanged();
        ShowSpectrumWhenLyricsNotFoundCheck.Checked += (_, _) => OnSettingChanged();
        ShowSpectrumWhenLyricsNotFoundCheck.Unchecked += (_, _) => OnSettingChanged();
        EnableLocalLyricsCheck.Checked += (_, _) => OnSettingChanged();
        EnableLocalLyricsCheck.Unchecked += (_, _) => OnSettingChanged();
        LocalMusicFoldersBox.LostFocus += (_, _) => OnSettingChanged();
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

        _fontOptionsPopulated = true;
        PopulateFontOptions();
    }

    private void LoadFromSettings()
    {
        EnsureFontOptionsPopulated();
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
        EnablePureMusicSpectrumCheck.IsChecked = _settings.EnablePureMusicSpectrum;
        EnableSpectrumCheck.IsChecked = _settings.EnableSpectrum;
        ShowSpectrumWhenLyricsNotFoundCheck.IsChecked = _settings.ShowSpectrumWhenLyricsNotFound;
        EnableLocalLyricsCheck.IsChecked = _settings.EnableLocalLyrics;
        LocalMusicFoldersBox.Text = string.Join(Environment.NewLine, _settings.LocalMusicFolders);
            ShowBackgroundCheck.IsChecked = _settings.ShowBackground;
            ShowBorderCheck.IsChecked = _settings.ShowBorder;
            ShowTextShadowCheck.IsChecked = _settings.ShowTextShadow;
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
            SelectFontFamily(_settings.FontFamily);
            SelectComboByTag(ForegroundModeCombo, _settings.ForegroundColorMode.ToString());

            SourceOrderList.SetOrder(NormalizeSourceOrder(_settings.SourceRecognitionOrder));
            UpdateColorUi();
            UpdateLineGapControlsState();
            UpdateWindowWidthControlsState();
            UpdateWindowHeightControlsState();
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
        _settings.EnablePureMusicSpectrum = EnablePureMusicSpectrumCheck.IsChecked == true;
        _settings.EnableSpectrum = EnableSpectrumCheck.IsChecked == true;
        _settings.ShowSpectrumWhenLyricsNotFound = ShowSpectrumWhenLyricsNotFoundCheck.IsChecked == true;
        _settings.EnableLocalLyrics = EnableLocalLyricsCheck.IsChecked == true;
        _settings.LocalMusicFolders = NormalizeLocalMusicFolders(LocalMusicFoldersBox.Text);
        _settings.ForceAlwaysOnTop = ForceAlwaysOnTopCheck.IsChecked == true;
        _settings.AutoCheckUpdates = AutoCheckUpdatesCheck.IsChecked == true;
        _settings.ShowBackground = ShowBackgroundCheck.IsChecked == true;
        _settings.ShowBorder = ShowBorderCheck.IsChecked == true;
        _settings.ShowTextShadow = ShowTextShadowCheck.IsChecked == true;
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

        var anchorTag = (HorizontalAnchorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Left";
        _settings.HorizontalAnchor = Enum.TryParse<LyricsHorizontalAnchor>(anchorTag, out var anchor)
            ? anchor
            : LyricsHorizontalAnchor.Left;

        SaveSettings();
    }

    private void SaveSettings()
    {
        if (System.Windows.Application.Current is App app)
        {
            app.SaveSettings(_settings.Clone());
        }
    }

    private void RenderAbout()
    {
        AppVersionText.Text = $"当前版本 {UpdateChecker.GetCurrentVersion()}";
        if (!_isCheckingUpdate)
        {
            UpdateStatusText.Text = string.Empty;
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdate)
        {
            return;
        }

        _isCheckingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "检查中...";
        UpdateStatusText.Text = "正在检查更新...";

        try
        {
            var result = await UpdateChecker.CheckLatestAsync();
            _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _settings.AutoCheckUpdates = AutoCheckUpdatesCheck.IsChecked == true;

            UpdateStatusText.Text = result.State switch
            {
                UpdateCheckState.Available =>
                    $"发现新版本 {result.Version}，当前版本 {result.CurrentVersion}。",
                UpdateCheckState.Latest =>
                    $"已是最新版本（{result.CurrentVersion}）。",
                _ => "检查更新失败，请稍后重试。"
            };

            SaveSettings();
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
        }
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
        SaveSettings();
    }

    private static void ClearLyricCache()
    {
        LyricProviderBase.ClearCache();
        GenericSmtcLyricProvider.ClearCache();
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

        var fonts = GetFontOptions();
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
        target.AutoCheckUpdates = source.AutoCheckUpdates;
        target.LastUpdateCheckUtc = source.LastUpdateCheckUtc;
        target.LastNotifiedUpdateVersion = source.LastNotifiedUpdateVersion;
        target.EnableSpectrum = source.EnableSpectrum;
        target.EnablePureMusicSpectrum = source.EnablePureMusicSpectrum;
        target.ShowSpectrumWhenLyricsNotFound = source.ShowSpectrumWhenLyricsNotFound;
        target.EnableLocalLyrics = source.EnableLocalLyrics;
        target.LocalMusicFolders = NormalizeLocalMusicFolders(source.LocalMusicFolders);
        target.FontSize = source.FontSize;
        target.AutoAdjustLineGap = source.AutoAdjustLineGap;
        target.LineGap = source.LineGap;
        target.LineGapOffset = source.LineGapOffset;
        target.FontFamily = source.FontFamily;
        target.FontWeight = source.FontWeight;
        target.ForegroundColorMode = source.ForegroundColorMode;
        target.ForegroundColor = source.ForegroundColor;
        target.ShowBackground = source.ShowBackground;
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
