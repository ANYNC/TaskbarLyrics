<#
.SYNOPSIS
Runs TaskbarLyrics verification at the scope appropriate for the current development stage.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts/verify.ps1 -Tier Targeted -Area Core -Filter FullyQualifiedName~LyricResolutionCoordinatorTests

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts/verify.ps1 -Tier Targeted -Area Web -Filter tests/web/bridge.test.js

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts/verify.ps1 -Tier Project -Area Core,App

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts/verify.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Targeted', 'Project', 'Full')]
    [string]$Tier = 'Full',

    [string]$Area,

    [string]$Filter
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'verify-output.ps1')
$appTests = Join-Path $repositoryRoot 'TaskbarLyrics.App.Tests\TaskbarLyrics.App.Tests.csproj'
$coreTests = Join-Path $repositoryRoot 'TaskbarLyrics.Core.Tests\TaskbarLyrics.Core.Tests.csproj'
$settingsContract = Join-Path $repositoryRoot 'TaskbarLyrics.App\Web\Settings\settings-contract.tests.ps1'
$restartAppTests = Join-Path $repositoryRoot 'scripts\restart-app.tests.ps1'
$verificationOutputTests = Join-Path $repositoryRoot 'scripts\verify-output.tests.ps1'
$webDependencies = Join-Path $repositoryRoot 'node_modules'
$verificationLogRoot = Join-Path $repositoryRoot 'tmp\verify-logs'
$verificationContext = New-VerificationLogContext -RepositoryRoot $repositoryRoot -LogRoot $verificationLogRoot
$script:verificationFailureReported = $false
$allowedAreas = @('Core', 'App', 'Web', 'Settings')
$requestedAreas = @(
    $Area -split ',' |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)

function Assert-VerificationArguments {
    foreach ($requestedArea in $requestedAreas) {
        if ($requestedArea -notin $allowedAreas) {
            throw "Unknown verification area '$requestedArea'. Expected one of: $($allowedAreas -join ', ')."
        }
    }

    if ($Tier -eq 'Full') {
        if ($requestedAreas.Count -gt 0 -or -not [string]::IsNullOrWhiteSpace($Filter)) {
            throw 'Full verification does not accept -Area or -Filter.'
        }

        return
    }

    if ($requestedAreas.Count -eq 0) {
        throw "$Tier verification requires at least one -Area value."
    }

    if ($Tier -eq 'Targeted') {
        if ($requestedAreas.Count -ne 1) {
            throw 'Targeted verification accepts exactly one -Area value.'
        }

        if ($requestedAreas[0] -eq 'Settings' -and -not [string]::IsNullOrWhiteSpace($Filter)) {
            throw 'Targeted Settings verification does not accept -Filter.'
        }

        if ($requestedAreas[0] -ne 'Settings' -and [string]::IsNullOrWhiteSpace($Filter)) {
            throw 'Targeted Core, App, or Web verification requires -Filter.'
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($Filter)) {
        throw 'Project verification does not accept -Filter.'
    }
}

function Invoke-VerificationStep {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    try {
        Invoke-LoggedVerificationStep -Context $verificationContext -Name $Name -Action $Action
    }
    catch {
        # Invoke-LoggedVerificationStep has already emitted the bounded failure
        # report. The outer handler must not print a second report for the same
        # step, but it still rethrows so the script retains non-zero semantics.
        $script:verificationFailureReported = $true
        throw
    }
}

function Initialize-WebTestDependencies {
    if (-not (Test-Path $webDependencies)) {
        Invoke-VerificationStep 'Install web test dependencies' {
            npm ci --ignore-scripts
        }
    }
}

function Invoke-WebBehaviorTests {
    param([string]$TestFilter)

    Initialize-WebTestDependencies
    if ([string]::IsNullOrWhiteSpace($TestFilter)) {
        Invoke-VerificationStep 'Web behavior tests' {
            npm run test:web
        }
    }
    else {
        Invoke-VerificationStep "Web targeted tests: $TestFilter" {
            npm run test:web -- $TestFilter
        }
    }
}

function Invoke-DotNetTests {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$Project,
        [string]$TestFilter
    )

    if ([string]::IsNullOrWhiteSpace($TestFilter)) {
        Invoke-VerificationStep $Name {
            dotnet test $Project --no-restore -p:BaseOutputPath=build_verify_tests\
        }
    }
    else {
        Invoke-VerificationStep "$Name targeted tests: $TestFilter" {
            dotnet test $Project --no-restore -p:BaseOutputPath=build_verify_tests\ --filter $TestFilter
        }
    }
}

function Invoke-SettingsContractTest {
    Invoke-VerificationStep 'Settings contract test' {
        powershell -ExecutionPolicy Bypass -File $settingsContract
    }
}

function Invoke-TargetedVerification {
    switch ($requestedAreas[0]) {
        'Core' { Invoke-DotNetTests 'Core unit tests' $coreTests $Filter }
        'App' { Invoke-DotNetTests 'App unit tests' $appTests $Filter }
        'Web' { Invoke-WebBehaviorTests $Filter }
        'Settings' { Invoke-SettingsContractTest }
    }
}

function Invoke-ProjectVerification {
    $selectedAreas = @($requestedAreas | Select-Object -Unique)
    $verifySettings = $selectedAreas -contains 'Settings'

    if ($selectedAreas -contains 'Web' -or $verifySettings) {
        Invoke-WebBehaviorTests
    }

    if ($selectedAreas -contains 'App' -or $verifySettings) {
        Invoke-DotNetTests 'App unit tests' $appTests
    }

    if ($selectedAreas -contains 'Core') {
        Invoke-DotNetTests 'Core unit tests' $coreTests
    }

    if ($verifySettings) {
        Invoke-SettingsContractTest
    }
}

function Invoke-FullVerification {
    Invoke-WebBehaviorTests
    Invoke-DotNetTests 'App unit tests' $appTests
    Invoke-DotNetTests 'Core unit tests' $coreTests
    Invoke-SettingsContractTest

    Invoke-VerificationStep 'Code format verification' {
        dotnet format TaskbarLyrics.sln --verify-no-changes --no-restore
    }

    Invoke-VerificationStep 'Verification output regression tests' {
        powershell -ExecutionPolicy Bypass -File $verificationOutputTests
    }

    Invoke-VerificationStep 'Restart script regression tests' {
        powershell -NoProfile -ExecutionPolicy Bypass -File $restartAppTests
    }
}

Push-Location $repositoryRoot
try {
    Assert-VerificationArguments
    switch ($Tier) {
        'Targeted' { Invoke-TargetedVerification }
        'Project' { Invoke-ProjectVerification }
        'Full' { Invoke-FullVerification }
    }

}
catch {
    if (-not $script:verificationFailureReported) {
        Write-VerificationUnhandledFailure -Context $verificationContext -ErrorRecord $_
    }

    # Keep the terminal report bounded while preserving the process failure
    # contract expected by callers of verify.ps1.
    exit 1
}
finally {
    Pop-Location
}
