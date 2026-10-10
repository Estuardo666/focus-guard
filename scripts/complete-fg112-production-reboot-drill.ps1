[CmdletBinding()]
param(
    [Parameter(Mandatory)][Guid]$RunId,
    [Parameter(Mandatory)][switch]$OwnerPresent,
    [ValidateRange(1, 10)][int]$ChannelWaitMinutes = 3
)

$ErrorActionPreference = 'Stop'
if (-not $OwnerPresent) { throw 'Completing the production reboot drill requires -OwnerPresent.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The reboot result requires an already elevated PowerShell process; this script never auto-elevates.'
}

$installRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramFiles} 'FocusGuard'))
$drillClient = Join-Path $installRoot 'FocusGuard.DrillClient.exe'
$stateDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\fg112-reboot'))
$statePath = Join-Path $stateDirectory "state-$($RunId.ToString('N')).json"
$resultPath = Join-Path $stateDirectory "result-$($RunId.ToString('N')).json"
$activePolicyPath = Join-Path ${env:ProgramData} 'FocusGuard\StrictPersistence\active-policy.json'

if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    throw 'FG-112 reboot state for this RunId is missing.'
}
if (-not (Test-Path -LiteralPath $drillClient -PathType Leaf)) {
    throw 'The installed Diagnostic status client is missing.'
}
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.schemaVersion -ne 2 -or $state.runId -ne $RunId) { throw 'Unsupported FG-112 reboot state.' }

$service = Get-CimInstance Win32_Service -Filter "Name='FocusGuard'"
if ($null -eq $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto' -or
    $service.StartName -ne 'NT AUTHORITY\LocalService' -or
    [IO.Path]::GetFullPath($service.PathName.Trim('"')) -ne (Join-Path $installRoot 'FocusGuard.Service.exe')) {
    throw 'Production service did not recover running automatically as LocalService from the installed path.'
}
$bootAfter = ([DateTimeOffset](Get-CimInstance Win32_OperatingSystem).LastBootUpTime).ToUniversalTime()
$restartObserved = $bootAfter -gt [DateTimeOffset]$state.bootUtcBeforeRestart
if (-not $restartObserved) { throw 'Windows restart was not observed.' }

function Get-InstalledStatus {
    $output = (& $drillClient status | Out-String)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "Installed Diagnostic status failed with exit code $exitCode." }
    return ($output | ConvertFrom-Json)
}

$statusBeforeChannels = Get-InstalledStatus
if ($statusBeforeChannels.sessionId -ne $state.sessionId) {
    throw 'The persisted session was not recovered by the production service.'
}
$endUtc = [DateTimeOffset]$state.endUtc
$enforcementBeforeChannels = [bool]$statusBeforeChannels.enforcementConnected
$statusAfterChannels = $statusBeforeChannels

if ([DateTimeOffset]::UtcNow -lt $endUtc) {
    if (-not $statusBeforeChannels.strictMode -or
        -not (Test-Path -LiteralPath $activePolicyPath -PathType Leaf)) {
        throw 'The unexpired strict policy was not retained after reboot.'
    }

    # Wait for the real Desktop/ProcessAgent and browser NativeMessaging channels.
    # The Diagnostic client never injects a synthetic acknowledgement.
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes($ChannelWaitMinutes)
    while (-not $statusAfterChannels.enforcementConnected -and
        [DateTimeOffset]::UtcNow -lt $deadline -and
        [DateTimeOffset]::UtcNow -lt $endUtc) {
        Start-Sleep -Seconds 1
        $statusAfterChannels = Get-InstalledStatus
        if ($statusAfterChannels.sessionId -ne $state.sessionId) {
            throw 'The session identity changed while waiting for real channel acknowledgements.'
        }
        if ($statusAfterChannels.sessionState -eq 'Expired') { break }
    }

    if ($statusAfterChannels.sessionState -eq 'Expired') {
        if ($statusAfterChannels.strictMode -or (Test-Path -LiteralPath $activePolicyPath)) {
            throw 'Expired post-reboot strict policy was not released.'
        }
    } elseif (-not $statusAfterChannels.enforcementConnected) {
        throw 'Real post-reboot channel acknowledgements did not arrive before the bounded wait ended.'
    } elseif (-not $statusAfterChannels.strictMode -or -not $statusAfterChannels.serviceReady -or
        -not $statusAfterChannels.extensionReady) {
        throw 'Recovered strict state is inconsistent with service or extension readiness.'
    }
} else {
    if ($statusBeforeChannels.sessionState -ne 'Expired' -or $statusBeforeChannels.strictMode -or
        $statusBeforeChannels.enforcementConnected -or (Test-Path -LiteralPath $activePolicyPath)) {
        throw 'Expired post-reboot policy was not released.'
    }
}

$result = [ordered]@{
    schemaVersion = 2
    runId = $RunId
    sessionId = $state.sessionId
    bootUtcBeforeRestart = $state.bootUtcBeforeRestart
    bootUtcAfterRestart = $bootAfter.ToString('O')
    restartObserved = $restartObserved
    stateBeforeRealChannels = $statusBeforeChannels.sessionState
    enforcementBeforeRealChannels = $enforcementBeforeChannels
    stateAfterRealChannels = $statusAfterChannels.sessionState
    enforcementAfterRealChannels = $statusAfterChannels.enforcementConnected
    strictModeAfterRealChannels = $statusAfterChannels.strictMode
    verifiedUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
[IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
[pscustomobject]$result
