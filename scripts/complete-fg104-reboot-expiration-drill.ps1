[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard.Development'
$drillDirectory = 'C:\ProgramData\FocusGuard\Development\RebootExpirationDrill'
$resultPath = Join-Path $drillDirectory 'result.json'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}
if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
    throw "Drill result not found: $resultPath"
}

$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$resultIsAccepted = $result.schemaVersion -eq 1
$resultIsAccepted = $resultIsAccepted -and $result.outcome -eq 'Released'
$resultIsAccepted = $resultIsAccepted -and $result.reason -eq 'Expired'
$resultIsAccepted = $resultIsAccepted -and $result.restartObserved -eq $true
if (-not $resultIsAccepted) {
    throw "Drill result did not meet acceptance criteria: $($result | ConvertTo-Json -Compress)"
}

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($null -eq $service -or $service.State -ne 'Running') {
    throw 'Development service did not recover to Running after reboot.'
}
if ($service.StartMode -ne 'Auto') {
    throw "Expected automatic start during verification, found: $($service.StartMode)"
}

& sc.exe config $serviceName start= demand
if ($LASTEXITCODE -ne 0) {
    throw "Failed to restore manual start: $LASTEXITCODE"
}

$evidence = [pscustomobject]@{
    ServiceName = $service.Name
    ServiceState = $service.State
    ProcessId = $service.ProcessId
    Outcome = $result.outcome
    Reason = $result.reason
    ReleasedUtc = $result.releasedUtc
    RestartObserved = $result.restartObserved
    BootIdBeforeRestart = $result.bootIdBeforeRestart
    BootIdAfterRestart = $result.bootIdAfterRestart
}

Remove-Item -LiteralPath $drillDirectory -Recurse -Force
if (Test-Path -LiteralPath $drillDirectory) {
    throw 'Drill directory remains after rollback.'
}
$manual = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($manual.StartMode -ne 'Manual') {
    throw "Manual start rollback failed: $($manual.StartMode)"
}

$evidence
