$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appTests = Join-Path $repositoryRoot 'TaskbarLyrics.App.Tests\TaskbarLyrics.App.Tests.csproj'
$coreTests = Join-Path $repositoryRoot 'TaskbarLyrics.Core.Tests\TaskbarLyrics.Core.Tests.csproj'
$settingsContract = Join-Path $repositoryRoot 'TaskbarLyrics.App\Web\Settings\settings-contract.tests.ps1'

function Invoke-VerificationStep {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    Invoke-VerificationStep 'App unit tests' {
        dotnet test $appTests --no-restore -p:BaseOutputPath=build_verify_tests\
    }

    Invoke-VerificationStep 'Core unit tests' {
        dotnet test $coreTests --no-restore -p:BaseOutputPath=build_verify_tests\
    }

    Invoke-VerificationStep 'Settings contract test' {
        powershell -ExecutionPolicy Bypass -File $settingsContract
    }

    Write-Host 'Verification passed.'
}
finally {
    Pop-Location
}
