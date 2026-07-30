[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$PurgeData
)

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard'
$installRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramFiles} 'FocusGuard'))
$dataRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramData} 'FocusGuard'))
$strictDirectory = Join-Path $dataRoot 'StrictPersistence'
$activePolicy = Join-Path $strictDirectory 'active-policy.json'
$pendingRecovery = Join-Path $strictDirectory 'recovery-request.json'
$nativeRegistrations = @(
    'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.focusguard.native',
    'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.focusguard.native'
)
$startMenuShortcut = Join-Path ${env:ProgramData} 'Microsoft\Windows\Start Menu\Programs\FocusGuard.lnk'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell window.' }

if ((Test-Path -LiteralPath $activePolicy -PathType Leaf) -or (Test-Path -LiteralPath $pendingRecovery -PathType Leaf)) {
    throw "An active or pending strict session exists. Use the elevated FocusGuard Recovery utility and wait until the displayed release time before uninstalling. No files were removed."
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $PSCmdlet.ShouldProcess($serviceName, 'Stop and remove FocusGuard service')) {
    if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(15))
    }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed with exit code $LASTEXITCODE." }
}

if ($PSCmdlet.ShouldProcess($installRoot, 'Remove FocusGuard binaries')) {
    if (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf) {
        $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($startMenuShortcut)
        if ($shortcut.TargetPath -and [IO.Path]::GetFullPath($shortcut.TargetPath).StartsWith(
            $installRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $startMenuShortcut -Force
        }
    }
    foreach ($registration in $nativeRegistrations) {
        if (Test-Path -LiteralPath $registration) {
            $registeredManifest = (Get-Item -LiteralPath $registration).GetValue('')
            if ($registeredManifest -and [IO.Path]::GetFullPath($registeredManifest).StartsWith(
                $installRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $registration -Recurse -Force
            }
        }
    }
    if (Test-Path -LiteralPath $installRoot) { Remove-Item -LiteralPath $installRoot -Recurse -Force }
}
if ($PurgeData -and $PSCmdlet.ShouldProcess($dataRoot, 'Remove FocusGuard machine state')) {
    if (Test-Path -LiteralPath $dataRoot) { Remove-Item -LiteralPath $dataRoot -Recurse -Force }
} elseif (Test-Path -LiteralPath $dataRoot) {
    Write-Output "Preserved FocusGuard machine state at $dataRoot. Use -PurgeData only after reviewing local audit evidence."
}
