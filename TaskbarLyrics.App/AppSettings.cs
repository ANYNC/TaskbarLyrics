using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

public enum LyricsHorizontalAnchor
{
    Left,
    Center,
    Right
}

public enum LyricsTextAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}

public enum CoverPosition
{
    Left = 0,
    Right = 1
}

public enum ControlPanelTriggerMethod
{
    Click,
    Hover
}

internal enum ControlPanelTriggerArea { Cover, Lyrics }
internal enum ControlPanelActivation { Click, Hover, Keyboard }

public enum ForegroundColorMode
{
    Dark = 0,
    Light = 1,
    Custom = 2,
    System = 3
}

public enum SpectrumDisplayMode
{
    Disabled = -1,
    PureMusicOrNoLyrics,
    PureMusicOnly,
    Always,
    Custom
}

public enum ToolWindowTheme
{
    System,
    Light,
    Dark
}

public enum LyricsDisplayMode
{
    All,
    Selected
}

public enum FloatingWindowLayer
{
    Desktop,
    Window,
    Fullscreen
}

public sealed class AppSettings
{
    private static readonly string[] ReservedLegacyPlayerSources = ["QQMusic", "Netease", "Kugou", "Spotify", "Browser"];

    public const int MinimumPlayerLyricOffsetMilliseconds = -5000;
    public const int MaximumPlayerLyricOffsetMilliseconds = 5000;
    public const double ExtendedFontSizeMin = 6;
    public const double ExtendedFontSizeMax = 96;
    public const double DefaultFontSize = 14;
    public const double DefaultCoverSize = 34;
    public const double ExtendedCoverSizeMin = 12;
    public const double ExtendedCoverSizeMax = 200;
    public const double DefaultCoverGap = 8;
    public const double CoverGapMin = 0;
    public const double CoverGapMax = 240;
    public const double DefaultCoverCornerRadius = 6;
    public const double DefaultLyricsLayoutScalePercent = 100;
    public const double MinimumLyricsLayoutScalePercent = 25;
    public const double MaximumLyricsLayoutScalePercent = 300;

    public const double DefaultWindowWidth = 420;
    public const double MinimumWindowWidth = 100;
    public const double MaximumWindowWidth = 1400;

    // The default presentation is embedded in the taskbar. Users can opt into
    // the unconstrained, top-level window through the settings page.
    public bool UseFloatingWindow { get; set; }

    public const double MinimumWindowOffset = -2000;
    public const double MaximumWindowOffset = 2000;

    public const string BundledFontFamily = "Source Han Sans SC";

    public const string DefaultFontFamily = BundledFontFamily;

    private const string LegacyDefaultFontFamily = "Source Han Sans SC, Source Han Sans CN, 思源黑体 CN, Microsoft YaHei UI, Microsoft YaHei";

    public const string DefaultFontWeight = "Bold";

    public const string DarkForegroundColor = "#FF111827";

    public const string LightForegroundColor = "#FFFFFFFF";

    public List<string> SourceRecognitionOrder { get; set; } = PlayerSourceIdentity.BuiltInOrder.ToList();

    public bool EnableNetease { get; set; } = true;

    public bool EnableQQMusic { get; set; } = true;

    public bool EnableKugou { get; set; } = true;

    public bool EnableSpotify { get; set; } = true;
    public bool EnableAppleMusic { get; set; } = true;
    public bool EnableSodaMusic { get; set; } = true;
    public bool EnableFoobar2000 { get; set; } = true;
    public bool EnableBrowser { get; set; }

    public Dictionary<string, PlayerSourceSettings> PlayerSources { get; set; } = CreateDefaultPlayerSources();
    public List<CustomPlayerSource> CustomPlayerSources { get; set; } = [];

    public bool EnableLocalLyrics { get; set; } = true;

    public List<string> LocalMusicFolders { get; set; } = new();

    public bool HasSeenGettingStarted { get; set; } = true;

    public bool ShowLyricsOnStartup { get; set; } = true;

    public bool AutoHideWhenNoPlayback { get; set; } = true;

    public bool StartWithWindows { get; set; }


    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public string LastNotifiedUpdateVersion { get; set; } = "";

