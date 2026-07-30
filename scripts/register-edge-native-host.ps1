[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$manifestPath = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\FocusGuard.NativeMessaging\manifests\edge.json'))
$registryPath = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.focusguard.native'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Edge native-host manifest is missing: $manifestPath"
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.name -ne 'com.focusguard.native' -or
    $manifest.type -ne 'stdio' -or
    $manifest.allowed_origins -cnotcontains
        'chrome-extension://emhdinkjpinjnmldipnejnbeefgonjnj/') {
    throw 'Edge manifest does not contain the expected host identity and extension origin.'
}

if (Test-Path -LiteralPath $registryPath) {
    throw "Refusing to overwrite an existing Edge native-host registration: $registryPath"
}

New-Item -Path $registryPath -Force | Out-Null
Set-Item -LiteralPath $registryPath -Value $manifestPath

Get-Item -LiteralPath $registryPath |
    Select-Object Name, Property
Get-ItemPropertyValue -LiteralPath $registryPath -Name '(default)'
