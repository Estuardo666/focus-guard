[CmdletBinding()]
param(
    [switch]$PreflightOnly,
    [switch]$Arm,
    [switch]$OwnerPresent
)

$ErrorActionPreference = 'Stop'
if ($PreflightOnly.IsPresent -eq $Arm.IsPresent) {
    throw 'Specify exactly one of -PreflightOnly or -Arm.'
}
if ($Arm -and -not $OwnerPresent) {
    throw 'Arming a production reboot drill requires -OwnerPresent.'
}

$installRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramFiles} 'FocusGuard'))
$drillClient = Join-Path $installRoot 'FocusGuard.DrillClient.exe'
$stateDirectory = Join-Path $PSScriptRoot '..\artifacts\fg112-reboot'
$service = Get-CimInstance Win32_Service -Filter "Name='FocusGuard'"
$status = $null
$statusExitCode = $null
$reason = $null

if ($null -eq $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto' -or
    $service.StartName -ne 'NT AUTHORITY\LocalService' -or
    [IO.Path]::GetFullPath($service.PathName.Trim('"')) -ne (Join-Path $installRoot 'FocusGuard.Service.exe')) {
    $reason = 'FocusGuard must be installed, running automatically, and owned by LocalService.'
} elseif (-not (Test-Path -LiteralPath $drillClient -PathType Leaf)) {
    $reason = 'The installed Diagnostic status client is missing.'
} else {
    $statusOutput = (& $drillClient status | Out-String)
    $statusExitCode = $LASTEXITCODE
    if ($statusExitCode -ne 0) {
        $reason = "Installed Diagnostic status failed with exit code $statusExitCode."
    } else {
        $status = $statusOutput | ConvertFrom-Json
        if (-not $status.serviceReady -or $status.sessionState -ne 'Active' -or
            -not $status.strictMode -or -not $status.enforcementConnected -or
            [string]::IsNullOrWhiteSpace([string]$status.sessionId) -or
            $null -eq $status.endUtc -or
            [DateTimeOffset]::UtcNow -ge [DateTimeOffset]$status.endUtc) {
            $reason = 'Start a finite strict session through the reviewed FocusGuard UI and wait for both channels before arming the reboot drill.'
        }
    }
}

$preflight = [pscustomobject]@{
    Mode = if ($PreflightOnly) { 'PreflightOnly' } else { 'Arm' }
    Ready = [string]::IsNullOrWhiteSpace($reason)
    Reason = $reason
    ServiceState = $service.State
    ServicePid = $service.ProcessId
    ServiceAccount = $service.StartName
    SessionState = $status.sessionState
    StrictMode = $status.strictMode
    EnforcementConnected = $status.enforcementConnected
        SessionEndUtc = $status.endUtc
    DiagnosticActivationUsed = $false
    MachineStateChanged = $false
}
if ($PreflightOnly) { $preflight; return }
if (-not $preflight.Ready) { throw $reason }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Arming the production reboot drill requires an already elevated PowerShell process; this script never auto-elevates.'
}

$stateDirectory = [IO.Path]::GetFullPath($stateDirectory)
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$runId = [Guid]::NewGuid()
$statePath = Join-Path $stateDirectory "state-$($runId.ToString('N')).json"
$state = [ordered]@{
    schemaVersion = 2
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
    EndUtc = $state.endUtc
    BootUtcBeforeRestart = $state.bootUtcBeforeRestart
    StatePath = $statePath
    DiagnosticActivationUsed = $false
    NextCommand = 'Restart-Computer'
    CompletionCommand = ".\scripts\complete-fg112-production-reboot-drill.ps1 -RunId $runId"
}
