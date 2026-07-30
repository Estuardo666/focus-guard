[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$registryPath = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.focusguard.native'

if (Test-Path -LiteralPath $registryPath) {
    Remove-Item -LiteralPath $registryPath -Force
    Write-Output "Removed Chrome native-host registration: $registryPath"
} else {
    Write-Output "Chrome native-host registration is already absent: $registryPath"
}

if (Test-Path -LiteralPath $registryPath) {
    throw "Chrome native-host registration still exists: $registryPath"
}
