[CmdletBinding()]
param(
    [switch]$PreflightOnly,
    [switch]$Arm,
    [switch]$OwnerPresent
)

# Owner-present drill: a running normal session must not be restored after a Windows restart,
# and the interruption must be recorded (graceful shutdown: audit outcome Stopped; a hard
# power loss: ServiceTerminated from the normal-session journal). Never reboots by itself.
$ErrorActionPreference = 'Stop'
if ($PreflightOnly.IsPresent -eq $Arm.IsPresent) {
    throw 'Specify exactly one of -PreflightOnly or -Arm.'
}
if ($Arm -and -not $OwnerPresent) {
    throw 'Arming a normal-session reboot drill requires -OwnerPresent.'
}

$installRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramFiles} 'FocusGuard'))
$drillClient = Join-Path $installRoot 'FocusGuard.DrillClient.exe'
$journalPath = Join-Path ${env:ProgramData} 'FocusGuard\normal-session-journal.json'
$stateDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\normal-session-reboot'))
$service = Get-CimInstance Win32_Service -Filter "Name='FocusGuard'"
$status = $null
$reason = $null

if ($null -eq $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto' -or
    $service.StartName -ne 'NT AUTHORITY\LocalService') {
    $reason = 'FocusGuard must be installed, running automatically, and owned by LocalService.'
} elseif (-not (Test-Path -LiteralPath $drillClient -PathType Leaf)) {
    $reason = 'The installed Diagnostic status client is missing.'
} else {
    $statusOutput = (& $drillClient status | Out-String)
    if ($LASTEXITCODE -ne 0) {
        $reason = "Installed Diagnostic status failed with exit code $LASTEXITCODE."
    } else {
        $status = $statusOutput | ConvertFrom-Json
        if (-not $status.serviceReady -or $status.sessionState -ne 'Active' -or $status.strictMode -or
            [string]::IsNullOrWhiteSpace([string]$status.sessionId) -or $null -eq $status.endUtc -or
            ([DateTimeOffset]$status.endUtc - [DateTimeOffset]::UtcNow) -lt [TimeSpan]::FromMinutes(5)) {
            $reason = 'Start a normal (non-strict) session of at least 10 minutes through the FocusGuard UI before arming.'
        } elseif (-not (Test-Path -LiteralPath $journalPath -PathType Leaf)) {
            $reason = 'The normal-session journal has not been written yet; wait a few seconds and retry.'
        }
    }
}

$preflight = [pscustomobject]@{
    Mode = if ($PreflightOnly) { 'PreflightOnly' } else { 'Arm' }
    Ready = [string]::IsNullOrWhiteSpace($reason)
    Reason = $reason
    ServiceState = $service.State
    ServiceAccount = $service.StartName
    SessionState = $status.sessionState
    StrictMode = $status.strictMode
    SessionId = $status.sessionId
    SessionEndUtc = $status.endUtc
    JournalPresent = Test-Path -LiteralPath $journalPath -PathType Leaf
    MachineStateChanged = $false
}
if ($PreflightOnly) { $preflight; return }
if (-not $preflight.Ready) { throw $reason }

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Arming requires an already elevated PowerShell process; this script never auto-elevates.'
}

New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$runId = [Guid]::NewGuid()
$statePath = Join-Path $stateDirectory "state-$($runId.ToString('N')).json"
$state = [ordered]@{
    schemaVersion = 1
    runId = $runId
    sessionId = $status.sessionId
    startUtc = $status.startUtc
    endUtc = $status.endUtc
    bootUtcBeforeRestart = ([DateTimeOffset](Get-CimInstance Win32_OperatingSystem).LastBootUpTime).ToUniversalTime().ToString('O')
    armedUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
[IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    Armed = $true
    RunId = $runId
    SessionId = $state.sessionId
    StatePath = $statePath
    NextStep = 'Restart Windows normally (Start > Power > Restart) while the session is still running.'
    CompletionCommand = ".\scripts\complete-normal-session-reboot-drill.ps1 -RunId $runId -OwnerPresent"
}
