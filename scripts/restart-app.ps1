<#
.SYNOPSIS
Restarts the local TaskbarLyrics application.

.DESCRIPTION
The default mode stops any existing TaskbarLyrics.App processes and runs the
application in the foreground. Use -StopOnly to stop the application without
starting it, or -NoWait to start it in the background and return after the
application process is observed.
#>
[CmdletBinding()]
param(
    [switch]$StopOnly,
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'

$script:TaskbarLyricsProcessName = 'TaskbarLyrics.App'
$script:ShutdownGracePeriodMilliseconds = 800
$script:StartupPollMilliseconds = 250
$script:StartupTimeoutSeconds = 30

function Resolve-RestartMode {
    [CmdletBinding()]
    param(
        [switch]$StopOnly,
        [switch]$NoWait
    )

    if ($StopOnly -and $NoWait) {
        throw 'The -StopOnly and -NoWait switches cannot be used together.'
    }

    if ($StopOnly) {
        return 'StopOnly'
    }

    if ($NoWait) {
        return 'NoWait'
    }

    return 'Foreground'
}

function Select-TaskbarLyricsProcesses {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [object[]]$Processes
    )

    return @(
        foreach ($process in $Processes) {
            if ($null -ne $process -and $process.ProcessName -eq $script:TaskbarLyricsProcessName) {
                $process
            }
        }
    )
}

function Get-TaskbarLyricsProcesses {
    [CmdletBinding()]
    param()

    $candidates = @(Get-Process -Name $script:TaskbarLyricsProcessName -ErrorAction SilentlyContinue)
    return @(Select-TaskbarLyricsProcesses -Processes $candidates)
}

function Test-ProcessStillRunning {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process]$Process
    )

    try {
        return (-not [bool]$Process.HasExited)
    }
    catch [System.InvalidOperationException] {
        return $false
    }
}

function Stop-TaskbarLyricsProcesses {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [System.Diagnostics.Process[]]$Processes
    )

    $selectedProcesses = @(Select-TaskbarLyricsProcesses -Processes $Processes)
    foreach ($process in $selectedProcesses) {
        if (-not (Test-ProcessStillRunning -Process $process)) {
            continue
        }

        try {
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                $null = $process.CloseMainWindow()
            }
        }
        catch [System.InvalidOperationException] {
            continue
        }
        catch [System.ComponentModel.Win32Exception] {
            Write-Verbose 'A graceful close request could not be sent; force termination will be attempted.'
        }
    }

    if ($selectedProcesses.Count -eq 0) {
        return
    }

    Start-Sleep -Milliseconds $script:ShutdownGracePeriodMilliseconds
    foreach ($process in $selectedProcesses) {
        if (-not (Test-ProcessStillRunning -Process $process)) {
            continue
        }

        try {
            Stop-Process -InputObject $process -Force -ErrorAction Stop
        }
        catch [System.InvalidOperationException] {
            Write-Verbose 'A selected TaskbarLyrics.App process exited during shutdown.'
        }
        catch [System.ArgumentException] {
            Write-Verbose 'A selected TaskbarLyrics.App process exited before force termination.'
        }
    }
}

function Start-TaskbarLyricsAndWait {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $launcher = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', 'TaskbarLyrics.App') `
        -WorkingDirectory $RepositoryRoot `
        -WindowStyle Hidden `
        -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds($script:StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (@(Get-TaskbarLyricsProcesses).Count -gt 0) {
            return
        }

        if ($launcher.HasExited) {
            throw "dotnet run exited with code $($launcher.ExitCode) before $script:TaskbarLyricsProcessName was detected."
        }

        Start-Sleep -Milliseconds $script:StartupPollMilliseconds
    }

    throw "Timed out after $script:StartupTimeoutSeconds seconds waiting for $script:TaskbarLyricsProcessName to start."
}

function Invoke-Restart {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [switch]$StopOnly,
        [switch]$NoWait
    )

    $mode = Resolve-RestartMode -StopOnly:$StopOnly -NoWait:$NoWait
    $existingProcesses = @(Get-TaskbarLyricsProcesses)
    Stop-TaskbarLyricsProcesses -Processes $existingProcesses

    if ($mode -eq 'StopOnly') {
        return
    }

    if ($mode -eq 'NoWait') {
        Start-TaskbarLyricsAndWait -RepositoryRoot $RepositoryRoot
        return
    }

    & dotnet run --project TaskbarLyrics.App
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot

# Dot-sourcing exposes pure helpers to the script-level regression test without
# stopping or starting a live application process.
if ($MyInvocation.InvocationName -eq '.') {
    return
}

Set-Location $repositoryRoot
Invoke-Restart -RepositoryRoot $repositoryRoot -StopOnly:$StopOnly -NoWait:$NoWait