    public bool ShowLyricTranslation { get; set; }

    public bool ShowLyricSearchStatus { get; set; } = true;

    public bool EnableWordScanning { get; set; } = true;

    public ToolWindowTheme ToolWindowTheme { get; set; } = ToolWindowTheme.System;

    /// <summary>Interface-language catalog id, or empty to follow the system language at startup.</summary>
    public string UiLanguage { get; set; } = "";

    public SpectrumDisplayMode SpectrumDisplayMode { get; set; } = SpectrumDisplayMode.Disabled;

    public LyricsFallbackSettings? LyricsFallback { get; set; }

    internal LyricsFallbackSettings GetLyricsFallback() =>
        (LyricsFallback is { Version: 1 } ? LyricsFallback : LyricsFallbackSettings.FromLegacy(this)).Normalized();

    internal void SetLegacySpectrumMode(SpectrumDisplayMode mode)
    {
        if (!Enum.IsDefined(mode) || mode == SpectrumDisplayMode.Custom) return;
        SpectrumDisplayMode = mode;
        if (LyricsFallback is null) return;
        var rules = GetLyricsFallback();
        LyricsFallback = rules with
        {
            Instrumental = rules.Instrumental with { UseSpectrum = mode != SpectrumDisplayMode.Disabled },
            NotFound = rules.NotFound with { UseSpectrum = mode is SpectrumDisplayMode.PureMusicOrNoLyrics or SpectrumDisplayMode.Always },
            SpectrumWithLyrics = mode == SpectrumDisplayMode.Always
        };
    }

    internal void SyncFallbackSpectrumMode()
    {
        if (LyricsFallback is null) return;
        var rules = GetLyricsFallback();
        LyricsFallback = rules;
        SpectrumDisplayMode = rules.SpectrumWithLyrics && rules.Instrumental.UseSpectrum && rules.NotFound.UseSpectrum ? SpectrumDisplayMode.Always
            : !rules.SpectrumWithLyrics && rules.Instrumental.UseSpectrum && rules.NotFound.UseSpectrum ? SpectrumDisplayMode.PureMusicOrNoLyrics
            : !rules.SpectrumWithLyrics && rules.Instrumental.UseSpectrum && !rules.NotFound.UseSpectrum ? SpectrumDisplayMode.PureMusicOnly
            : !rules.UsesSpectrum ? SpectrumDisplayMode.Disabled : SpectrumDisplayMode.Custom;
    }

    public bool SpectrumAudioAccessGranted { get; set; }

    public SpectrumTuningSettings SpectrumTuning { get; set; } = SpectrumTuningSettings.CreateDefault();

    // Retained so existing settings.json files continue to round-trip without data loss.
    public bool UseSafeFontSizeRange { get; set; } = true;

    public double FontSize { get; set; } = DefaultFontSize;

    // Retained so existing settings.json files continue to round-trip without data loss.
    public bool UseSafeCoverSizeRange { get; set; } = true;

    public double CoverSize { get; set; } = DefaultCoverSize;

    public double CoverGap { get; set; } = DefaultCoverGap;

    public double CoverCornerRadius { get; set; } = DefaultCoverCornerRadius;

    public bool ShowCover { get; set; } = true;

    public bool SingleLineLyrics { get; set; }

    public bool EnableControlPanel { get; set; } = true;

    public ControlPanelTriggerMethod ControlPanelTriggerMethod { get; set; } = ControlPanelTriggerMethod.Click;
    public bool ControlPanelTriggerCover { get; set; } = true;
    public bool ControlPanelTriggerLyrics { get; set; }

    public void NormalizeControlPanelTriggers()
    {
        if (!Enum.IsDefined(ControlPanelTriggerMethod)) ControlPanelTriggerMethod = ControlPanelTriggerMethod.Click;
        if (!ControlPanelTriggerLyrics && (!ControlPanelTriggerCover || !ShowCover))
        {
            if (ShowCover) ControlPanelTriggerCover = true;
            else ControlPanelTriggerLyrics = true;
        }
    }

