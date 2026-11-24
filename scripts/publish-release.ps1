[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Force,
    [switch]$Installer,
    [string]$InnoSetupCompiler
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
$installerName = "$packageName-Setup.exe"
$installerPath = Join-Path $publishRoot $installerName
$buildInstaller = $Installer.IsPresent -or -not [string]::IsNullOrWhiteSpace($InnoSetupCompiler)
$releaseTargets = @($outputDirectory, $archivePath)
if ($buildInstaller) {
    $releaseTargets += $installerPath
}

if (-not $Force -and ($releaseTargets | Where-Object { Test-Path -LiteralPath $_ })) {
    throw "Release artifacts for version $Version already exist. Use -Force to replace them."
}

if ($buildInstaller) {
    if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
        $InnoSetupCompiler = (Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1).Source
        if (-not $InnoSetupCompiler) {
            $InnoSetupCompiler = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ } |
                ForEach-Object { Join-Path $_ "Inno Setup 7\ISCC.exe" } |
                Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        }
    }
    if (-not $InnoSetupCompiler -or -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
        throw "Inno Setup 7 ISCC.exe is required. Install it or specify -InnoSetupCompiler."
    }
    $InnoSetupCompiler = (Resolve-Path -LiteralPath $InnoSetupCompiler).ProviderPath
}

$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ("TaskbarLyrics-release-" + [Guid]::NewGuid().ToString("N"))
$stagingPackage = Join-Path $stagingRoot $packageName
$stagingArchive = Join-Path $stagingRoot "$packageName.zip"
$stagingInstaller = Join-Path $stagingRoot $installerName

New-Item -ItemType Directory -Path $stagingPackage -Force | Out-Null

try {
    $updaterOutput = Join-Path $stagingRoot "updater"
    & dotnet publish (Join-Path $repoRoot "TaskbarLyrics.Updater\TaskbarLyrics.Updater.csproj") `
        -c Release -r win-x64 -p:PublishAot=true -p:TreatWarningsAsErrors=true `
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

    if ($buildInstaller) {
        Write-Host "Building the Windows installer..." -ForegroundColor Cyan
        & $InnoSetupCompiler "/DAppVersion=$Version" "/DPayloadDir=$stagingPackage" "/O$stagingRoot" `
            (Join-Path $repoRoot "installer\TaskbarLyrics.iss")
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $stagingInstaller -PathType Leaf)) {
            throw "Installer build failed: $LASTEXITCODE. Expected $stagingInstaller."
        }
    }

    if ($Force) {
        $resolvedPublish = [IO.Path]::GetFullPath($publishRoot).TrimEnd('\') + '\'
        foreach ($target in $releaseTargets) {
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
        if ($buildInstaller -and (Test-Path -LiteralPath $installerPath)) {
            Remove-Item -LiteralPath $installerPath -Force
        }
    }

    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    Move-Item -LiteralPath $stagingPackage -Destination $outputDirectory
    Move-Item -LiteralPath $stagingArchive -Destination $archivePath
    if ($buildInstaller) {
        Move-Item -LiteralPath $stagingInstaller -Destination $installerPath
    }

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
    if ($buildInstaller) {
        Write-Host "Installer: $installerPath"
        Write-Host "Installer SHA256: $((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant())"
    }
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
