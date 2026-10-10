[CmdletBinding()]
param(
    [Parameter(Mandatory)][Guid]$RunId,
    [Parameter(Mandatory)][switch]$OwnerPresent
)

$ErrorActionPreference = 'Stop'
if (-not $OwnerPresent) { throw 'Completing the normal-session reboot drill requires -OwnerPresent.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The reboot result requires an already elevated PowerShell process; this script never auto-elevates.'
}

$installRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramFiles} 'FocusGuard'))
$drillClient = Join-Path $installRoot 'FocusGuard.DrillClient.exe'
$journalPath = Join-Path ${env:ProgramData} 'FocusGuard\normal-session-journal.json'
$stateDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\normal-session-reboot'))
$statePath = Join-Path $stateDirectory "state-$($RunId.ToString('N')).json"
$resultPath = Join-Path $stateDirectory "result-$($RunId.ToString('N')).json"
if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) { throw 'Normal-session reboot state for this RunId is missing.' }
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.schemaVersion -ne 1 -or $state.runId -ne $RunId) { throw 'Unsupported normal-session reboot state.' }

$service = Get-CimInstance Win32_Service -Filter "Name='FocusGuard'"
if ($null -eq $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto' -or
    $service.StartName -ne 'NT AUTHORITY\LocalService') {
    throw 'The service did not come back running automatically as LocalService.'
}
$bootAfter = ([DateTimeOffset](Get-CimInstance Win32_OperatingSystem).LastBootUpTime).ToUniversalTime()
if ($bootAfter -le [DateTimeOffset]$state.bootUtcBeforeRestart) { throw 'Windows restart was not observed.' }

$status = (& $drillClient status | Out-String) | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Installed Diagnostic status failed.' }
if ($status.sessionId -eq $state.sessionId -or $status.sessionState -in @('Active', 'Activating')) {
    throw 'The normal session was restored after reboot; normal sessions must not be restored.'
}
$activity = ((& $drillClient activity | Out-String) | ConvertFrom-Json -DateKind String).events
$interruption = @($activity | Where-Object { $_.kind -eq 4 -and $_.sessionId -eq $state.sessionId })
if ($interruption.Count -ne 1) {
    throw "Expected exactly one SessionInterrupted audit event for the session; found $($interruption.Count)."
}
if ($interruption[0].outcome -notin @('Stopped', 'ServiceTerminated')) {
    throw "Unexpected interruption outcome: $($interruption[0].outcome)"
}
if (Test-Path -LiteralPath $journalPath) { throw 'The normal-session journal was left behind after recovery.' }

$result = [ordered]@{
    schemaVersion = 1
    runId = $RunId
    sessionId = $state.sessionId
    bootUtcBeforeRestart = $state.bootUtcBeforeRestart
    bootUtcAfterRestart = $bootAfter.ToString('O')
    sessionStateAfterReboot = $status.sessionState
    interruptionOutcome = $interruption[0].outcome
    interruptionAuditUtc = $interruption[0].timestampUtc
    journalPresent = $false
    verifiedUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
[IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
[pscustomobject]$result
