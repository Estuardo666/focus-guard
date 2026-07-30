[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard.Development'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$sourceDirectory = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\FocusGuard.Service\win-x64'))
$stagingDirectory = [IO.Path]::GetFullPath(
    (Join-Path $env:ProgramData 'FocusGuard\Development\Service'))
$expectedBinary = [IO.Path]::GetFullPath(
    (Join-Path $stagingDirectory 'FocusGuard.Service.exe'))
$backupDirectory = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\FocusGuard.Service\installed-backup'))

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
if (-not [string]::Equals(
    [IO.Path]::GetFullPath($service.PathName.Trim('"')),
    $expectedBinary,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to update $serviceName because its binary path is unexpected."
}

if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory 'FocusGuard.Service.exe') -PathType Leaf)) {
    throw 'Published development service is missing.'
}

if (Test-Path -LiteralPath $backupDirectory) {
    Remove-Item -LiteralPath $backupDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
Get-ChildItem -LiteralPath $stagingDirectory -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $backupDirectory -Force
}

$windowsService = Get-Service -Name $serviceName
try {
    if ($windowsService.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName
        $windowsService.WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(15))
    }

    Get-ChildItem -LiteralPath $sourceDirectory -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stagingDirectory -Force
    }
    Start-Service -Name $serviceName
    $windowsService.WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(15))
} catch {
    Get-ChildItem -LiteralPath $backupDirectory -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stagingDirectory -Force
    }
    Start-Service -Name $serviceName -ErrorAction SilentlyContinue
    throw
}

Get-CimInstance Win32_Service -Filter "Name='$serviceName'" |
    Select-Object Name, State, StartMode, StartName, PathName, ProcessId