    internal bool CanTriggerControlPanel(ControlPanelTriggerArea area, ControlPanelActivation activation)
    {
        if (!Enum.IsDefined(area) || !Enum.IsDefined(activation)) return false;
        if (UseFloatingWindow) return ShowCover && area == ControlPanelTriggerArea.Cover && activation == ControlPanelActivation.Click;
        return EnableControlPanel &&
            (area == ControlPanelTriggerArea.Cover ? ShowCover && ControlPanelTriggerCover : ControlPanelTriggerLyrics) &&
            (activation == ControlPanelActivation.Keyboard ||
                (activation == ControlPanelActivation.Hover) == (ControlPanelTriggerMethod == ControlPanelTriggerMethod.Hover));
    }

    public bool EnableFloatingControlPanel { get; set; } = true;

    public bool ShowFloatingBackground { get; set; }

    public bool GetEnableControlPanel() => UseFloatingWindow ? EnableFloatingControlPanel : EnableControlPanel;

    public const double DefaultEmbeddedPanelBrightness = 0.20;
    public const double DefaultEmbeddedPanelTintOpacity = 0.65;
    public const double DefaultEmbeddedPanelBlurAmount = 24;
    public const double DefaultFloatingPanelBrightness = 0.25;
    public const double DefaultFloatingPanelTintOpacity = 0.55;
    public const double DefaultFloatingPanelBlurAmount = 32;

    private double _embeddedPanelBrightness = DefaultEmbeddedPanelBrightness;
    private double _embeddedPanelTintOpacity = DefaultEmbeddedPanelTintOpacity;
    private double _embeddedPanelBlurAmount = DefaultEmbeddedPanelBlurAmount;
    public double EmbeddedPanelBrightness
    {
        get => _embeddedPanelBrightness;
        set => _embeddedPanelBrightness = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : DefaultEmbeddedPanelBrightness;
    }
    public double EmbeddedPanelTintOpacity
    {
        get => _embeddedPanelTintOpacity;
        set => _embeddedPanelTintOpacity = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : DefaultEmbeddedPanelTintOpacity;
    }
    public double EmbeddedPanelBlurAmount
    {
        get => _embeddedPanelBlurAmount;
        set => _embeddedPanelBlurAmount = double.IsFinite(value) ? Math.Clamp(value, 0, 64) : DefaultEmbeddedPanelBlurAmount;
    }

    private double _floatingPanelBrightness = DefaultFloatingPanelBrightness;
    private double _floatingPanelTintOpacity = DefaultFloatingPanelTintOpacity;
    private double _floatingPanelBlurAmount = DefaultFloatingPanelBlurAmount;
    public double FloatingPanelBlurAmount
    {
        get => _floatingPanelBlurAmount;
        set => _floatingPanelBlurAmount = double.IsFinite(value) ? Math.Clamp(value, 0, 64) : DefaultFloatingPanelBlurAmount;
    }
    public double FloatingPanelBrightness
    {
        get => _floatingPanelBrightness;
        set => _floatingPanelBrightness = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : DefaultFloatingPanelBrightness;
    }
    public double FloatingPanelTintOpacity
    {
        get => _floatingPanelTintOpacity;
        set => _floatingPanelTintOpacity = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : DefaultFloatingPanelTintOpacity;
    }

    public double LyricsLayoutScalePercent { get; set; } = DefaultLyricsLayoutScalePercent;

    public double FloatingLyricsLayoutScalePercent { get; set; } = DefaultLyricsLayoutScalePercent;

    public double GetLyricsLayoutScalePercent() => UseFloatingWindow ? FloatingLyricsLayoutScalePercent : LyricsLayoutScalePercent;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public string FontWeight { get; set; } = DefaultFontWeight;

    public ForegroundColorMode ForegroundColorMode { get; set; } = ForegroundColorMode.System;

    public string ForegroundColor { get; set; } = LightForegroundColor;

    public bool ShowBackground { get; set; }

    public double BackgroundOpacity { get; set; } = 0.55;

    public bool ShowBorder { get; set; }

    public bool ShowTextShadow { get; set; }

    // Keep the existing persisted name for the embedded presentation.
    public double WindowWidth { get; set; } = DefaultWindowWidth;

    public double FloatingWindowWidth { get; set; } = DefaultWindowWidth;

