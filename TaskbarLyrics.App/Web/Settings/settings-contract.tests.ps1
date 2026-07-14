$ErrorActionPreference = 'Stop'

$settingsRoot = $PSScriptRoot
$appRoot = Resolve-Path (Join-Path $settingsRoot '..\..')
$html = [IO.File]::ReadAllText((Join-Path $settingsRoot 'settings.html'), [Text.UTF8Encoding]::new($false, $true))
$css = [IO.File]::ReadAllText((Join-Path $settingsRoot 'settings.css'), [Text.UTF8Encoding]::new($false, $true))
$script = [IO.File]::ReadAllText((Join-Path $settingsRoot 'settings.js'), [Text.UTF8Encoding]::new($false, $true))
$settingsWindow = [IO.File]::ReadAllText((Join-Path $appRoot 'SettingsWindow.xaml.cs'), [Text.UTF8Encoding]::new($false, $true))
$app = [IO.File]::ReadAllText((Join-Path $appRoot 'App.xaml.cs'), [Text.UTF8Encoding]::new($false, $true))

$errors = [Collections.Generic.List[string]]::new()

$pages = @('sources', 'lyrics', 'appearance', 'window', 'general', 'advanced', 'about')
foreach ($page in $pages) {
    if (-not $html.Contains("data-nav=`"$page`"")) { $errors.Add("missing nav: $page") }
    if (-not $html.Contains("data-page=`"$page`"")) { $errors.Add("missing page: $page") }
}

$settings = @(
    'enableLocalLyrics', 'localMusicFolders', 'showLyricsOnStartup', 'showLyricTranslation',
    'enableSpectrum', 'spectrumDisplayMode', 'useSafeFontSizeRange', 'fontSize',
    'useSafeCoverSizeRange', 'coverSize', 'coverGap', 'coverCornerRadius', 'fontFamily',
    'fontWeight', 'foregroundColorMode', 'showTextShadow', 'showBackground',
    'backgroundOpacity', 'showBorder', 'windowWidth', 'horizontalAnchor', 'xOffset',
    'yOffset', 'forceAlwaysOnTop', 'startWithWindows', 'autoCheckUpdates',
    'enableSmtcTimelineMonitor'
)
foreach ($key in $settings) {
    if (-not $html.Contains("data-setting=`"$key`"")) { $errors.Add("missing setting control: $key") }
}

$requiredHtml = @(
    'id="sourceGrid"', 'id="priorityList"', 'id="selectPopover"', 'role="listbox"',
    'id="colorPopover"', 'id="colorArea"', 'id="restoreDialog"', 'id="clearDialog"',
    'id="browseButton"', 'id="showLyricsWindowButton"'
)
foreach ($marker in $requiredHtml) {
    if (-not $html.Contains($marker)) { $errors.Add("missing html marker: $marker") }
}

if ([regex]::IsMatch($html, '<select\b', 'IgnoreCase')) { $errors.Add('native select remains') }
if ([regex]::IsMatch($html, 'input[^>]+type="color"', 'IgnoreCase')) { $errors.Add('native color input remains') }

$requiredScript = @(
    'window.settingsApp = { setState, setUpdateStatus }', 'window.settingsApp.setWindowState = setWindowState',
    'window.chrome?.webview?.postMessage',
    'type: "reorderSources"', 'type: "pickLocalFolder"', 'type: "showLyricsWindow"',
    'type: "windowDrag"', 'type: "windowMinimize"', 'type: "windowMaximize"', 'type: "windowClose"',
    'function openSelect', 'function closeSelect', 'function rgbToHex', 'function toArgb',
    'function activatePage', 'function renderSources', 'function renderPriority', 'function setWindowState',
    'function positionPopover', 'function postSourceOrder', '"ArrowDown"', '"Home"', '"Escape"'
)
foreach ($marker in $requiredScript) {
    if (-not $script.Contains($marker)) { $errors.Add("missing script marker: $marker") }
}

$supportedSources = @('QQMusic', 'Netease', 'Kugou', 'Spotify')
foreach ($source in $supportedSources) {
    if (-not $script.Contains("adapter: `"$source`"")) { $errors.Add("missing source: $source") }
}
foreach ($unsupported in @('AppleMusic', 'Foobar', 'MusicBee', 'AIMP', 'VLC', 'Winamp', 'Tidal', 'GenericSMTC')) {
    if ($script.Contains($unsupported)) { $errors.Add("unsupported source exposed: $unsupported") }
}

foreach ($marker in @('case "pickLocalFolder":', 'case "showLyricsWindow":', 'case "windowDrag":', 'case "windowClose":')) {
    if (-not $settingsWindow.Contains($marker)) { $errors.Add("missing desktop message: $marker") }
}
if (-not $app.Contains('public void ShowLyricsWindow()')) { $errors.Add('missing App.ShowLyricsWindow') }

if (-not $css.Contains('--background: oklch(0.145 0 0)')) { $errors.Add('neutral palette missing') }
if ($css.Contains('Settings prototype integration: neutral Shadcn-inspired control layer.')) { $errors.Add('legacy override layer remains') }
foreach ($marker in @('.sidebar-collapsed', '.page.transitioning', '.setting-row.child', '.about-layout', '.color-popover')) {
    if (-not $css.Contains($marker)) { $errors.Add("missing prototype style: $marker") }
}
foreach ($demoMarker in @('settingsPrototype', 'demoFonts')) {
    if ($html.Contains($demoMarker) -or $script.Contains($demoMarker)) { $errors.Add("demo marker remains: $demoMarker") }
}

if ($errors.Count -gt 0) {
    Write-Error ("SETTINGS CONTRACT FAILED`n - " + ($errors -join "`n - "))
    exit 1
}

Write-Output 'PASS: full settings prototype contract'
