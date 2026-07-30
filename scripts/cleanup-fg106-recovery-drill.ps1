[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard.Development'
$stateDirectory = [IO.Path]::GetFullPath(
    (Join-Path $env:ProgramData 'FocusGuard\Development\StrictPersistence'))
$expectedStateDirectory = 'C:\ProgramData\FocusGuard\Development\StrictPersistence'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}
if (-not [string]::Equals(
    $stateDirectory,
    $expectedStateDirectory,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unexpected state directory: $stateDirectory"
}

$service = Get-Service -Name $serviceName -ErrorAction Stop
if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
    Stop-Service -Name $serviceName
    $service.WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Stopped,
        [TimeSpan]::FromSeconds(15))
}
if (Test-Path -LiteralPath $stateDirectory) {
    Remove-Item -LiteralPath $stateDirectory -Recurse -Force
}
Start-Service -Name $serviceName
$service.WaitForStatus(
    [ServiceProcess.ServiceControllerStatus]::Running,
    [TimeSpan]::FromSeconds(15))

[pscustomobject]@{
    Service = $serviceName
    State = (Get-Service -Name $serviceName).Status
    StateDirectoryExists = Test-Path -LiteralPath $stateDirectory
}
