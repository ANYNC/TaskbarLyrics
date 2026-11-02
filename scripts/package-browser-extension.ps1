[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'tmp\browser-extension' }
& node (Join-Path $PSScriptRoot 'sync-browser-extension-locales.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Extension localization generation failed.' }
$source = Join-Path $repositoryRoot 'BrowserExtension'
$manifest = Get-Content (Join-Path $source 'manifest.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$destination = Join-Path $OutputDirectory "TaskbarLyrics-Browser-$($manifest.version).zip"
# Explicit runtime allowlist keeps docs, development tools and tests out of store packages.
$runtime = @('manifest.json', 'distribution.json', 'icon.png', 'rules.js', 'worker.js', 'relay.js', 'media-probe.js', 'popup.html', 'popup.css', 'popup.js', '_locales')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryPath = Join-Path $OutputDirectory ("browser-extension-$([Guid]::NewGuid().ToString('N')).tmp")
try {
    $stream = [System.IO.File]::Open($temporaryPath, [System.IO.FileMode]::CreateNew)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($item in $runtime) {
                $itemPath = Join-Path $source $item
                $files = if (Test-Path -LiteralPath $itemPath -PathType Container) {
                    Get-ChildItem -LiteralPath $itemPath -Recurse -File | Sort-Object FullName
                } else { Get-Item -LiteralPath $itemPath }
                foreach ($file in $files) {
                    # Chromium ZIP loading requires forward slashes, including on Windows.
                    $entryName = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
                    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                        $archive, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
                }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
    Move-Item -LiteralPath $temporaryPath -Destination $destination -Force
} finally {
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
}
Write-Output $destination
