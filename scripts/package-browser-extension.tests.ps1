[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repositoryRoot 'tmp\browser-extension-package-test'
$package = & (Join-Path $PSScriptRoot 'package-browser-extension.ps1') -OutputDirectory $outputDirectory

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

# Read original central-directory and local-header bytes. Some ZIP readers on
# Windows silently normalize backslashes, masking an archive Chromium rejects.
$bytes = [System.IO.File]::ReadAllBytes($package)
$end = $bytes.Length - 22
while ($end -ge 0 -and [BitConverter]::ToUInt32($bytes, $end) -ne 0x06054b50) { $end-- }
Assert-Condition ($end -ge 0) 'ZIP end record is missing.'
$count = [BitConverter]::ToUInt16($bytes, $end + 10)
$position = [int][BitConverter]::ToUInt32($bytes, $end + 16)
$names = @()
for ($index = 0; $index -lt $count; $index++) {
    Assert-Condition ([BitConverter]::ToUInt32($bytes, $position) -eq 0x02014b50) 'Invalid ZIP central-directory record.'
    $nameLength = [BitConverter]::ToUInt16($bytes, $position + 28)
    $extraLength = [BitConverter]::ToUInt16($bytes, $position + 30)
    $commentLength = [BitConverter]::ToUInt16($bytes, $position + 32)
    $name = [System.Text.Encoding]::UTF8.GetString($bytes, $position + 46, $nameLength)
    Assert-Condition (-not $name.Contains('\')) "ZIP contains a Windows path: $name"
    Assert-Condition (-not ($name.StartsWith('/') -or $name.Contains('../'))) "ZIP contains an unsafe path: $name"
    $local = [int][BitConverter]::ToUInt32($bytes, $position + 42)
    Assert-Condition ([BitConverter]::ToUInt32($bytes, $local) -eq 0x04034b50) 'Invalid ZIP local header.'
    $localNameLength = [BitConverter]::ToUInt16($bytes, $local + 26)
    $localName = [System.Text.Encoding]::UTF8.GetString($bytes, $local + 30, $localNameLength)
    Assert-Condition ($localName -ceq $name) 'ZIP central-directory and local-header names differ.'
    $names += $name
    $position += 46 + $nameLength + $extraLength + $commentLength
}
foreach ($required in @('manifest.json', 'worker.js', 'popup.js', '_locales/en/messages.json', '_locales/zh_CN/messages.json', '_locales/zh_TW/messages.json')) {
    Assert-Condition ($names -ccontains $required) "Missing required runtime asset: $required"
}
Assert-Condition (@($names | Where-Object { $_ -match '(node_modules|\.test\.|\.md$)' }).Count -eq 0) 'Development assets entered the ZIP.'
$archive = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach ($entry in $archive.Entries) {
        $inputStream = $entry.Open()
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $actual = [Convert]::ToBase64String($sha.ComputeHash($inputStream))
            $sourcePath = Join-Path (Join-Path $repositoryRoot 'BrowserExtension') $entry.FullName.Replace('/', '\')
            $expected = [Convert]::ToBase64String($sha.ComputeHash([System.IO.File]::ReadAllBytes($sourcePath)))
        } finally { $inputStream.Dispose(); $sha.Dispose() }
        Assert-Condition ($actual -ceq $expected) "Packaged bytes differ from source: $($entry.FullName)"
    }
} finally { $archive.Dispose() }
Write-Output "PASS browser extension packaging: $count runtime files; raw ZIP paths and source bytes verified."
