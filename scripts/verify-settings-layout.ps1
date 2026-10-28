<#
.SYNOPSIS
Checks settings reachability with real WebView2 layout at 100%, 140%, and 225% text scaling.
.DESCRIPTION
Windows desktop check; uses an isolated invisible window and a new profile under tmp/.
Does not open the user app, change Windows scaling, or route settings actions to a host.
#>
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $repositoryRoot 'tests/SettingsLayoutProbe/SettingsLayoutProbe.csproj') -- $repositoryRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
