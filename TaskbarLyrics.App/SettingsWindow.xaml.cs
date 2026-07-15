using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private const string DisabledSpectrumDisplayMode = "Disabled";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppSettings _settings;
    private bool _isWebReady;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        _settings = settings;
        ApplyWindowTheme();

        SourceInitialized += SettingsWindow_SourceInitialized;
        Loaded += SettingsWindow_Loaded;
        Activated += SettingsWindow_Activated;
        StateChanged += SettingsWindow_StateChanged;
        Closed += SettingsWindow_Closed;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void SettingsWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowTheme();
    }

    private async void SettingsWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        await InitializeSettingsWebViewAsync();
    }

    private void SettingsWindow_Activated(object? sender, EventArgs e)
    {
        ApplyWindowTheme();
    }

    private void SettingsWindow_StateChanged(object? sender, EventArgs e)
    {
        _ = PushWindowStateToWebAsync();
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        if (SettingsWebView.CoreWebView2 is not null)
        {
            SettingsWebView.CoreWebView2.WebMessageReceived -= SettingsWebView_WebMessageReceived;
            SettingsWebView.CoreWebView2.Navigate("about:blank");
        }

        SettingsWebView.Dispose();
    }

    private async Task InitializeSettingsWebViewAsync()
    {
        if (_isWebReady)
        {
            return;
        }

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarLyrics",
            "WebView2",
            "Settings");
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await SettingsWebView.EnsureCoreWebView2Async(environment);
        ApplyWindowTheme();

        var core = SettingsWebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.WebMessageReceived += SettingsWebView_WebMessageReceived;

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "Web", "Settings", "settings.html");
        SettingsWebView.Source = new Uri(htmlPath);
        _isWebReady = true;
    }

    private async void SettingsWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var messageJson = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            messageJson = e.WebMessageAsJson;
        }

        var message = JsonSerializer.Deserialize<WebSettingsMessage>(messageJson, JsonOptions);
        if (message?.Type is null)
        {
            return;
        }

        switch (message.Type)
        {
            case "ready":
                await PushSettingsToWebAsync();
                await PushWindowStateToWebAsync();
                break;
            case "update":
                ApplyWebSettingUpdate(message.Key, message.Value);
                SaveSettings();
                break;
            case "reorderSources":
                ApplySourceOrder(message.Value);
                SaveSettings();
                break;
            case "resetDefaults":
                var defaultSettings = new AppSettings();
                App.ApplyStartupForegroundColor(defaultSettings);
                CopySettings(defaultSettings, _settings);
                SaveSettings();
                await PushSettingsToWebAsync();
                break;
            case "clearCache":
                ClearLyricCache();
                break;
            case "openSmtcMonitor":
                if (System.Windows.Application.Current is App smtcApp)
                {
                    smtcApp.OpenSmtcTimelineMonitorWindow();
                }
                break;
            case "openSpectrumTuning":
                if (System.Windows.Application.Current is App app)
                {
                    app.OpenSpectrumTuningWindow();
                }
                break;
            case "pickColor":
                await PickForegroundColorAsync();
                break;
            case "pickLocalFolder":
                await PickLocalFolderAsync();
                break;
            case "showLyricsWindow":
                if (System.Windows.Application.Current is App lyricsApp)
                {
                    lyricsApp.ShowLyricsWindow();
                }
                break;
            case "checkForUpdates":
                await CheckForUpdatesAsync();
                break;
            case "openExternalLink":
                OpenExternalLink(message.Value.HasValue
                    ? ReadString(message.Value.Value, UpdateChecker.RepositoryUrl)
                    : UpdateChecker.RepositoryUrl);
                break;
            case "windowDrag":
                NativeWindowInteraction.BeginDrag(this);
                break;
            case "windowResizeStart":
                NativeWindowInteraction.BeginResize(this, message.Value.HasValue
                    ? ReadString(message.Value.Value, string.Empty)
                    : string.Empty);
                break;
            case "windowMinimize":
                WindowState = WindowState.Minimized;
                break;
            case "windowMaximize":
                ToggleMaximizeRestore();
                break;
            case "windowClose":
                Close();
                break;
        }
    }

    private async Task PushSettingsToWebAsync()
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var payload = CreateSettingsPayload();
        var settingsJson = JsonSerializer.Serialize(payload, JsonOptions);
        var fontsJson = JsonSerializer.Serialize(GetFontOptions(), JsonOptions);
        await SettingsWebView.ExecuteScriptAsync($"window.settingsApp?.setState({settingsJson}, {fontsJson});");
    }

    public async void ApplyExternalSettings(AppSettings settings)
    {
        CopySettings(settings, _settings);
        await PushSettingsToWebAsync();
    }

    private WebSettingsPayload CreateSettingsPayload()
    {
        return new WebSettingsPayload
        {
            SourceRecognitionOrder = NormalizeSourceOrder(_settings.SourceRecognitionOrder),
            EnableNetease = _settings.EnableNetease,
            EnableQQMusic = _settings.EnableQQMusic,
            EnableKugou = _settings.EnableKugou,
            EnableSpotify = _settings.EnableSpotify,
            EnableLocalLyrics = _settings.EnableLocalLyrics,
            LocalMusicFolders = NormalizeLocalMusicFolders(_settings.LocalMusicFolders),
            ShowLyricsOnStartup = _settings.ShowLyricsOnStartup,
            StartWithWindows = _settings.StartWithWindows,
            AutoCheckUpdates = _settings.AutoCheckUpdates,
            ShowLyricTranslation = _settings.ShowLyricTranslation,
            SpectrumDisplayMode = _settings.EnableSpectrum
                ? _settings.SpectrumDisplayMode.ToString()
                : DisabledSpectrumDisplayMode,
            EnablePureMusicSpectrum = _settings.EnablePureMusicSpectrum,
            ShowSpectrumWhenLyricsNotFound = _settings.ShowSpectrumWhenLyricsNotFound,
            UseSafeFontSizeRange = _settings.UseSafeFontSizeRange,
            FontSize = _settings.FontSize,
            UseSafeCoverSizeRange = _settings.UseSafeCoverSizeRange,
            CoverSize = _settings.CoverSize,
            CoverGap = _settings.CoverGap,
            CoverCornerRadius = _settings.CoverCornerRadius,
            FontFamily = ResolveInstalledFontFamily(AppSettings.NormalizeFontFamily(_settings.FontFamily)) ?? AppSettings.BundledFontFamily,
            FontWeight = NormalizeFontWeight(_settings.FontWeight),
            ForegroundColorMode = _settings.ForegroundColorMode,
            ForegroundColor = _settings.ForegroundColor,
            ShowBackground = _settings.ShowBackground,
            BackgroundOpacity = _settings.BackgroundOpacity,
            ShowBorder = _settings.ShowBorder,
            ShowTextShadow = _settings.ShowTextShadow,
            WindowWidth = _settings.WindowWidth,
            HorizontalAnchor = _settings.HorizontalAnchor,
            XOffset = _settings.XOffset,
            YOffset = _settings.YOffset,
            ForceAlwaysOnTop = _settings.ForceAlwaysOnTop,
            AppVersion = UpdateChecker.GetCurrentVersion(),
            RepositoryUrl = UpdateChecker.RepositoryUrl
        };
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

    private static List<FontOption> GetFontOptions()
    {
        var fonts = Fonts.SystemFontFamilies
            .Select(x => new FontOption
            {
                Value = x.Source,
                Label = GetLocalizedFontName(x)
            })
            .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!fonts.Any(x => string.Equals(x.Value, AppSettings.BundledFontFamily, StringComparison.OrdinalIgnoreCase)))
        {
            fonts.Insert(0, new FontOption
            {
                Value = AppSettings.BundledFontFamily,
                Label = $"{AppSettings.BundledFontFamily} (内置)"
            });
        }

        return fonts;
    }

    private static string GetLocalizedFontName(System.Windows.Media.FontFamily fontFamily)
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
            if (string.Equals(candidate, AppSettings.BundledFontFamily, StringComparison.OrdinalIgnoreCase))
            {
                return AppSettings.BundledFontFamily;
            }

            if (byValue.TryGetValue(candidate, out var value) ||
                byLabel.TryGetValue(candidate, out value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeFontWeight(string? value)
    {
        return value?.Trim() switch
        {
            "Light" => "Light",
            "Normal" => "Normal",
            "Medium" => "Medium",
            "SemiBold" => "SemiBold",
            "Bold" => "Bold",
            _ => "SemiBold"
        };
    }

    private void ApplyWebSettingUpdate(string? key, JsonElement? value)
    {
        if (key is null || value is null)
        {
            return;
        }

        var element = value.Value;
        switch (key)
        {
            case "enableQQMusic":
                _settings.EnableQQMusic = ReadBool(element, _settings.EnableQQMusic);
                break;
            case "enableNetease":
                _settings.EnableNetease = ReadBool(element, _settings.EnableNetease);
                break;
            case "enableKugou":
                _settings.EnableKugou = ReadBool(element, _settings.EnableKugou);
                break;
            case "enableSpotify":
                _settings.EnableSpotify = ReadBool(element, _settings.EnableSpotify);
                break;
            case "enableLocalLyrics":
                _settings.EnableLocalLyrics = ReadBool(element, _settings.EnableLocalLyrics);
                break;
            case "localMusicFolders":
                _settings.LocalMusicFolders = NormalizeLocalMusicFolders(ReadStringList(element));
                break;
            case "showLyricsOnStartup":
                _settings.ShowLyricsOnStartup = ReadBool(element, _settings.ShowLyricsOnStartup);
                break;
            case "startWithWindows":
                _settings.StartWithWindows = ReadBool(element, _settings.StartWithWindows);
                StartupService.SetEnabled(_settings.StartWithWindows);
                break;
            case "autoCheckUpdates":
                _settings.AutoCheckUpdates = ReadBool(element, _settings.AutoCheckUpdates);
                break;
            case "showLyricTranslation":
                _settings.ShowLyricTranslation = ReadBool(element, _settings.ShowLyricTranslation);
                break;
            case "spectrumDisplayMode":
                var spectrumDisplayMode = ReadString(element, DisabledSpectrumDisplayMode);
                if (string.Equals(spectrumDisplayMode, DisabledSpectrumDisplayMode, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.EnableSpectrum = false;
                }
                else if (Enum.TryParse<SpectrumDisplayMode>(spectrumDisplayMode, true, out var parsedSpectrumDisplayMode))
                {
                    _settings.EnableSpectrum = true;
                    _settings.SpectrumDisplayMode = parsedSpectrumDisplayMode;
                }
                break;
            case "enablePureMusicSpectrum":
                _settings.EnablePureMusicSpectrum = ReadBool(element, _settings.EnablePureMusicSpectrum);
                break;
            case "showSpectrumWhenLyricsNotFound":
                _settings.ShowSpectrumWhenLyricsNotFound = ReadBool(element, _settings.ShowSpectrumWhenLyricsNotFound);
                break;
            case "showBackground":
                _settings.ShowBackground = ReadBool(element, _settings.ShowBackground);
                break;
            case "showBorder":
                _settings.ShowBorder = ReadBool(element, _settings.ShowBorder);
                break;
            case "showTextShadow":
                _settings.ShowTextShadow = ReadBool(element, _settings.ShowTextShadow);
                break;
            case "forceAlwaysOnTop":
                _settings.ForceAlwaysOnTop = ReadBool(element, _settings.ForceAlwaysOnTop);
                break;
            case "useSafeFontSizeRange":
                _settings.UseSafeFontSizeRange = ReadBool(element, _settings.UseSafeFontSizeRange);
                _settings.FontSize = AppSettings.ClampFontSize(_settings.FontSize, _settings.UseSafeFontSizeRange);
                break;
            case "fontSize":
                _settings.FontSize = AppSettings.ClampFontSize(ReadDouble(element, _settings.FontSize), _settings.UseSafeFontSizeRange);
                break;
            case "useSafeCoverSizeRange":
                _settings.UseSafeCoverSizeRange = ReadBool(element, _settings.UseSafeCoverSizeRange);
                _settings.CoverSize = AppSettings.ClampCoverSize(_settings.CoverSize, _settings.UseSafeCoverSizeRange);
                _settings.CoverCornerRadius = AppSettings.ClampCoverCornerRadius(_settings.CoverCornerRadius, _settings.CoverSize);
                break;
            case "coverSize":
                _settings.CoverSize = AppSettings.ClampCoverSize(ReadDouble(element, _settings.CoverSize), _settings.UseSafeCoverSizeRange);
                _settings.CoverCornerRadius = AppSettings.ClampCoverCornerRadius(_settings.CoverCornerRadius, _settings.CoverSize);
                break;
            case "coverGap":
                _settings.CoverGap = AppSettings.ClampCoverGap(ReadDouble(element, _settings.CoverGap));
                break;
            case "coverCornerRadius":
                _settings.CoverCornerRadius = AppSettings.ClampCoverCornerRadius(ReadDouble(element, _settings.CoverCornerRadius), _settings.CoverSize);
                break;
            case "fontFamily":
                _settings.FontFamily = AppSettings.NormalizeFontFamily(ReadString(element, _settings.FontFamily));
                break;
            case "fontWeight":
                _settings.FontWeight = NormalizeFontWeight(ReadString(element, _settings.FontWeight));
                break;
            case "foregroundColor":
                _settings.ForegroundColorMode = ForegroundColorMode.Custom;
                _settings.ForegroundColor = NormalizeColor(ReadString(element, _settings.ForegroundColor));
                break;
            case "foregroundColorMode":
                _settings.ForegroundColorMode = ReadForegroundColorMode(element, _settings.ForegroundColorMode);
                ApplyForegroundColorMode();
                break;
            case "backgroundOpacity":
                _settings.BackgroundOpacity = Math.Clamp(ReadDouble(element, _settings.BackgroundOpacity), 0, 1);
                break;
            case "windowWidth":
                _settings.WindowWidth = Math.Clamp(ReadDouble(element, _settings.WindowWidth), 320, 1400);
                break;
            case "horizontalAnchor":
                if (Enum.TryParse<LyricsHorizontalAnchor>(ReadString(element, _settings.HorizontalAnchor.ToString()), out var anchor))
                {
                    _settings.HorizontalAnchor = anchor;
                }
                break;
            case "xOffset":
                _settings.XOffset = Math.Clamp(ReadDouble(element, _settings.XOffset), -2000, 2000);
                break;
            case "yOffset":
                _settings.YOffset = Math.Clamp(ReadDouble(element, _settings.YOffset), -2000, 2000);
                break;
        }
    }

    private void ApplySourceOrder(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        _settings.SourceRecognitionOrder = NormalizeSourceOrder(value.Value.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!));
    }

    private async Task PickForegroundColorAsync()
    {
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true
        };

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
        SaveSettings();
        await PushSettingsToWebAsync();
    }

    private async Task PickLocalFolderAsync()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择本地音乐目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        var initialFolder = _settings.LocalMusicFolders.FirstOrDefault(Directory.Exists);
        if (!string.IsNullOrWhiteSpace(initialFolder))
        {
            dialog.SelectedPath = initialFolder;
        }

        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        _settings.LocalMusicFolders = NormalizeLocalMusicFolders(
            _settings.LocalMusicFolders.Append(dialog.SelectedPath));
        SaveSettings();
        await PushSettingsToWebAsync();
    }

    private void ApplyForegroundColorMode()
    {
        _settings.ForegroundColor = _settings.ForegroundColorMode switch
        {
            ForegroundColorMode.Dark => AppSettings.DarkForegroundColor,
            ForegroundColorMode.Light => AppSettings.LightForegroundColor,
            _ => _settings.ForegroundColor
        };
    }

    private void ClearLyricCache()
    {
        LyricProviderBase.ClearCache();
        GenericSmtcLyricProvider.ClearCache();
    }

    private async Task CheckForUpdatesAsync()
    {
        await PushUpdateStatusToWebAsync(new UpdateStatusPayload
        {
            State = "checking",
            Message = "正在检查更新..."
        });

        try
        {
            var result = await UpdateChecker.CheckLatestAsync();
            if (result.State == UpdateCheckState.Error)
            {
                await PushUpdateStatusToWebAsync(new UpdateStatusPayload
                {
                    State = "error",
                    Message = "没有读取到最新版本信息。"
                });
                return;
            }

            await PushUpdateStatusToWebAsync(new UpdateStatusPayload
            {
                State = result.HasUpdate ? "available" : "latest",
                Message = result.HasUpdate
                    ? $"发现新版本 {result.Version}，当前版本 {result.CurrentVersion}。"
                    : $"当前已是最新版本：{result.CurrentVersion}。",
                Version = result.Version,
                Url = result.Url
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            await PushUpdateStatusToWebAsync(new UpdateStatusPayload
            {
                State = "error",
                Message = "检查更新失败，请稍后重试。"
            });
        }
    }

    private async Task PushUpdateStatusToWebAsync(UpdateStatusPayload payload)
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await SettingsWebView.ExecuteScriptAsync($"window.settingsApp?.setUpdateStatus({json});");
    }

    private async Task PushWindowStateToWebAsync()
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var state = WindowState == WindowState.Maximized ? "maximized" : "normal";
        await SettingsWebView.ExecuteScriptAsync($"window.settingsApp?.setWindowState({JsonSerializer.Serialize(state, JsonOptions)});");
    }

    private static void OpenExternalLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }

    private void SaveSettings()
    {
        if (System.Windows.Application.Current is App app)
        {
            app.SaveSettings(_settings.Clone());
        }

    }

    private static bool ReadBool(JsonElement element, bool fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var value) => value,
            _ => fallback
        };
    }

    private static double ReadDouble(JsonElement element, double fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            _ => fallback
        };
    }

    private static string ReadString(JsonElement element, string fallback)
    {
        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
            : fallback;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement element, TEnum fallback)
        where TEnum : struct, Enum
    {
        return element.ValueKind == JsonValueKind.String &&
            Enum.TryParse<TEnum>(element.GetString(), ignoreCase: true, out var value)
                ? value
                : fallback;
    }

    private static List<string> ReadStringList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString()?
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList() ?? new List<string>();
        }

        return new List<string>();
    }

    private static List<string> NormalizeLocalMusicFolders(IEnumerable<string>? folders)
    {
        return (folders ?? Enumerable.Empty<string>())
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return AppSettings.LightForegroundColor;
        }

        var trimmed = color.Trim();
        return trimmed.Length == 7 && trimmed.StartsWith('#')
            ? $"#FF{trimmed[1..]}"
            : trimmed;
    }

    private static ForegroundColorMode ReadForegroundColorMode(JsonElement element, ForegroundColorMode fallback)
    {
        if (element.ValueKind == JsonValueKind.String &&
            Enum.TryParse<ForegroundColorMode>(element.GetString(), out var stringValue))
        {
            return stringValue;
        }

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out var intValue) &&
            Enum.IsDefined(typeof(ForegroundColorMode), intValue))
        {
            return (ForegroundColorMode)intValue;
        }

        return fallback;
    }

    private static bool TryParseMediaColor(string? color, out System.Windows.Media.Color parsedColor)
    {
        parsedColor = Colors.White;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(color.Trim()) is System.Windows.Media.Color mediaColor)
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
        target.EnableLocalLyrics = source.EnableLocalLyrics;
        target.LocalMusicFolders = NormalizeLocalMusicFolders(source.LocalMusicFolders);
        target.ShowLyricsOnStartup = source.ShowLyricsOnStartup;
        target.StartWithWindows = source.StartWithWindows;
        target.AutoCheckUpdates = source.AutoCheckUpdates;
        target.LastUpdateCheckUtc = source.LastUpdateCheckUtc;
        target.LastNotifiedUpdateVersion = source.LastNotifiedUpdateVersion;
        target.ShowLyricTranslation = source.ShowLyricTranslation;
        target.EnableSpectrum = source.EnableSpectrum;
        target.SpectrumDisplayMode = source.SpectrumDisplayMode;
        target.EnablePureMusicSpectrum = source.EnablePureMusicSpectrum;
        target.ShowSpectrumWhenLyricsNotFound = source.ShowSpectrumWhenLyricsNotFound;
        target.UseSafeFontSizeRange = source.UseSafeFontSizeRange;
        target.FontSize = source.FontSize;
        target.UseSafeCoverSizeRange = source.UseSafeCoverSizeRange;
        target.CoverSize = source.CoverSize;
        target.CoverGap = source.CoverGap;
        target.CoverCornerRadius = source.CoverCornerRadius;
        target.FontFamily = source.FontFamily;
        target.FontWeight = source.FontWeight;
        target.ForegroundColorMode = source.ForegroundColorMode;
        target.ForegroundColor = source.ForegroundColor;
        target.ShowBackground = source.ShowBackground;
        target.BackgroundOpacity = source.BackgroundOpacity;
        target.ShowBorder = source.ShowBorder;
        target.ShowTextShadow = source.ShowTextShadow;
        target.WindowWidth = source.WindowWidth;
        target.HorizontalAnchor = source.HorizontalAnchor;
        target.XOffset = source.XOffset;
        target.YOffset = source.YOffset;
        target.ForceAlwaysOnTop = source.ForceAlwaysOnTop;
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        Dispatcher.BeginInvoke(ApplyWindowTheme);
    }

    private void ApplyWindowTheme()
    {
        NativeWindowTheme.Apply(this, SettingsWebView);
    }

    private sealed class WebSettingsMessage
    {
        public string? Type { get; set; }

        public string? Key { get; set; }

        public JsonElement? Value { get; set; }
    }

    private sealed class WebSettingsPayload
    {
        public List<string> SourceRecognitionOrder { get; set; } = new();
        public bool EnableNetease { get; set; }
        public bool EnableQQMusic { get; set; }
        public bool EnableKugou { get; set; }
        public bool EnableSpotify { get; set; }
        public bool EnableLocalLyrics { get; set; }
        public List<string> LocalMusicFolders { get; set; } = new();
        public bool ShowLyricsOnStartup { get; set; }
        public bool StartWithWindows { get; set; }
        public bool AutoCheckUpdates { get; set; }
        public bool ShowLyricTranslation { get; set; }
        public string SpectrumDisplayMode { get; set; } = DisabledSpectrumDisplayMode;
        public bool EnablePureMusicSpectrum { get; set; }
        public bool ShowSpectrumWhenLyricsNotFound { get; set; }
        public bool UseSafeFontSizeRange { get; set; }
        public double FontSize { get; set; }
        public bool UseSafeCoverSizeRange { get; set; }
        public double CoverSize { get; set; }
        public double CoverGap { get; set; }
        public double CoverCornerRadius { get; set; }
        public string FontFamily { get; set; } = "";
        public string FontWeight { get; set; } = "";
        public ForegroundColorMode ForegroundColorMode { get; set; }
        public string ForegroundColor { get; set; } = "";
        public bool ShowBackground { get; set; }
        public double BackgroundOpacity { get; set; }
        public bool ShowBorder { get; set; }
        public bool ShowTextShadow { get; set; }
        public double WindowWidth { get; set; }
        public LyricsHorizontalAnchor HorizontalAnchor { get; set; }
        public double XOffset { get; set; }
        public double YOffset { get; set; }
        public bool ForceAlwaysOnTop { get; set; }
        public string AppVersion { get; set; } = "";
        public string RepositoryUrl { get; set; } = "";
    }

    private sealed class UpdateStatusPayload
    {
        public string State { get; set; } = "";

        public string Message { get; set; } = "";

        public string Version { get; set; } = "";

        public string Url { get; set; } = "";
    }

    private sealed class FontOption
    {
        public string Value { get; set; } = "";

        public string Label { get; set; } = "";
    }
}
