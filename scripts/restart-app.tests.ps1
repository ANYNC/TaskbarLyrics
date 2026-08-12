<#
.SYNOPSIS
Script-level regression checks for restart-app.ps1.

The test dot-sources restart-app.ps1 so it can exercise pure argument and
process-selection helpers without stopping or starting a live application.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

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

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,

        [Parameter(Mandatory)]
        [string]$Message
    )

    $didThrow = $false
    try {
        & $Action
    }
    catch {
        $didThrow = $true
    }

    Assert-Condition $didThrow $Message
}

$restartScriptPath = Join-Path $PSScriptRoot 'restart-app.ps1'
$restartScriptSource = Get-Content -Raw -LiteralPath $restartScriptPath

# Dot-sourcing must define helpers only; the restart script's guard prevents
# this test from touching any real TaskbarLyrics.App process.
. $restartScriptPath

Assert-Condition ($restartScriptSource -match '\[switch\]\$StopOnly') 'The -StopOnly switch is missing.'
Assert-Condition ($restartScriptSource -match '\[switch\]\$NoWait') 'The -NoWait switch is missing.'
Assert-Condition ($restartScriptSource -match 'if \(\$MyInvocation\.InvocationName -eq ''\.''\)') 'The dot-source safety guard is missing.'
Assert-Condition ($restartScriptSource -match 'Stop-Process\s+-InputObject \$process\s+-Force') 'Shutdown does not force-stop explicit process objects.'
Assert-Condition ($restartScriptSource -match '\$process\.CloseMainWindow\(\)') 'Shutdown does not attempt a graceful window close.'
Assert-Condition ($restartScriptSource -match 'Timed out after') 'Startup timeout failure is not explicit.'
Assert-Condition ($restartScriptSource -match "Start-Process") 'NoWait does not launch dotnet in the background.'
Assert-Condition ($restartScriptSource -match "-FilePath 'dotnet'") 'NoWait launch executable changed.'
Assert-Condition ($restartScriptSource -match "-ArgumentList @\('run', '--project', 'TaskbarLyrics\.App'\)") 'NoWait launch arguments changed.'
Assert-Condition ($restartScriptSource -notmatch 'Get-Process\s+[*?]') 'Process discovery uses a wildcard instead of an exact name.'

Assert-Condition ((Resolve-RestartMode) -eq 'Foreground') 'The no-argument mode changed.'
Assert-Condition ((Resolve-RestartMode -StopOnly) -eq 'StopOnly') 'The -StopOnly mode is not resolved.'
Assert-Condition ((Resolve-RestartMode -NoWait) -eq 'NoWait') 'The -NoWait mode is not resolved.'
Assert-Throws -Action { Resolve-RestartMode -StopOnly -NoWait } -Message 'Conflicting switches were accepted.'

$savedStartupTimeoutSeconds = $script:StartupTimeoutSeconds
try {
    $script:StartupTimeoutSeconds = 0
    $script:fakeLaunchCalled = $false
    $timeoutMessage = & {
        function Start-Process {
            $script:fakeLaunchCalled = $true
            return [pscustomobject]@{
                HasExited = $false
                ExitCode = 0
            }
        }

        try {
            Start-TaskbarLyricsAndWait -RepositoryRoot $PSScriptRoot
            throw 'Expected startup timeout was not raised.'
        }
        catch {
            return $_.Exception.Message
        }
    }
    Assert-Condition $script:fakeLaunchCalled 'Startup timeout test did not use its side-effect-free launcher.'
    Assert-Condition ($timeoutMessage -match 'Timed out after 0 seconds') 'Startup timeout did not produce a clear failure.'
}
finally {
    $script:StartupTimeoutSeconds = $savedStartupTimeoutSeconds
    Remove-Variable -Name fakeLaunchCalled -Scope Script -ErrorAction SilentlyContinue
}

$exactProcess = [pscustomobject]@{ ProcessName = 'TaskbarLyrics.App'; Id = 101 }
$secondExactProcess = [pscustomobject]@{ ProcessName = 'TaskbarLyrics.App'; Id = 102 }
$otherProcess = [pscustomobject]@{ ProcessName = 'TaskbarLyrics.Helper'; Id = 103 }
$selectedProcesses = @(Select-TaskbarLyricsProcesses -Processes @($exactProcess, $otherProcess, $secondExactProcess))
Assert-Condition ($selectedProcesses.Count -eq 2) 'Process selection did not keep only exact TaskbarLyrics.App names.'
Assert-Condition (($selectedProcesses | ForEach-Object Id) -join ',' -eq '101,102') 'Process selection changed the explicit process order.'
Assert-Condition (@(Select-TaskbarLyricsProcesses -Processes $null).Count -eq 0) 'Null process input was not handled safely.'

Write-Host 'Test summary: restart-app script tests passed'
