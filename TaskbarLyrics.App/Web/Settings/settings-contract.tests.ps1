$ErrorActionPreference = 'Stop'

$settingsRoot = $PSScriptRoot
$appRoot = Resolve-Path (Join-Path $settingsRoot '..\..')
$html = [IO.File]::ReadAllText((Join-Path $settingsRoot 'settings.html'), [Text.UTF8Encoding]::new($false, $true))
$css = [IO.File]::ReadAllText((Join-Path $settingsRoot 'settings.css'), [Text.UTF8Encoding]::new($false, $true))
$script = [IO.File]::ReadAllText((Join-Path $settingsRoot 'settings.js'), [Text.UTF8Encoding]::new($false, $true))
$bridge = [IO.File]::ReadAllText((Join-Path $settingsRoot 'bridge.js'), [Text.UTF8Encoding]::new($false, $true))
$hotkeyScript = [IO.File]::ReadAllText((Join-Path $settingsRoot 'hotkeys.js'), [Text.UTF8Encoding]::new($false, $true))
$settingsWindow = [IO.File]::ReadAllText((Join-Path $appRoot 'SettingsWindow.xaml.cs'), [Text.UTF8Encoding]::new($false, $true))
$app = [IO.File]::ReadAllText((Join-Path $appRoot 'App.xaml.cs'), [Text.UTF8Encoding]::new($false, $true))
$appSettings = [IO.File]::ReadAllText((Join-Path $appRoot 'AppSettings.cs'), [Text.UTF8Encoding]::new($false, $true))
$lyricsWindow = [IO.File]::ReadAllText((Join-Path $appRoot 'MainWindow.xaml.cs'), [Text.UTF8Encoding]::new($false, $true))
$lyricsWindowHost = [IO.File]::ReadAllText((Join-Path $appRoot 'LyricsWindowHost.cs'), [Text.UTF8Encoding]::new($false, $true))
$lyricsStyleFactory = [IO.File]::ReadAllText((Join-Path $appRoot 'LyricsStyleScriptFactory.cs'), [Text.UTF8Encoding]::new($false, $true))
$mediaHotkeyCatalog = [IO.File]::ReadAllText((Join-Path $appRoot 'MediaHotkeyCatalog.cs'), [Text.UTF8Encoding]::new($false, $true))

$errors = [Collections.Generic.List[string]]::new()