    public double GetWindowWidth() => UseFloatingWindow ? FloatingWindowWidth : WindowWidth;

    public LyricsHorizontalAnchor HorizontalAnchor { get; set; } = LyricsHorizontalAnchor.Left;

    public bool TaskbarAvoidanceEnabled { get; set; } = true;

    public LyricsTextAlignment LyricsTextAlignment { get; set; } = LyricsTextAlignment.Left;

    public CoverPosition CoverPosition { get; set; } = CoverPosition.Left;

    public double XOffset { get; set; }

    public double YOffset { get; set; }

    public bool ForceAlwaysOnTop { get; set; } = true;

    private FloatingWindowLayer? _floatingWindowLayer;

    // Missing/invalid new fields retain the legacy switch's behavior, independent of JSON field order.
    public FloatingWindowLayer FloatingWindowLayer
    {
        get => _floatingWindowLayer ?? (ForceAlwaysOnTop ? FloatingWindowLayer.Fullscreen : FloatingWindowLayer.Window);
        set => _floatingWindowLayer = Enum.IsDefined(value) ? value : null;
    }

    public LyricsDisplayMode LyricsDisplayMode { get; set; } = LyricsDisplayMode.All;

    public List<string> SelectedDisplayIds { get; set; } = new();

    public GlobalMediaHotkeySettings GlobalMediaHotkeys { get; set; } = new();

