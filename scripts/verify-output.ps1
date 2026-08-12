<#
.SYNOPSIS
Shared output capture and reporting helpers for scripts/verify.ps1.

The helpers intentionally keep logs under tmp/ so verification diagnostics never
become release input or source-controlled state.
#>

function New-VerificationLogContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$LogRoot
    )

    $resolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path.TrimEnd('\', '/')
    New-Item -ItemType Directory -Path $LogRoot -Force -ErrorAction Stop | Out-Null

    # The GUID prevents collisions when multiple verify.ps1 processes start in the
    # same millisecond or reuse a process id across separate shells.
    $runName = '{0}-{1}-{2}' -f (
        Get-Date -Format 'yyyyMMdd-HHmmssfff'),
        $PID,
        ([guid]::NewGuid().ToString('N'))
    $runDirectory = Join-Path $LogRoot $runName
    New-Item -ItemType Directory -Path $runDirectory -ErrorAction Stop | Out-Null

    [pscustomobject]@{
        RepositoryRoot = $resolvedRepositoryRoot
        LogDirectory = (Resolve-Path -LiteralPath $runDirectory -ErrorAction Stop).Path
        StepNumber = 0
    }
}

function ConvertTo-VerificationRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $rootWithSeparator = $RepositoryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($resolvedPath.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        return $resolvedPath.Substring($rootWithSeparator.Length).Replace('\', '/')
    }

    return $resolvedPath.Replace('\', '/')
}

function ConvertTo-VerificationSafeFileName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $safeName = $Name -replace '[^A-Za-z0-9._-]+', '-'
    $safeName = $safeName.Trim('-')
    if ([string]::IsNullOrWhiteSpace($safeName)) {
        return 'verification-step'
    }

    if ($safeName.Length -gt 96) {
        $safeName = $safeName.Substring(0, 96)
    }

    return $safeName.ToLowerInvariant()
}

function Remove-VerificationAnsiSequences {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [string]$Text
    )

    if ($null -eq $Text) {
        return ''
    }

    return $Text -replace '\x1B\[[0-?]*[ -/]*[@-~]', ''
}

function ConvertTo-VerificationCompactLine {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [string]$Text,

        [int]$MaximumLength = 260
    )

    $compact = Remove-VerificationAnsiSequences $Text
    $compact = $compact -replace '\s+', ' '
    $compact = $compact.Trim()
    if ($compact.Length -gt $MaximumLength) {
        if ($MaximumLength -le 3) {
            return $compact.Substring(0, [Math]::Max(0, $MaximumLength))
        }

        return $compact.Substring(0, $MaximumLength - 3) + '...'
    }

    return $compact
}

function Get-VerificationTestSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$LogPath
    )

    $summaryLines = [System.Collections.Generic.List[string]]::new()
    try {
        $lines = @(Get-Content -LiteralPath $LogPath -ErrorAction Stop)
    }
    catch {
        return 'summary unavailable'
    }

    foreach ($line in $lines) {
        $compactLine = ConvertTo-VerificationCompactLine $line
        if ([string]::IsNullOrWhiteSpace($compactLine)) {
            continue
        }

        # dotnet test emits "Passed! - Failed: ..." or "Failed! - Failed: ...".
        # Vitest emits compact "Test Files ..." and "Tests ..." aggregate rows.
        $isDotNetSummary = $compactLine -match '^(Passed!|Failed!)(\s|$)'
        $isLocalizedDotNetSummary = $compactLine -match '^(\u5DF2\u901A\u8FC7!|\u5931\u8D25!).*(\u5931\u8D25:).*(\u603B\u8BA1:)'
        $isVitestSummary = $compactLine -match '^(Test Files|Tests)\s+\d+\s+'
        $isGenericSummary = $compactLine -match '^(Test summary|Total tests?):\s*'
        if (-not ($isDotNetSummary -or $isLocalizedDotNetSummary -or $isVitestSummary -or $isGenericSummary)) {
            continue
        }

        if (-not $summaryLines.Contains($compactLine)) {
            $summaryLines.Add($compactLine)
        }
    }

    if ($summaryLines.Count -eq 0) {
        return 'summary unavailable'
    }

    return ($summaryLines -join '; ')
}

function Get-VerificationTailEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$LogPath,

        [int]$MaximumLines = 20,

        [int]$MaximumCharacters = 4000
    )

    try {
        $lines = @(Get-Content -LiteralPath $LogPath -Tail $MaximumLines -ErrorAction Stop)
    }
    catch {
        return @('Unable to read verification log tail.')
    }

    $evidence = [System.Collections.Generic.List[string]]::new()
    $characterCount = 0
    foreach ($line in $lines) {
        $compactLine = ConvertTo-VerificationCompactLine $line 400
        if ([string]::IsNullOrWhiteSpace($compactLine)) {
            continue
        }

        $remainingCharacters = $MaximumCharacters - $characterCount
        if ($remainingCharacters -le 0) {
            break
        }

        if ($compactLine.Length -gt $remainingCharacters) {
            if ($remainingCharacters -le 3) {
                $compactLine = $compactLine.Substring(0, [Math]::Max(0, $remainingCharacters))
            }
            else {
                $compactLine = $compactLine.Substring(0, $remainingCharacters - 3) + '...'
            }
        }

        $evidence.Add($compactLine)
        $characterCount += $compactLine.Length
    }

    if ($evidence.Count -eq 0) {
        return @('(log contains no non-empty output)')
    }

    return $evidence.ToArray()
}

