[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$registryPath = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.focusguard.native'

if (Test-Path -LiteralPath $registryPath) {
    Remove-Item -LiteralPath $registryPath -Force
    Write-Output "Removed Edge native-host registration: $registryPath"
} else {
    Write-Output "Edge native-host registration is already absent: $registryPath"
}

if (Test-Path -LiteralPath $registryPath) {
    throw "Edge native-host registration still exists: $registryPath"
}