    public static string NormalizeFontFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return DefaultFontFamily;
        }

        var trimmed = fontFamily.Trim();
        if (string.Equals(trimmed, LegacyDefaultFontFamily, StringComparison.OrdinalIgnoreCase))
        {
            return BundledFontFamily;
        }

        var firstFamily = trimmed
            .Split(',', 2, StringSplitOptions.TrimEntries)[0]
            .Trim('"', '\'');
        return string.Equals(firstFamily, BundledFontFamily, StringComparison.OrdinalIgnoreCase)
            ? BundledFontFamily
            : trimmed;
    }

    public AppSettings Clone()
    {
        NormalizePlayerSources();
        NormalizeLyricsTextAlignment();
        NormalizeCoverPosition();
        var cloned = (AppSettings)MemberwiseClone();
        cloned.SourceRecognitionOrder = SourceRecognitionOrder.ToList();
        cloned.CustomPlayerSources = CustomPlayerSources.ToList();
        cloned.PlayerSources = PlayerSources.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        cloned.LocalMusicFolders = LocalMusicFolders.ToList();
        cloned.SelectedDisplayIds = (SelectedDisplayIds ?? []).ToList();
        cloned.SpectrumTuning = SpectrumTuning.Clone();
        cloned.LyricsFallback = LyricsFallback?.Normalized();
        cloned.GlobalMediaHotkeys = (GlobalMediaHotkeys ?? new GlobalMediaHotkeySettings()).Clone();
        return cloned;
    }

    public void NormalizePlayerSources()
    {
        CustomPlayerSources = (CustomPlayerSources ?? [])
            .Where(source => source is not null && IsValidCustomSourceId(source.SourceAppUserModelId))
            .Select(source =>
            {
                var icon = CustomPlayerIcon.Normalize(source.IconDataUrl);
                var presetId = string.Empty;
                var presetColor = string.Empty;
                var hasPreset = icon.Length == 0 && PresetPlayerIcon.TryNormalize(
                    source.PresetIconId, source.PresetIconColor, out presetId, out presetColor);
                return new CustomPlayerSource(
                    source.SourceAppUserModelId.Trim(),
                    NormalizeCustomDisplayName(source.DisplayName, source.SourceAppUserModelId),
                    source.Enabled)
                {
                    IconDataUrl = icon,
                    PresetIconId = hasPreset ? presetId : string.Empty,
                    PresetIconColor = hasPreset ? presetColor : string.Empty
                };
            })
            .DistinctBy(source => source.SourceAppUserModelId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var current = PlayerSources ?? new Dictionary<string, PlayerSourceSettings>();
        var normalized = CreateDefaultPlayerSources();
        foreach (var overridden in CustomPlayerSources.Select(source => PlayerSourceIdentity.NormalizeAdditional(source.SourceAppUserModelId)).OfType<string>())
        {
            normalized.Remove(overridden);
        }
        foreach (var custom in CustomPlayerSources)
        {
            normalized[custom.SourceAppUserModelId] = new PlayerSourceSettings();
        }
        foreach (var source in normalized.Keys.ToList())
        {
            if (current.TryGetValue(source, out var sourceSettings) && sourceSettings is not null)
            {
                normalized[source] = new PlayerSourceSettings
                {
                    LyricOffsetMilliseconds = ClampPlayerLyricOffset(sourceSettings.LyricOffsetMilliseconds),
                    LyricProviders = PlayerSourceSettings.NormalizeLyricProviders(sourceSettings.LyricProviders, source)
                };
            }
        }

        PlayerSources = normalized;
    }

    public bool AddCustomPlayerSource(string? sourceId, string? displayName, string? iconDataUrl = null,
        string? presetIconId = null, string? presetIconColor = null, bool enabled = true,
        IReadOnlyList<LyricProviderPreference>? lyricProviders = null)
    {
        if (!IsValidCustomSourceId(sourceId)) return false;
        if (!CustomPlayerIcon.TryNormalize(iconDataUrl, out var normalizedIcon) ||
            !PresetPlayerIcon.TryNormalize(presetIconId, presetIconColor, out var normalizedPresetId, out var normalizedPresetColor) ||
            (normalizedIcon.Length > 0 && normalizedPresetId.Length > 0) ||
            (lyricProviders is not null && !PlayerSourceSettings.IsValidLyricProviders(lyricProviders))) return false;
        NormalizePlayerSources();
        var id = sourceId!.Trim();
        if (CustomPlayerSources.Any(source => string.Equals(source.SourceAppUserModelId, id, StringComparison.OrdinalIgnoreCase))) return false;
        CustomPlayerSources.Add(new CustomPlayerSource(id, NormalizeCustomDisplayName(displayName, id), enabled)
        {
            IconDataUrl = normalizedIcon,
            PresetIconId = normalizedPresetId,
            PresetIconColor = normalizedPresetColor
        });
        PlayerSources[id] = new PlayerSourceSettings
        {
            LyricProviders = PlayerSourceSettings.NormalizeLyricProviders(lyricProviders)
        };
        SourceRecognitionOrder.Add(id);
        return true;
    }

    public bool RemoveCustomPlayerSource(string? sourceId)
    {
        var removed = CustomPlayerSources.RemoveAll(source => string.Equals(source.SourceAppUserModelId, sourceId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed) return false;
        PlayerSources.Remove(sourceId!);
        SourceRecognitionOrder.RemoveAll(source => string.Equals(source, sourceId, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public bool UpdateCustomPlayerSource(string? sourceId, string? displayName, string? iconDataUrl,
        string? presetIconId, string? presetIconColor)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(displayName) ||
            displayName.Length > 80 || displayName.Any(char.IsControl) ||
            !CustomPlayerIcon.TryNormalize(iconDataUrl, out var normalizedIcon) ||
            !PresetPlayerIcon.TryNormalize(presetIconId, presetIconColor, out var normalizedPresetId, out var normalizedPresetColor) ||
            (normalizedIcon.Length > 0 && normalizedPresetId.Length > 0)) return false;

        NormalizePlayerSources();
        var index = CustomPlayerSources.FindIndex(source =>
            string.Equals(source.SourceAppUserModelId, sourceId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        CustomPlayerSources[index] = CustomPlayerSources[index] with
        {
            DisplayName = displayName.Trim(),
            IconDataUrl = normalizedIcon,
            PresetIconId = normalizedPresetId,
            PresetIconColor = normalizedPresetColor
        };
        return true;
    }

    public bool SetCustomPlayerSourceEnabled(string? sourceId, bool enabled)
    {
        var index = CustomPlayerSources.FindIndex(source => string.Equals(source.SourceAppUserModelId, sourceId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        CustomPlayerSources[index] = CustomPlayerSources[index] with { Enabled = enabled };
        return true;
    }

    private static bool IsValidCustomSourceId(string? sourceId) =>
        !string.IsNullOrWhiteSpace(sourceId) && sourceId.Length <= 256 &&
        !sourceId.Any(char.IsControl) && !ReservedLegacyPlayerSources.Contains(sourceId.Trim(), StringComparer.OrdinalIgnoreCase) &&
        !string.Equals(sourceId.Trim(), "neteasemusic", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCustomDisplayName(string? name, string sourceId)
    {
        var value = name?.Trim();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && !value.Any(char.IsControl)
            ? value
            : sourceId;
    }

    public void ResetWindowPresentation(bool floating)
    {
        var defaults = new AppSettings();
        if (floating)
        {
            FloatingWindowWidth = defaults.FloatingWindowWidth;
            FloatingLyricsLayoutScalePercent = defaults.FloatingLyricsLayoutScalePercent;
            EnableFloatingControlPanel = defaults.EnableFloatingControlPanel;
            ShowFloatingBackground = defaults.ShowFloatingBackground;
            ForceAlwaysOnTop = defaults.ForceAlwaysOnTop;
            FloatingWindowLayer = defaults.FloatingWindowLayer;
            FloatingPanelBlurAmount = defaults.FloatingPanelBlurAmount;
            FloatingPanelBrightness = defaults.FloatingPanelBrightness;
            FloatingPanelTintOpacity = defaults.FloatingPanelTintOpacity;
        }
        else
        {
            WindowWidth = defaults.WindowWidth;
            LyricsLayoutScalePercent = defaults.LyricsLayoutScalePercent;
            EnableControlPanel = defaults.EnableControlPanel;
            EmbeddedPanelBrightness = defaults.EmbeddedPanelBrightness;
            EmbeddedPanelTintOpacity = defaults.EmbeddedPanelTintOpacity;
            EmbeddedPanelBlurAmount = defaults.EmbeddedPanelBlurAmount;
            ControlPanelTriggerMethod = defaults.ControlPanelTriggerMethod;
            ControlPanelTriggerCover = defaults.ControlPanelTriggerCover;
            ControlPanelTriggerLyrics = defaults.ControlPanelTriggerLyrics;
            HorizontalAnchor = defaults.HorizontalAnchor;
            TaskbarAvoidanceEnabled = defaults.TaskbarAvoidanceEnabled;
            XOffset = defaults.XOffset;
            YOffset = defaults.YOffset;
            ShowBackground = defaults.ShowBackground;
            BackgroundOpacity = defaults.BackgroundOpacity;
            ShowBorder = defaults.ShowBorder;
        }
    }

    public void ResetLyricsLayoutBase()
    {
        FontSize = DefaultFontSize;
        CoverSize = DefaultCoverSize;
        CoverGap = DefaultCoverGap;
        CoverCornerRadius = DefaultCoverCornerRadius;
    }

    public void NormalizeLyricsLayout()
    {
        FontSize = ClampFontSize(FontSize);
        CoverSize = ClampCoverSize(CoverSize);
        CoverGap = ClampCoverGap(CoverGap);
        CoverCornerRadius = ClampCoverCornerRadius(CoverCornerRadius, CoverSize);
        LyricsLayoutScalePercent = ClampLyricsLayoutScalePercent(LyricsLayoutScalePercent);
        FloatingLyricsLayoutScalePercent = ClampLyricsLayoutScalePercent(FloatingLyricsLayoutScalePercent);
        NormalizeLyricsTextAlignment();
        NormalizeCoverPosition();
        NormalizeControlPanelTriggers();
    }

    public void NormalizeCoverPosition()
    {
        if (!Enum.IsDefined(CoverPosition))
        {
            CoverPosition = CoverPosition.Left;
        }
    }

    public void NormalizeLyricsTextAlignment()
    {
        if (!Enum.IsDefined(LyricsTextAlignment))
        {
            LyricsTextAlignment = LyricsTextAlignment.Left;
        }
    }

    public void NormalizeDisplaySelection()
    {
        if (!Enum.IsDefined(LyricsDisplayMode))
        {
            LyricsDisplayMode = LyricsDisplayMode.All;
        }

        SelectedDisplayIds = (SelectedDisplayIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void NormalizeWindowLayout()
    {
        WindowWidth = double.IsFinite(WindowWidth)
            ? Math.Clamp(WindowWidth, MinimumWindowWidth, MaximumWindowWidth)
            : DefaultWindowWidth;
        FloatingWindowWidth = CalculateEffectiveWindowWidth(FloatingWindowWidth);
        XOffset = double.IsFinite(XOffset)
            ? Math.Clamp(XOffset, MinimumWindowOffset, MaximumWindowOffset)
            : 0;
        YOffset = double.IsFinite(YOffset)
            ? Math.Clamp(YOffset, MinimumWindowOffset, MaximumWindowOffset)
            : 0;
    }

    public int GetPlayerLyricOffsetMilliseconds(string? sourceApp)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        if (source is null)
        {
            return 0;
        }

        return PlayerSources is not null &&
            PlayerSources.TryGetValue(source, out var sourceSettings) &&
            sourceSettings is not null
                ? ClampPlayerLyricOffset(sourceSettings.LyricOffsetMilliseconds)
                : GetDefaultPlayerLyricOffsetMilliseconds(source);
    }

    public void SetPlayerLyricOffsetMilliseconds(string? sourceApp, int value)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        if (source is null)
        {
            return;
        }

        NormalizePlayerSources();
        PlayerSources[source].LyricOffsetMilliseconds = ClampPlayerLyricOffset(value);
    }

    public IReadOnlyList<LyricProviderPreference> GetPlayerLyricProviders(string? sourceApp)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        return source is not null && PlayerSources is not null &&
            PlayerSources.TryGetValue(source, out var settings) && settings is not null
                ? PlayerSourceSettings.NormalizeLyricProviders(settings.LyricProviders, source)
                : PlayerSourceSettings.CreateDefaultLyricProviders(source);
    }

    public bool SetPlayerLyricProviders(string? sourceApp, IReadOnlyList<LyricProviderPreference>? providers)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        if (source is null || !PlayerSourceSettings.IsValidLyricProviders(providers))
        {
            return false;
        }

        NormalizePlayerSources();
        PlayerSources[source].LyricProviders = providers!.Select(item => item with { }).ToList();
        return true;
    }

    public LyricSourceSelection GetLyricSourceSelection(string? sourceApp)
    {
        var source = ResolvePlayerSourceName(sourceApp);
        var providers = GetPlayerLyricProviders(sourceApp);
        var enabledOrder = providers.Where(item => item.Enabled)
            .Select(item => new LyricProviderId(item.ProviderId)).ToArray();
        // Keep the original shared-order cache scope; other orders already have their own context.
        var defaults = PlayerSourceSettings.CreateDefaultLyricProviders();
        var isDefault = source is null || providers.SequenceEqual(defaults);
        var context = isDefault ? null : $"{source}:{string.Join(',', providers.Select(item => $"{item.ProviderId}:{(item.Enabled ? 1 : 0)}"))}";
        return new LyricSourceSelection(enabledOrder, context);
    }

    public static int GetDefaultPlayerLyricOffsetMilliseconds(string? sourceApp)
    {
        return NormalizePlayerSourceName(sourceApp) switch
        {
            "QQMusic" => 0,
            "Netease" => 0,
            "Kugou" => 0,
            "Spotify" => 0,
            _ => 0
        };
    }

    public static int ClampPlayerLyricOffset(int value)
    {
        return Math.Clamp(value, MinimumPlayerLyricOffsetMilliseconds, MaximumPlayerLyricOffsetMilliseconds);
    }

    private static Dictionary<string, PlayerSourceSettings> CreateDefaultPlayerSources()
    {
        return PlayerSourceIdentity.BuiltInOrder.ToDictionary(
            source => source,
            source => new PlayerSourceSettings
            {
                LyricOffsetMilliseconds = GetDefaultPlayerLyricOffsetMilliseconds(source),
                LyricProviders = PlayerSourceSettings.CreateDefaultLyricProviders(source)
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizePlayerSourceName(string? sourceApp)
    {
        return sourceApp?.Trim().ToLowerInvariant() switch
        {
            "qqmusic" => "QQMusic",
            "netease" or "neteasemusic" => "Netease",
            "kugou" => "Kugou",
            "spotify" => "Spotify",
            "browser" => "Browser",
            _ => PlayerSourceIdentity.NormalizeAdditional(sourceApp)
        };
    }

    private string? ResolvePlayerSourceName(string? sourceApp) =>
        CustomPlayerSources.FirstOrDefault(source => string.Equals(source.SourceAppUserModelId, sourceApp, StringComparison.OrdinalIgnoreCase))?.SourceAppUserModelId ??
        NormalizePlayerSourceName(sourceApp);

    public static double ClampFontSize(double value)
    {
        return Math.Clamp(value, ExtendedFontSizeMin, ExtendedFontSizeMax);
    }

    public static double ClampCoverSize(double value)
    {
        return Math.Clamp(value, ExtendedCoverSizeMin, ExtendedCoverSizeMax);
    }

    public static double ClampCoverGap(double value)
    {
        return Math.Clamp(value, CoverGapMin, CoverGapMax);
    }

    public static double ClampCoverCornerRadius(double value, double coverSize)
    {
        var maxRadius = Math.Max(0, coverSize / 2);
        return Math.Clamp(value, 0, maxRadius);
    }

    public static double ClampLyricsLayoutScalePercent(double value)
    {
        return Math.Clamp(
            value,
            MinimumLyricsLayoutScalePercent,
            MaximumLyricsLayoutScalePercent);
    }

    public static double ClampEffectiveWindowWidth(double windowWidth, double maxWidth)
    {
        var width = CalculateEffectiveWindowWidth(windowWidth);
        if (!double.IsFinite(maxWidth) || maxWidth <= 0)
        {
            maxWidth = MaximumWindowWidth;
        }

        if (maxWidth < MinimumWindowWidth)
        {
            return maxWidth;
        }

        return Math.Clamp(width, MinimumWindowWidth, maxWidth);
    }

    public static double CalculateEffectiveWindowWidth(double windowWidth) => double.IsFinite(windowWidth)
        ? Math.Clamp(windowWidth, MinimumWindowWidth, MaximumWindowWidth) : DefaultWindowWidth;

}

public sealed class PlayerSourceSettings
{
    public int LyricOffsetMilliseconds { get; set; }

    // An absent list is filled with the owning player's defaults during normalization.
    public List<LyricProviderPreference> LyricProviders { get; set; } = [];

    public static List<LyricProviderPreference> CreateDefaultLyricProviders(string? sourceApp = null) =>
        KnownLyricProviders.OnlineTrustOrder
            .OrderBy(provider => provider != KnownLyricProviders.LrcLib &&
                string.Equals(provider.Value, sourceApp, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(provider => new LyricProviderPreference(provider.Value, true)).ToList();

    public static bool IsValidLyricProviders(IReadOnlyList<LyricProviderPreference>? providers) =>
        providers is not null && providers.Count == KnownLyricProviders.OnlineTrustOrder.Count &&
        providers.All(item => item is not null && KnownLyricProviders.OnlineTrustOrder.Any(
            known => string.Equals(known.Value, item.ProviderId, StringComparison.Ordinal))) &&
        providers.Select(item => item.ProviderId).Distinct(StringComparer.Ordinal).Count() == providers.Count;

    public static List<LyricProviderPreference> NormalizeLyricProviders(
        IReadOnlyList<LyricProviderPreference>? providers, string? sourceApp = null) =>
        IsValidLyricProviders(providers)
            ? providers!.Select(item => item with { }).ToList()
            : CreateDefaultLyricProviders(sourceApp);

    public PlayerSourceSettings Clone()
    {
        return new PlayerSourceSettings
        {
            LyricOffsetMilliseconds = LyricOffsetMilliseconds,
            LyricProviders = NormalizeLyricProviders(LyricProviders)
        };
    }
}

public sealed record LyricProviderPreference(string ProviderId, bool Enabled);

public sealed record CustomPlayerSource(string SourceAppUserModelId, string DisplayName, bool Enabled)
{
    public string IconDataUrl { get; init; } = string.Empty;
    public string PresetIconId { get; init; } = string.Empty;
    public string PresetIconColor { get; init; } = string.Empty;
}