function Add-VerificationExceptionToLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$LogPath,

        [Parameter(Mandatory)]
        [System.Management.Automation.ErrorRecord]$ErrorRecord
    )

    $exceptionText = ($ErrorRecord | Out-String).Trim()
    Add-Content -LiteralPath $LogPath -Value @(
        ''
        '[PowerShell exception]'
        $exceptionText
    ) -ErrorAction Stop
}

function Invoke-LoggedVerificationStep {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Context,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    $Context.StepNumber++
    $stepNumber = $Context.StepNumber
    $safeName = ConvertTo-VerificationSafeFileName $Name
    # The run directory is already unique; include both sequence and GUID in the
    # file name so independently invoked helper calls cannot overwrite a log.
    $fileName = '{0:D3}-{1}-{2}.log' -f $stepNumber, $safeName, ([guid]::NewGuid().ToString('N'))
    $logPath = Join-Path $Context.LogDirectory $fileName
    New-Item -ItemType File -Path $logPath -Force -ErrorAction Stop | Out-Null

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $exitCode = 0
    $actionFailed = $false
    $priorErrorRecords = @($Error)
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # Reset the automatic process exit code before every action; otherwise a
        # prior failed external command could contaminate a PowerShell-only step.
        $global:LASTEXITCODE = 0
        # PowerShell 7 promotes native stderr to a terminating NativeCommandError
        # when ErrorActionPreference is Stop, replacing the process's real exit
        # code with -1. Keep native stderr in the captured log and inspect the
        # actual LASTEXITCODE; explicit PowerShell throws still enter this catch.
        $ErrorActionPreference = 'Continue'
        & $Action *> $logPath
        $exitCode = [int]$global:LASTEXITCODE
        # Native stderr is represented as a NativeCommandError record even when
        # the process succeeds. It belongs in the complete log, but it must not
        # override the process's real exit code or turn a successful step into a
        # failure. Other newly emitted PowerShell errors remain failures.
        $actionErrorRecords = @(
            $Error |
                Where-Object {
                    $_ -notin $priorErrorRecords -and
                    $_.FullyQualifiedErrorId -notmatch 'NativeCommandError'
                }
        )
        if ($exitCode -ne 0 -or $actionErrorRecords.Count -gt 0) {
            $actionFailed = $true
            if ($exitCode -eq 0 -and $actionErrorRecords.Count -gt 0) {
                $exitCode = 1
            }
        }
    }
    catch {
        $actionFailed = $true
        $exitCode = if ([int]$global:LASTEXITCODE -ne 0) {
            [int]$global:LASTEXITCODE
        }
        else {
            1
        }

        Add-VerificationExceptionToLog -LogPath $logPath -ErrorRecord $_
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        $stopwatch.Stop()
    }

    $elapsed = '{0:0.0}s' -f $stopwatch.Elapsed.TotalSeconds
    $relativeLogPath = ConvertTo-VerificationRelativePath -RepositoryRoot $Context.RepositoryRoot -Path $logPath
    if ($actionFailed) {
        Write-Host ("FAIL {0} | exit code {1} | elapsed {2} | log: {3}" -f $Name, $exitCode, $elapsed, $relativeLogPath)
        Write-Host '  key tail evidence:'
        foreach ($evidenceLine in @(Get-VerificationTailEvidence -LogPath $logPath)) {
            Write-Host ("  {0}" -f $evidenceLine)
        }

        throw [System.Management.Automation.RuntimeException]::new(
            ("{0} failed with exit code {1}. Full output is in {2}." -f $Name, $exitCode, $relativeLogPath)
        )
    }

    $summary = Get-VerificationTestSummary -LogPath $logPath
    Write-Host ("PASS {0} | elapsed {1} | summary: {2} | log: {3}" -f $Name, $elapsed, $summary, $relativeLogPath)
}

function Write-VerificationUnhandledFailure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Context,

        [Parameter(Mandatory)]
        [System.Management.Automation.ErrorRecord]$ErrorRecord
    )

    $Context.StepNumber++
    $fileName = '{0:D3}-verification-exception-{1}.log' -f $Context.StepNumber, ([guid]::NewGuid().ToString('N'))
    $logPath = Join-Path $Context.LogDirectory $fileName
    New-Item -ItemType File -Path $logPath -Force -ErrorAction Stop | Out-Null
    Add-VerificationExceptionToLog -LogPath $logPath -ErrorRecord $ErrorRecord

    $relativeLogPath = ConvertTo-VerificationRelativePath -RepositoryRoot $Context.RepositoryRoot -Path $logPath
    Write-Host ("FAIL Verification setup | exit code 1 | log: {0}" -f $relativeLogPath)
    Write-Host '  key tail evidence:'
    foreach ($evidenceLine in @(Get-VerificationTailEvidence -LogPath $logPath)) {
        Write-Host ("  {0}" -f $evidenceLine)
    }
}
