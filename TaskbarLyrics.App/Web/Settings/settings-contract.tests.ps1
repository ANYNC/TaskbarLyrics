$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$html = [IO.File]::ReadAllText((Join-Path $root 'settings.html'), [Text.UTF8Encoding]::new($false, $true))
$script = [IO.File]::ReadAllText((Join-Path $root 'settings.js'), [Text.UTF8Encoding]::new($false, $true))
$window = [IO.File]::ReadAllText((Join-Path $root '..\..\SettingsWindow.xaml'), [Text.UTF8Encoding]::new($false, $true))

$requiredHtml = @('class="select-trigger"', 'id="selectPopover"', 'id="colorPopover"', 'id="colorArea"')
$requiredScript = @('window.settingsApp', 'window.chrome?.webview?.postMessage', 'foregroundColorMode', 'foregroundColor')
$requiredWindow = @('Background="#18181B"', 'DefaultBackgroundColor="#18181B"')
$missing = @($requiredHtml | Where-Object { -not $html.Contains($_) }) + @($requiredScript | Where-Object { -not $script.Contains($_) }) + @($requiredWindow | Where-Object { -not $window.Contains($_) })

if ($missing.Count -gt 0) {
    throw "设置页迁移契约缺失: $($missing -join ', ')"
}

Write-Output 'PASS: settings WebView bridge and custom controls are present'
