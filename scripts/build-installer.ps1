[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Version = '0.1.0',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\installer\FocusGuard-$Version"))
} else {
    [IO.Path]::GetFullPath($OutputDirectory)
}
$publishRoot = Join-Path $outputRoot 'publish'

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

foreach ($project in @('FocusGuard.Service', 'FocusGuard.Recovery', 'FocusGuard.Desktop', 'FocusGuard.ProcessAgent', 'FocusGuard.NativeMessaging')) {
    $projectPath = Join-Path $repositoryRoot "src\$project\$project.csproj"
    $destination = Join-Path $publishRoot $project
    & dotnet publish $projectPath --configuration $Configuration --runtime $RuntimeIdentifier `
        --self-contained true --property:FocusGuardProduction=true --output $destination
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing $project failed with exit code $LASTEXITCODE."
    }
}

& (Join-Path $PSScriptRoot 'build-browser-extension.ps1') -Version $Version | Out-Null
$browserSource = Join-Path $repositoryRoot "artifacts\browser-extension\$Version"
$browserDestination = Join-Path $outputRoot 'browser-extension'
Copy-Item -LiteralPath $browserSource -Destination $browserDestination -Recurse

$setupProject = Join-Path $repositoryRoot 'src\FocusGuard.Setup\FocusGuard.Setup.csproj'
$setupPublishRoot = Join-Path $repositoryRoot 'artifacts\setup-publish'
if (Test-Path -LiteralPath $setupPublishRoot) { Remove-Item -LiteralPath $setupPublishRoot -Recurse -Force }
& dotnet publish $setupProject --configuration $Configuration --runtime $RuntimeIdentifier `
    --self-contained true --property:PublishSingleFile=true `
    --property:IncludeNativeLibrariesForSelfExtract=true --property:PublishTrimmed=false `
    --output $setupPublishRoot
if ($LASTEXITCODE -ne 0) { throw "Publishing FocusGuard.Setup failed with exit code $LASTEXITCODE." }
Copy-Item -LiteralPath (Join-Path $setupPublishRoot 'FocusGuard.Setup.exe') -Destination (Join-Path $outputRoot 'FocusGuard.Setup.exe')
foreach ($scriptName in @('install-focusguard.ps1', 'uninstall-focusguard.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination (Join-Path $outputRoot $scriptName)
}

$nativeManifestDirectory = Join-Path $outputRoot 'native-messaging'
New-Item -ItemType Directory -Path $nativeManifestDirectory -Force | Out-Null
$nativeHostPath = Join-Path ${env:ProgramFiles} 'FocusGuard\FocusGuard.NativeMessaging.exe'
$nativeManifest = [ordered]@{
    name = 'com.focusguard.native'
    description = 'FocusGuard native messaging host'
    path = $nativeHostPath
    type = 'stdio'
    allowed_origins = @('chrome-extension://emhdinkjpinjnmldipnejnbeefgonjnj/')
}
$utf8WithoutBom = [Text.UTF8Encoding]::new($false)
$nativeJson = $nativeManifest | ConvertTo-Json -Depth 4
foreach ($browser in @('chrome', 'edge')) {
    [IO.File]::WriteAllText((Join-Path $nativeManifestDirectory "$browser.json"), $nativeJson, $utf8WithoutBom)
}
[IO.File]::WriteAllText(
    (Join-Path $nativeManifestDirectory 'focusguard.native.json'),
    $nativeJson,
    $utf8WithoutBom)

$manifest = [ordered]@{
    SchemaVersion = 1
    Product = 'FocusGuard'
    Version = $Version
    RuntimeIdentifier = $RuntimeIdentifier
    ServiceName = 'FocusGuard'
    ServiceAccount = 'NT AUTHORITY\LocalService'
    Files = @(
        'FocusGuard.Setup.exe',
        'install-focusguard.ps1',
        'uninstall-focusguard.ps1',
        'publish\FocusGuard.Service\FocusGuard.Service.exe',
        'publish\FocusGuard.Recovery\FocusGuard.Recovery.exe',
        'publish\FocusGuard.Desktop\FocusGuard.Desktop.exe'
        ,'publish\FocusGuard.ProcessAgent\FocusGuard.ProcessAgent.exe'
        ,'publish\FocusGuard.NativeMessaging\FocusGuard.NativeMessaging.exe'
        ,'native-messaging\chrome.json'
        ,'native-messaging\edge.json'
        ,'native-messaging\focusguard.native.json'
        ,"browser-extension\FocusGuard-$Version-chrome.zip"
        ,"browser-extension\FocusGuard-$Version-edge.zip"
        ,'browser-extension\distribution.json'
    )
}
$manifestPath = Join-Path $outputRoot 'manifest.json'
$manifestJson = $manifest | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($manifestPath, $manifestJson, $utf8WithoutBom)

$archiveName = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    "FocusGuard-$Version-win-x64.zip"
} else {
    "FocusGuard-$Version-win-x64-menu.zip"
}
$archive = Join-Path (Split-Path $outputRoot -Parent) $archiveName
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $outputRoot '*') -DestinationPath $archive
Write-Output "Created $archive"
