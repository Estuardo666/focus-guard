[CmdletBinding()]
param(
    [ValidateRange(2, 10)]
    [int]$DurationMinutes = 4
)

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard.Development'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\FocusGuard.Service\win-x64'))
$destination = 'C:\ProgramData\FocusGuard\Development\Service'
$drillDirectory = 'C:\ProgramData\FocusGuard\Development\RebootExpirationDrill'
$expectedBinary = Join-Path $destination 'FocusGuard.Service.exe'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($null -eq $service) {
    throw 'Expected development service is not installed.'
}
if ($service.StartName -ne 'NT AUTHORITY\LocalService') {
    throw "Unexpected service account: $($service.StartName)"
}
if ([IO.Path]::GetFullPath($service.PathName.Trim('"')) -ne $expectedBinary) {
    throw "Unexpected service path: $($service.PathName)"
}

Stop-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus(
    [ServiceProcess.ServiceControllerStatus]::Stopped,
    [TimeSpan]::FromSeconds(15))

Get-ChildItem -LiteralPath $source -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
}
$sourceHash = (Get-FileHash -LiteralPath (
    Join-Path $source 'FocusGuard.Service.dll') -Algorithm SHA256).Hash
$stagedHash = (Get-FileHash -LiteralPath (
    Join-Path $destination 'FocusGuard.Service.dll') -Algorithm SHA256).Hash
if ($sourceHash -ne $stagedHash) {
    throw 'Staged service hash does not match the reviewed publish.'
}

if (Test-Path -LiteralPath $drillDirectory) {
    Remove-Item -LiteralPath $drillDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $drillDirectory | Out-Null

$armedUtc = [DateTimeOffset]::UtcNow
$expiresUtc = $armedUtc.AddMinutes($DurationMinutes)
$bootUtc = [DateTimeOffset]::UtcNow.Subtract(
    [TimeSpan]::FromMilliseconds([Environment]::TickCount64))
$bootTicks = $bootUtc.UtcTicks - ($bootUtc.UtcTicks % [TimeSpan]::TicksPerMinute)
$bootId = ([DateTimeOffset]::new(
    $bootTicks,
    [TimeSpan]::Zero)).ToString('O')
$drillId = [Guid]::NewGuid()
$state = [ordered]@{
    schemaVersion = 1
    drillId = $drillId
    armedUtc = $armedUtc.ToString('O')
    expiresUtc = $expiresUtc.ToString('O')
    bootIdBeforeRestart = $bootId
}
[IO.File]::WriteAllText(
    (Join-Path $drillDirectory 'state.json'),
    ($state | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))

& sc.exe config $serviceName start= auto
if ($LASTEXITCODE -ne 0) {
    throw "Failed to set automatic start: $LASTEXITCODE"
}
Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus(
    [ServiceProcess.ServiceControllerStatus]::Running,
    [TimeSpan]::FromSeconds(15))

$resultPath = Join-Path $drillDirectory 'result.json'
if (Test-Path -LiteralPath $resultPath) {
    throw 'Drill released before reboot.'
}

[pscustomobject]@{
    ServiceName = $serviceName
    StartMode = (Get-CimInstance Win32_Service -Filter "Name='$serviceName'").StartMode
    SourceHash = $sourceHash
    DrillId = $drillId
    ArmedUtc = $armedUtc
    ExpiresUtc = $expiresUtc
    BootIdBeforeRestart = $bootId
    ResultPresent = $false
}
