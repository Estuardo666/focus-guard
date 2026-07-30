[CmdletBinding()]
param(
    [ValidateRange(3, 30)]
    [int]$DurationMinutes = 10
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$drillClient = Join-Path $repositoryRoot 'src\FocusGuard.DrillClient\bin\Release\net10.0\FocusGuard.DrillClient.exe'
$stateDirectory = Join-Path $repositoryRoot 'artifacts\fg112-reboot'
$statePath = Join-Path $stateDirectory 'state.json'
$activePolicyPath = 'C:\ProgramData\FocusGuard\StrictPersistence\active-policy.json'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}
if (-not (Test-Path -LiteralPath $drillClient -PathType Leaf)) {
    throw 'Build FocusGuard.DrillClient Release before arming the drill.'
}
if (Test-Path -LiteralPath $activePolicyPath) {
    throw 'A production strict policy is already persisted.'
}

$service = Get-CimInstance Win32_Service -Filter "Name='FocusGuard'"
if ($null -eq $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto') {
    throw 'FocusGuard must be installed, running, and automatic.'
}
if ($service.StartName -ne 'NT AUTHORITY\LocalService') {
    throw "Unexpected service account: $($service.StartName)"
}
if ([IO.Path]::GetFullPath($service.PathName.Trim('"')) -ne 'C:\Program Files\FocusGuard\FocusGuard.Service.exe') {
    throw "Unexpected service path: $($service.PathName)"
}

$activation = ((& $drillClient activate-strict ($DurationMinutes * 60) | Out-String) | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not $activation.activation.accepted) {
    throw 'Production strict activation failed.'
}
$acknowledgement = $null
for ($attempt = 0; $attempt -lt 10; $attempt++) {
    $acknowledgement = ((& $drillClient acknowledge | Out-String) | ConvertFrom-Json)
    if ($LASTEXITCODE -eq 0 -and $acknowledgement.sessionState -eq 'Active') { break }
    Start-Sleep -Milliseconds 250
}
if ($null -eq $acknowledgement -or $acknowledgement.sessionState -ne 'Active') {
    throw 'Dual policy acknowledgement did not activate enforcement.'
}
if (-not (Test-Path -LiteralPath $activePolicyPath -PathType Leaf)) {
    throw 'Strict policy was not persisted before reboot.'
}

New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$state = [ordered]@{
    schemaVersion = 1
    sessionId = $activation.activation.sessionId
    startUtc = $activation.activation.startUtc
    endUtc = $activation.activation.endUtc
    bootUtcBeforeRestart = ([DateTimeOffset](Get-CimInstance Win32_OperatingSystem).LastBootUpTime).ToUniversalTime().ToString('O')
    armedUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
[IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    Armed = $true
    SessionId = $state.sessionId
    EndUtc = $state.endUtc
    BootUtcBeforeRestart = $state.bootUtcBeforeRestart
    StatePath = $statePath
    NextCommand = 'Restart-Computer'
}