$pages = @('sources', 'shortcuts', 'lyrics', 'trackOffsets', 'displayArea', 'general', 'advanced', 'lyricDiagnostics', 'about')
foreach ($page in $pages) {
    if (-not $html.Contains("data-nav=`"$page`"")) { $errors.Add("missing nav: $page") }
    if (-not $html.Contains("data-page=`"$page`"")) { $errors.Add("missing page: $page") }
}

$settings = @(
    'enableLocalLyrics', 'localMusicFolders', 'enableGlobalMediaHotkeys', 'showLyricsOnStartup', 'showLyricTranslation', 'enableWordScanning',
    'spectrumDisplayMode', 'lyricsLayoutScalePercent', 'fontSize', 'showCover',
    'coverSize', 'coverGap', 'coverCornerRadius', 'fontFamily',
    'fontWeight', 'foregroundColorMode', 'showTextShadow', 'toolWindowTheme', 'showBackground',
    'backgroundOpacity', 'showBorder', 'windowWidth', 'horizontalAnchor', 'xOffset',
    'yOffset', 'forceAlwaysOnTop', 'startWithWindows', 'autoCheckUpdates'
)
foreach ($key in $settings) {
    if (-not $html.Contains("data-setting=`"$key`"")) { $errors.Add("missing setting control: $key") }
}

$settingsWithoutDescriptions = @(
    'showLyricsOnStartup', 'fontFamily', 'fontWeight',
    'foregroundColorMode', 'showTextShadow', 'showBackground', 'showBorder',
    'startWithWindows'
)
foreach ($key in $settingsWithoutDescriptions) {
    $escapedKey = [Regex]::Escape($key)
    $descriptionFreeRow = '<div class="setting-row[^"]*"><div class="setting-label"><strong>[^<]+</strong></div>.*?data-setting="' + $escapedKey + '"'
    if ($html -notmatch $descriptionFreeRow) {
        $errors.Add("setting description should remain removed: $key")
    }
}

if ($script.Contains('definition.description')) { $errors.Add('media hotkey description rendering should remain removed') }
if ($settingsWindow.Contains('Description = definition.Description')) { $errors.Add('media hotkey description should not enter the web state payload') }
if ($mediaHotkeyCatalog.Contains('string Description')) { $errors.Add('media hotkey catalog description field should remain removed') }

$requiredHtml = @(
    'id="sourceGrid"', 'id="priorityList"', 'id="mediaHotkeyList"', 'id="selectPopover"', 'role="listbox"',
    'id="colorPopover"', 'id="colorArea"', 'id="restoreDialog"', 'id="clearDialog"',
    'id="playerSettingsDialog"', 'id="playerRecognitionToggle"', 'id="playerOffsetInput"',
    'id="currentTrackOffset"', 'id="trackOffsetList"', 'id="trackOffsetPagination"', 'id="clearTrackOffsetsDialog"',
    'id="runLyricDiagnosticsButton"', 'id="lyricDiagnosticsStatus"', 'id="lyricDiagnosticsReportSummary"', 'id="lyricDiagnosticsProviders"',
    'id="lyricDiagnosticsSelection"', 'id="spectrumAudioAccessStatus"', 'id="retrySpectrumAudioAccessButton"', 'id="revokeSpectrumAudioAccessButton"',
    'id="spectrumTuningDescription"', 'aria-describedby="spectrumTuningDescription"',
    'id="spectrumAudioConsentDialog"', 'id="confirmSpectrumAudioAccess"',
    'id="spectrumCaptureFailureDialog"', 'id="retrySpectrumCaptureButton"', 'id="disableSpectrumButton"',
    'id="browseButton"', 'id="showLyricsWindowButton"', 'data-reset-layout-scale',
    'name="lyricsDisplayMode"', 'id="displayMonitorList"', 'data-display-mode',
    'data-reset-layout-base', 'id="layoutScalePreview"', 'data-window-resize="top"',
    'class="slider-number-control"', 'compact-number-input', 'id="hueNumberInput"',
    'type="range" min="-2000" max="2000" step="1" data-setting="xOffset"',
    'type="number" min="-2000" max="2000" step="1" inputmode="numeric" data-setting="xOffset"',
    'type="range" min="-2000" max="2000" step="1" data-setting="yOffset"',
    'type="number" min="-2000" max="2000" step="1" inputmode="numeric" data-setting="yOffset"'
)
foreach ($marker in $requiredHtml) {
    if (-not $html.Contains($marker)) { $errors.Add("missing html marker: $marker") }
}
foreach ($key in @('lyricsLayoutScalePercent', 'coverGap', 'coverCornerRadius', 'backgroundOpacity', 'xOffset', 'yOffset', 'windowWidth')) {
    if ([regex]::Matches($html, "type=`"range`"[^>]+data-setting=`"$key`"").Count -ne 1) { $errors.Add("missing unique slider: $key") }
    if ([regex]::Matches($html, "type=`"number`"[^>]+data-setting=`"$key`"").Count -ne 1) { $errors.Add("missing unique numeric pair: $key") }
}

if ([regex]::IsMatch($html, '<select\b', 'IgnoreCase')) { $errors.Add('native select remains') }
if ([regex]::IsMatch($html, 'input[^>]+type="color"', 'IgnoreCase')) { $errors.Add('native color input remains') }

$requiredScript = @(
    'window.settingsApp = { receive }', 'function receive(message)',
    'type: "reorderSources"', 'type: "pickLocalFolder"', 'type: "showLyricsWindow"',
    'type: "openSmtcMonitor"', 'type: "openSpectrumTuning"',
    'type: "confirmSpectrumAudioAccess"', 'type: "revokeSpectrumAudioAccess"',
    'type: "retrySpectrumCapture"', 'type: "disableSpectrum"',
    'type: "runLyricDiagnostics"',
    'type: "windowDrag"', 'type: "windowResizeStart"', 'type: "windowMinimize"', 'type: "windowMaximize"', 'type: "windowClose"',
    'function openSelect', 'function closeSelect', 'function rgbToHex', 'function toArgb',
    'function activatePage', 'function renderSources', 'function renderPriority', 'function setWindowState',
    'function openPlayerSettings', 'function commitPlayerOffset', 'playerLyricOffset:',
    'function renderMediaHotkeys', 'function beginHotkeyRecording', 'function getRecordedHotkey', 'type: "resetMediaHotkey"',
    'function renderTrackOffsets', 'function commitCurrentTrackOffset', 'function setCurrentTrackOffsetData',
    'function renderDisplayMonitors', 'commitSetting("lyricsDisplayMode"', 'commitSetting("selectedDisplayIds"',
    'function setTrackOffsetEntries', 'function requestTrackOffsetPage', 'function changeTrackOffsetPage',
    'function setLyricDiagnosticsState', 'function renderLyricDiagnosticsProviders',
    'function renderLyricDiagnosticsVariants', 'function renderLyricDiagnosticsSelection',
    'type: "queryTrackOffsets"',
    'type: "setCurrentTrackOffset"', 'type: "setStoredTrackOffset"', 'type: "deleteTrackOffset"',
    'function positionPopover', 'function postSourceOrder', 'function updateLayoutPreview', 'function readSettingControlValue',
    'function setSettingsSaveResult', 'case "settingsSaveResult"',
    'type: "resetLyricsLayoutBase"', 'type: "previewUpdate"', '"ArrowDown"', '"Home"', '"Escape"'
)
foreach ($marker in $requiredScript) {
    if (-not $script.Contains($marker)) { $errors.Add("missing script marker: $marker") }
}

foreach ($marker in @('version: VERSION', 'payload: toPayload(message)', 'type: message.type')) {
    if (-not $bridge.Contains($marker)) { $errors.Add("missing V1 bridge marker: $marker") }
}
foreach ($marker in @('const labels =', 'registered:', 'duplicate:', 'visualState(state)')) {
    if (-not $hotkeyScript.Contains($marker)) { $errors.Add("missing hotkey status presentation: $marker") }
}

$supportedSources = @('QQMusic', 'Netease', 'Kugou', 'Spotify')
foreach ($source in $supportedSources) {
    if (-not $script.Contains("adapter: `"$source`"")) { $errors.Add("missing source: $source") }
}
foreach ($unsupported in @('AppleMusic', 'Foobar', 'MusicBee', 'AIMP', 'VLC', 'Winamp', 'Tidal', 'GenericSMTC')) {
    if ($script.Contains($unsupported)) { $errors.Add("unsupported source exposed: $unsupported") }
}

foreach ($marker in @('case "pickLocalFolder":', 'case "showLyricsWindow":', 'case "openSmtcMonitor":', 'case "openSpectrumTuning":', 'case "confirmSpectrumAudioAccess":', 'case "revokeSpectrumAudioAccess":', 'case "retrySpectrumCapture":', 'case "disableSpectrum":', 'case "runLyricDiagnostics":', 'case "settingsPageChanged":', 'case "queryTrackOffsets":', 'case "setCurrentTrackOffset":', 'case "setStoredTrackOffset":', 'case "deleteTrackOffset":', 'case "clearTrackOffsets":', 'case "resetMediaHotkey":', 'case "resetLyricsLayoutBase":', 'case "previewUpdate":', 'case "windowDrag":', 'case "windowResizeStart":', 'case "windowClose":')) {
    if (-not $settingsWindow.Contains($marker)) { $errors.Add("missing desktop message: $marker") }
}
if (-not $settingsWindow.Contains('"settingsSaveResult"')) { $errors.Add('missing settings save result dispatch') }
if (-not $settingsWindow.Contains('"lyricDiagnosticsState"')) { $errors.Add('missing lyric diagnostics state dispatch') }
if (-not $settingsWindow.Contains('"requestSpectrumDisplayMode"')) { $errors.Add('missing spectrum mode request dispatch') }
if (-not $settingsWindow.Contains('"spectrumCaptureState"')) { $errors.Add('missing spectrum capture state dispatch') }
if (-not $app.Contains('public void ShowLyricsWindow()')) { $errors.Add('missing App.ShowLyricsWindow') }
if (-not $appSettings.Contains('public GlobalMediaHotkeySettings GlobalMediaHotkeys')) { $errors.Add('global media hotkeys settings missing') }
if (-not $appSettings.Contains('public double LyricsLayoutScalePercent')) { $errors.Add('lyrics layout scale setting missing') }
if (-not $appSettings.Contains('public bool ShowCover')) { $errors.Add('show cover setting missing') }
if (-not $appSettings.Contains('public bool SpectrumAudioAccessGranted')) { $errors.Add('spectrum audio access setting missing') }
if (-not $appSettings.Contains('public LyricsDisplayMode LyricsDisplayMode')) { $errors.Add('lyrics display mode setting missing') }
if (-not $appSettings.Contains('public List<string> SelectedDisplayIds')) { $errors.Add('selected display ids setting missing') }
if (-not $appSettings.Contains('public const string DefaultFontFamily = BundledFontFamily;')) { $errors.Add('bundled font is not the default') }
if (-not $app.Contains('Settings.FontFamily = AppSettings.NormalizeFontFamily(Settings.FontFamily);')) { $errors.Add('startup font normalization missing') }
if (-not $lyricsStyleFactory.Contains('fontFamily = AppSettings.NormalizeFontFamily(settings.FontFamily)')) { $errors.Add('lyrics font normalization missing') }
if (-not $lyricsStyleFactory.Contains('showCover = settings.ShowCover')) { $errors.Add('lyrics cover visibility payload missing') }
if (-not $lyricsStyleFactory.Contains('WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "style"')) { $errors.Add('lyrics V1 style dispatch missing') }
if (-not $lyricsWindowHost.Contains('LyricsDisplayTargetSelector.Select(')) { $errors.Add('lyrics display target reconciliation missing') }
if (-not $lyricsWindowHost.Contains('SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;')) { $errors.Add('lyrics display hotplug handling missing') }

$lyricsScript = [IO.File]::ReadAllText((Join-Path $appRoot 'Web\Lyrics\app.js'), [Text.UTF8Encoding]::new($false, $true))
$lyricsCss = [IO.File]::ReadAllText((Join-Path $appRoot 'Web\Lyrics\style.css'), [Text.UTF8Encoding]::new($false, $true))
if (-not $lyricsScript.Contains('root.classList.toggle("cover-hidden", payload.showCover === false)')) { $errors.Add('lyrics cover visibility behavior missing') }
if (-not $lyricsCss.Contains('.cover-hidden :is(.cover, .cover-gap)')) { $errors.Add('lyrics hidden cover style missing') }

$lyricsHtml = [IO.File]::ReadAllText((Join-Path $appRoot 'Web\Lyrics\index.html'), [Text.UTF8Encoding]::new($false, $true))
if (-not $lyricsHtml.Contains('class="cover-gap" aria-hidden="true"')) { $errors.Add('lyrics cover gap marker missing') }

if (-not $css.Contains('--background: oklch(0.145 0 0)')) { $errors.Add('neutral palette missing') }
if ($css.Contains('Settings prototype integration: neutral Shadcn-inspired control layer.')) { $errors.Add('legacy override layer remains') }
foreach ($marker in @('.sidebar-collapsed', '.page.transitioning', '.setting-row.child', '.theme-segmented', '.about-layout', '.color-popover')) {
    if (-not $css.Contains($marker)) { $errors.Add("missing prototype style: $marker") }
}
if (-not $script.Contains('{ value: "Disabled"')) { $errors.Add('spectrum disabled option missing') }
if ($html.Contains('data-setting="enableSpectrum"')) { $errors.Add('legacy spectrum switch remains') }
if ($html.Contains('data-setting="enableSmtcTimelineMonitor"')) { $errors.Add('legacy SMTC monitor switch remains') }
if ($html.Contains('data-setting="useSafeFontSizeRange"') -or $html.Contains('data-setting="useSafeCoverSizeRange"')) { $errors.Add('legacy visual safe-range controls remain') }
foreach ($demoMarker in @('settingsPrototype', 'demoFonts')) {
    if ($html.Contains($demoMarker) -or $script.Contains($demoMarker)) { $errors.Add("demo marker remains: $demoMarker") }
}

if ($errors.Count -gt 0) {
    Write-Error ("SETTINGS CONTRACT FAILED`n - " + ($errors -join "`n - "))
    exit 1
}

Write-Output 'PASS: full settings prototype contract'
