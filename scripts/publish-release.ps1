[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "TaskbarLyrics.App\TaskbarLyrics.App.csproj"
$publishRoot = Join-Path $repoRoot "publish"
$executableName = "TaskbarLyrics"
$projectExecutableName = "TaskbarLyrics.App.exe"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Read-Host "Release version (for example 1.2.0)"
}

$Version = $Version.Trim()
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Invalid version: $Version. Use x.x.x format, for example 1.2.0."
}

$packageName = "TaskbarLyrics-$Version"
$outputDirectory = Join-Path $publishRoot $packageName
$archivePath = Join-Path $publishRoot "$packageName.zip"

if (-not $Force -and ((Test-Path $outputDirectory) -or (Test-Path $archivePath))) {
    throw "Release artifacts for version $Version already exist. Use -Force to replace them."
}

$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ("TaskbarLyrics-release-" + [Guid]::NewGuid().ToString("N"))
$stagingPackage = Join-Path $stagingRoot $packageName
$stagingArchive = Join-Path $stagingRoot "$packageName.zip"

New-Item -ItemType Directory -Path $stagingPackage -Force | Out-Null

try {
    $updaterOutput = Join-Path $stagingRoot "updater"
    & dotnet publish (Join-Path $repoRoot "TaskbarLyrics.Updater\TaskbarLyrics.Updater.csproj") `
        -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $updaterOutput
    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed: $LASTEXITCODE" }
    $updaterPayload = Join-Path $updaterOutput "TaskbarLyrics.Updater.exe"
    if (-not (Test-Path -LiteralPath $updaterPayload)) { throw "Missing updater payload." }
    Write-Host "Publishing TaskbarLyrics $Version..." -ForegroundColor Cyan
    & dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        "-p:UpdaterPayload=$updaterPayload" `
        -o $stagingPackage

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $projectExecutablePath = Join-Path $stagingPackage $projectExecutableName
    if (-not (Test-Path $projectExecutablePath)) {
        throw "Published executable was not found: $projectExecutablePath"
    }

    $executablePath = Join-Path $stagingPackage "$executableName.exe"
    Rename-Item -LiteralPath $projectExecutablePath -NewName "$executableName.exe"
    if (-not (Test-Path $executablePath)) {
        throw "Failed to rename published executable to: $executablePath"
    }

    Write-Host "Creating ZIP archive..." -ForegroundColor Cyan
    Compress-Archive -Path $stagingPackage -DestinationPath $stagingArchive -CompressionLevel Optimal
    & $updaterPayload --validate-package $stagingArchive $Version
    if ($LASTEXITCODE -ne 0) { throw "Release package validation failed: $LASTEXITCODE" }

    if ($Force) {
        $resolvedPublish = [IO.Path]::GetFullPath($publishRoot).TrimEnd('\') + '\'
        foreach ($target in @($outputDirectory, $archivePath)) {
            $resolvedTarget = [IO.Path]::GetFullPath($target)
            if (-not $resolvedTarget.StartsWith($resolvedPublish, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Unsafe release replacement path: $resolvedTarget"
            }
        }
        if (Test-Path $outputDirectory) {
            Remove-Item -LiteralPath $outputDirectory -Recurse -Force
        }
        if (Test-Path $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }
    }

    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    Move-Item -LiteralPath $stagingPackage -Destination $outputDirectory
    Move-Item -LiteralPath $stagingArchive -Destination $archivePath

    $projectText = [IO.File]::ReadAllText($projectPath)
    $versionMatches = [regex]::Matches($projectText, '<Version>[^<]+</Version>')
    if ($versionMatches.Count -ne 1) {
        throw "Expected exactly one <Version> element in the project file. The source version was not updated."
    }

    $updatedProjectText = [regex]::Replace(
        $projectText,
        '<Version>[^<]+</Version>',
        "<Version>$Version</Version>",
        1)
    [IO.File]::WriteAllText($projectPath, $updatedProjectText, [Text.UTF8Encoding]::new($false))

    Write-Host "Release completed." -ForegroundColor Green
    Write-Host "Version: $Version"
    Write-Host "Directory: $outputDirectory"
    Write-Host "Archive: $archivePath"
    Write-Host "SHA256: $((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant())"
    Write-Host "Project version updated: $projectPath"
}
finally {
    if (Test-Path $stagingRoot) {
        $resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
        $expectedCacheRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $resolvedStaging.StartsWith($expectedCacheRoot, [StringComparison]::OrdinalIgnoreCase) -or
            (Split-Path -Leaf $resolvedStaging) -notmatch '^TaskbarLyrics-release-[0-9a-f]{32}$') {
            throw "Unsafe staging cleanup path: $resolvedStaging"
        }
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
