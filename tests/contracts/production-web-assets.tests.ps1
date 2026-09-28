$ErrorActionPreference = 'Stop'

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$productionWebRoot = Join-Path $repositoryRoot 'TaskbarLyrics.App\Web'
$developmentArtifacts = Get-ChildItem -LiteralPath $productionWebRoot -Recurse -File | Where-Object {
    $_.Name -match '\.(tests?|spec)\.' -or $_.Extension -eq '.ps1'
}

if ($developmentArtifacts.Count -gt 0) {
    $relativePaths = $developmentArtifacts | ForEach-Object {
        [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName)
    }
    Write-Error ("Development artifacts are inside production Web assets:`n - " + ($relativePaths -join "`n - "))
    exit 1
}

# Settings page behavior and DOM contracts are exercised by Vitest/jsdom; this
# script retains only the release-boundary check that scans the production tree.
Write-Output 'PASS: production Web assets contain no tests or PowerShell scripts'
