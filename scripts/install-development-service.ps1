[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard.Development'
$displayName = 'FocusGuard Development Service'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceDirectory = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\FocusGuard.Service\win-x64'))
$sourceBinaryPath = [IO.Path]::GetFullPath(
    (Join-Path $sourceDirectory 'FocusGuard.Service.exe'))
$stagingDirectory = [IO.Path]::GetFullPath(
    (Join-Path $env:ProgramData 'FocusGuard\Development\Service'))
$binaryPath = [IO.Path]::GetFullPath(
    (Join-Path $stagingDirectory 'FocusGuard.Service.exe'))

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}

if (-not (Test-Path -LiteralPath $sourceBinaryPath -PathType Leaf)) {
    throw "Release service executable not found: $sourceBinaryPath"
}

$existing = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    $existingPath = $existing.PathName.Trim('"')
    if (-not [string]::Equals(
        [IO.Path]::GetFullPath($existingPath),
        $binaryPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify $serviceName because it points to a different executable."
    }

    Write-Output "$serviceName already exists at the expected path."
} else {
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $sourceDirectory -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stagingDirectory -Force
    }
    if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
        throw "Staged service executable not found: $binaryPath"
    }

    $createArguments = @(
        'create',
        $serviceName,
        'binPath=',
        "`"$binaryPath`"",
        'start=',
        'demand',
        'obj=',
        'NT AUTHORITY\LocalService',
        'DisplayName=',
        "`"$displayName`""
    )
    & sc.exe $createArguments
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe create failed with exit code $LASTEXITCODE."
    }

    & sc.exe description $serviceName 'Non-enforcing owner-present development host for FocusGuard.'
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe description failed with exit code $LASTEXITCODE."
    }
}

$service = Get-Service -Name $serviceName
if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Running) {
    Start-Service -Name $serviceName
    $service.WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(15))
}

Get-CimInstance Win32_Service -Filter "Name='$serviceName'" |
    Select-Object Name, DisplayName, State, StartMode, StartName, PathName
