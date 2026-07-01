param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent,
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

$lightRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $lightRoot
$projectPath = Join-Path $lightRoot "TaskbarLyrics.Light.App\TaskbarLyrics.Light.App.csproj"
$publishRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repoRoot "publish"
} else {
    if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory
    } else {
        Join-Path $repoRoot $OutputDirectory
    }
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$versionNode = $projectXml.Project.PropertyGroup |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.Version) } |
    Select-Object -First 1
$version = $versionNode.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version is not set in $projectPath"
}

$packageBaseName = "TaskbarLyrics_light_v$version"
$stageDir = Join-Path $publishRoot $packageBaseName
$packagePath = Join-Path $publishRoot "$packageBaseName.zip"

function Assert-ChildPath {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullParent = $fullParent + [System.IO.Path]::DirectorySeparatorChar

    if (-not $fullPath.StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to touch path outside output directory: $fullPath"
    }
}

New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
Assert-ChildPath -Path $stageDir -Parent $publishRoot
Assert-ChildPath -Path $packagePath -Parent $publishRoot

if (Test-Path -LiteralPath $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

$selfContainedValue = if ($FrameworkDependent) { "false" } else { "true" }

& dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContainedValue `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $stageDir

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $packagePath -CompressionLevel Optimal -Force

Write-Host "Published Light package:"
Write-Host $packagePath
