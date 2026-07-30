[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard.Development'
$stagingDirectory = [IO.Path]::GetFullPath(
    (Join-Path $env:ProgramData 'FocusGuard\Development\Service'))
$allowedDevelopmentRoot = [IO.Path]::GetFullPath(
    (Join-Path $env:ProgramData 'FocusGuard\Development'))

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Output "$serviceName is not installed."
} else {
    if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName
        $service.WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(15))
    }

    & sc.exe delete $serviceName
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe delete failed with exit code $LASTEXITCODE."
    }

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if ($null -eq (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
            Write-Output "$serviceName was removed."
            break
        }

        Start-Sleep -Milliseconds 250
    }

    if ($null -ne (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
        throw "$serviceName is still registered after the removal timeout."
    }
}

$resolvedStaging = [IO.Path]::GetFullPath($stagingDirectory)
if (-not $resolvedStaging.StartsWith(
    $allowedDevelopmentRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove unexpected staging path: $resolvedStaging"
}

if (Test-Path -LiteralPath $resolvedStaging) {
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    Write-Output "Removed staged development service files: $resolvedStaging"
}
