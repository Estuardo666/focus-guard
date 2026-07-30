[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-p]{32}$')]
    [string]$ChromeExtensionId,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-p]{32}$')]
    [string]$EdgeExtensionId
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$stagingDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot 'FocusGuard.NativeMessaging'))
$hostDirectory = Join-Path $stagingDirectory 'host'
$manifestDirectory = Join-Path $stagingDirectory 'manifests'
$projectPath = Join-Path $repositoryRoot `
    'src\FocusGuard.NativeMessaging\FocusGuard.NativeMessaging.csproj'
$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'

if (-not $stagingDirectory.StartsWith(
    $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to stage outside repository artifacts: $stagingDirectory"
}

if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw "dotnet executable not found: $dotnetPath"
}

if (Test-Path -LiteralPath $stagingDirectory) {
    & (Join-Path $PSScriptRoot 'remove-native-messaging-staging.ps1')
}

& $dotnetPath publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --property:FocusGuardProduction=true `
    --output $hostDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$hostPath = [IO.Path]::GetFullPath(
    (Join-Path $hostDirectory 'FocusGuard.NativeMessaging.exe'))
if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw "Published native messaging host not found: $hostPath"
}

$allowedOrigins = @(
    "chrome-extension://$ChromeExtensionId/"
    "chrome-extension://$EdgeExtensionId/"
) | Select-Object -Unique

$manifest = [ordered]@{
    name = 'com.focusguard.native'
    description = 'FocusGuard native messaging host'
    path = $hostPath
    type = 'stdio'
    allowed_origins = [object[]]$allowedOrigins
}

New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
$json = $manifest | ConvertTo-Json -Depth 4
$localManifestPath = Join-Path $hostDirectory 'focusguard.native.json'
$chromeManifestPath = Join-Path $manifestDirectory 'chrome.json'
$edgeManifestPath = Join-Path $manifestDirectory 'edge.json'

Set-Content -LiteralPath $localManifestPath -Value $json -Encoding utf8NoBOM
Set-Content -LiteralPath $chromeManifestPath -Value $json -Encoding utf8NoBOM
Set-Content -LiteralPath $edgeManifestPath -Value $json -Encoding utf8NoBOM

[pscustomobject]@{
    HostPath = $hostPath
    ChromeManifest = $chromeManifestPath
    EdgeManifest = $edgeManifestPath
    AllowedOrigins = $allowedOrigins -join ', '
    BrowserRegistrationChanged = $false
}
