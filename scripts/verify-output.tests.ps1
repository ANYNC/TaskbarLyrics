<#
.SYNOPSIS
Script-level regression checks for verify.ps1 output capture and reporting.

This file intentionally uses only PowerShell assertions so it can run before
the repository's normal test dependencies are available.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'verify-output.ps1')

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

function Get-SingleVerificationLog {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Context
    )

    $logs = @(Get-ChildItem -LiteralPath $Context.LogDirectory -Filter '*.log' -File)
    Assert-Condition ($logs.Count -eq 1) "Expected one log in $($Context.LogDirectory), found $($logs.Count)."
    return $logs[0]
}

$testRoot = Join-Path $repositoryRoot ('tmp\verify-output-script-tests-{0}-{1}' -f $PID, ([guid]::NewGuid().ToString('N')))
try {
    # A successful action keeps ordinary output in its log and exposes only its
    # compact aggregate summary to the terminal.
    $successContext = New-VerificationLogContext -RepositoryRoot $repositoryRoot -LogRoot $testRoot
    $successMessages = [System.Collections.Generic.List[string]]::new()
    & {
        Invoke-LoggedVerificationStep -Context $successContext -Name 'Success summary' -Action {
            Write-Output 'ordinary individual test output must remain in the log'
            Write-Output 'Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 10 ms'
            $global:LASTEXITCODE = 0
        }
    } 6>&1 | ForEach-Object { $successMessages.Add([string]$_) }
    $successReport = $successMessages -join [Environment]::NewLine
    Assert-Condition ($successReport -match '^PASS Success summary \| elapsed ') 'Success report is missing the step and elapsed time.'
    Assert-Condition ($successReport -match 'Passed! - Failed: 0, Passed: 2') 'Success report is missing the compact dotnet summary.'
    Assert-Condition ($successReport -notmatch 'ordinary individual test output') 'Success report leaked ordinary command output.'
    $successLog = Get-SingleVerificationLog $successContext
    $successLogText = Get-Content -Raw -LiteralPath $successLog.FullName
    Assert-Condition ($successLogText -match 'ordinary individual test output') 'Success log did not retain complete command output.'

    # A non-zero action reports the exit code, bounded tail evidence, and keeps
    # the full log available to the caller.
    $failureContext = New-VerificationLogContext -RepositoryRoot $repositoryRoot -LogRoot $testRoot
    $failureMessages = [System.Collections.Generic.List[string]]::new()
    $failureWasThrown = $false
    try {
        & {
            Invoke-LoggedVerificationStep -Context $failureContext -Name 'Failure tail' -Action {
                Write-Output 'critical tail evidence'
                $global:LASTEXITCODE = 7
            }
        } 6>&1 | ForEach-Object { $failureMessages.Add([string]$_) }
    }
    catch {
        $failureWasThrown = $true
    }
    $failureReport = $failureMessages -join [Environment]::NewLine
    Assert-Condition $failureWasThrown 'A failed verification step did not throw.'
    Assert-Condition ($failureReport -match 'FAIL Failure tail \| exit code 7') 'Failure report omitted the exit code.'
    Assert-Condition ($failureReport -match 'critical tail evidence') 'Failure report omitted tail evidence.'
    $failureLog = Get-SingleVerificationLog $failureContext
    Assert-Condition ((Get-Content -Raw -LiteralPath $failureLog.FullName) -match 'critical tail evidence') 'Failure log was not retained.'

    # Native stderr with a successful exit remains diagnostic log content, not a
    # synthetic failure.
    $nativeWarningContext = New-VerificationLogContext -RepositoryRoot $repositoryRoot -LogRoot $testRoot
    $nativeWarningMessages = [System.Collections.Generic.List[string]]::new()
    & {
        Invoke-LoggedVerificationStep -Context $nativeWarningContext -Name 'Native warning' -Action {
            & cmd.exe /c 'echo native warning evidence 1>&2 & exit /b 0'
        }
    } 6>&1 | ForEach-Object { $nativeWarningMessages.Add([string]$_) }
    $nativeWarningReport = $nativeWarningMessages -join [Environment]::NewLine
    Assert-Condition ($nativeWarningReport -match 'PASS Native warning | elapsed ') 'Successful native stderr was incorrectly reported as a failure.'
    $nativeWarningLog = Get-SingleVerificationLog $nativeWarningContext
    Assert-Condition ((Get-Content -Raw -LiteralPath $nativeWarningLog.FullName) -match 'native warning evidence') 'Successful native stderr was not retained in the log.'

    # Native stderr must not turn a real process exit code into PowerShell's -1.
    $nativeFailureContext = New-VerificationLogContext -RepositoryRoot $repositoryRoot -LogRoot $testRoot
    $nativeFailureMessages = [System.Collections.Generic.List[string]]::new()
    try {
        & {
            Invoke-LoggedVerificationStep -Context $nativeFailureContext -Name 'Native failure' -Action {
                & cmd.exe /c 'echo native failure evidence 1>&2 & exit /b 7'
            }
        } 6>&1 | ForEach-Object { $nativeFailureMessages.Add([string]$_) }
    }
    catch {
    }
    $nativeFailureReport = $nativeFailureMessages -join [Environment]::NewLine
    Assert-Condition ($nativeFailureReport -match 'FAIL Native failure \| exit code 7') 'Native failure did not preserve its process exit code.'
    Assert-Condition ($nativeFailureReport -notmatch 'exit code -1') 'Native stderr incorrectly replaced the process exit code with -1.'
    Assert-Condition ($nativeFailureReport -match 'native failure evidence') 'Native stderr tail evidence is missing.'

    # Run and step identifiers are unique, including when contexts are created
    # back-to-back by concurrent verification processes.
    $secondSuccessContext = New-VerificationLogContext -RepositoryRoot $repositoryRoot -LogRoot $testRoot
    Invoke-LoggedVerificationStep -Context $secondSuccessContext -Name 'Duplicate name' -Action { $global:LASTEXITCODE = 0 }
    Invoke-LoggedVerificationStep -Context $secondSuccessContext -Name 'Duplicate name' -Action { $global:LASTEXITCODE = 0 }
    $duplicateLogs = @(Get-ChildItem -LiteralPath $secondSuccessContext.LogDirectory -Filter '*.log' -File)
    Assert-Condition ($successContext.LogDirectory -ne $secondSuccessContext.LogDirectory) 'Verification run directories collided.'
    Assert-Condition (($duplicateLogs.Name | Select-Object -Unique).Count -eq 2) 'Step log names were not unique.'

    # PowerShell exceptions are rendered as failures and persisted in the same
    # complete log rather than being swallowed.
    $exceptionContext = New-VerificationLogContext -RepositoryRoot $repositoryRoot -LogRoot $testRoot
    $exceptionMessages = [System.Collections.Generic.List[string]]::new()
    try {
        & {
            Invoke-LoggedVerificationStep -Context $exceptionContext -Name 'PowerShell exception' -Action {
                throw 'simulated PowerShell exception'
            }
        } 6>&1 | ForEach-Object { $exceptionMessages.Add([string]$_) }
    }
    catch {
    }
    $exceptionReport = $exceptionMessages -join [Environment]::NewLine
    Assert-Condition ($exceptionReport -match 'FAIL PowerShell exception \| exit code 1') 'PowerShell exception was not reported as a failure.'
    $exceptionLog = Get-SingleVerificationLog $exceptionContext
    $exceptionLogText = Get-Content -Raw -LiteralPath $exceptionLog.FullName
    Assert-Condition ($exceptionLogText -match '\[PowerShell exception\]') 'PowerShell exception marker is missing from the log.'
    Assert-Condition ($exceptionLogText -match 'simulated PowerShell exception') 'PowerShell exception details are missing from the log.'

    # Representative dotnet/Vitest summaries are extracted while unknown output
    # safely falls back to a bounded, non-leaking status.
    $summaryLogPath = Join-Path $testRoot 'summary-formats.log'
    $localizedDotNetSummary = [string]::Concat(
        [char]0x5DF2, [char]0x901A, [char]0x8FC7, '!',
        ' - ', [char]0x5931, [char]0x8D25, ': 0, ',
        [char]0x901A, [char]0x8FC7, ': 4, ',
        [char]0x603B, [char]0x8BA1, ': 4')
    Set-Content -LiteralPath $summaryLogPath -Value @(
        'Passed! - Failed: 0, Passed: 4, Skipped: 1, Total: 5, Duration: 12 ms'
        $localizedDotNetSummary
        ' Test Files  2 passed (2)'
        '      Tests  10 passed (10)'
        '      ordinary test case name'
    )
    $summary = Get-VerificationTestSummary -LogPath $summaryLogPath
    Assert-Condition ($summary -match 'Passed! - Failed: 0, Passed: 4') 'Dotnet summary extraction failed.'
    Assert-Condition ($summary -match $localizedDotNetSummary) 'Localized dotnet summary extraction failed.'
    Assert-Condition ($summary -match 'Test Files 2 passed \(2\); Tests 10 passed \(10\)') 'Vitest summary extraction failed.'
    Assert-Condition ($summary -notmatch 'ordinary test case name') 'Summary extraction leaked ordinary output.'

    $fallbackLogPath = Join-Path $testRoot 'summary-fallback.log'
    Set-Content -LiteralPath $fallbackLogPath -Value 'unrecognized command output'
    Assert-Condition ((Get-VerificationTestSummary -LogPath $fallbackLogPath) -eq 'summary unavailable') 'Summary fallback is not safe.'

    Write-Host 'Test summary: verify-output script tests passed'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction Stop
    }
}
