[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Version = '0.1.7',
    [string]$OutputDirectory = '',
    [string]$PayloadSuffix = '',
    [string]$SetupPublishSuffix = ''
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

# The Avalonia Desktop app is no longer shipped; FocusGuard.UI (FocusGuard.Host.Windows) is the only interface.
foreach ($project in @('FocusGuard.Service', 'FocusGuard.Recovery', 'FocusGuard.ProcessAgent', 'FocusGuard.NativeMessaging', 'FocusGuard.Host.Windows', 'FocusGuard.DrillClient')) {
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

foreach ($scriptName in @('install-focusguard.ps1', 'uninstall-focusguard.ps1', 'New-FocusGuardIpcPolicy.ps1', 'Get-FocusGuardInteractiveOwnerSid.ps1')) {
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
        'publish\FocusGuard.ProcessAgent\FocusGuard.ProcessAgent.exe'
        ,'publish\FocusGuard.NativeMessaging\FocusGuard.NativeMessaging.exe'
        ,'publish\FocusGuard.Host.Windows\FocusGuard.UI.exe'
        ,'publish\FocusGuard.Host.Windows\FocusGuard.UI.dll'
        ,'publish\FocusGuard.Host.Windows\ui\index.html'
        ,'publish\FocusGuard.Host.Windows\ui\host.js'
        ,'publish\FocusGuard.Host.Windows\ui\host.css'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\ScheduleEditor.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\PreferencesPanel.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\ProfileEditor.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\SessionHistoryDetail.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\DashboardStart.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\FocusOrbScene.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\jsx-runtime.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\CompactFocus.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\SessionEndExperience.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\StrictReviewPanel.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\WorkflowViews.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\strict.js'
        ,'publish\FocusGuard.Host.Windows\ui\chunks\count-label.js'
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

# Build Setup only after the complete install payload exists. The payload is
# embedded as a resource so FocusGuard.Setup.exe remains coherent when copied or
# downloaded without any adjacent files.
$payloadName = $Version
if (-not [string]::IsNullOrWhiteSpace($PayloadSuffix)) {
    if ($PayloadSuffix -notmatch '^[a-zA-Z0-9][a-zA-Z0-9-]{0,31}$') {
        throw 'PayloadSuffix must contain only letters, digits, and hyphens.'
    }
    $payloadName = "$Version-$PayloadSuffix"
}
$payloadRoot = Join-Path $repositoryRoot "artifacts\setup-payload\$payloadName"
$payloadArchive = Join-Path $payloadRoot 'FocusGuard.Payload.zip'
if (Test-Path -LiteralPath $payloadRoot) { Remove-Item -LiteralPath $payloadRoot -Recurse -Force }
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
Compress-Archive -Path (Join-Path $outputRoot '*') -DestinationPath $payloadArchive

$setupProject = Join-Path $repositoryRoot 'src\FocusGuard.Setup\FocusGuard.Setup.csproj'
$setupPublishName = $Version
if (-not [string]::IsNullOrWhiteSpace($SetupPublishSuffix)) {
    if ($SetupPublishSuffix -notmatch '^[a-zA-Z0-9][a-zA-Z0-9-]{0,31}$') {
        throw 'SetupPublishSuffix must contain only letters, digits, and hyphens.'
    }
    $setupPublishName = "$Version-$SetupPublishSuffix"
}
$setupPublishRoot = Join-Path $repositoryRoot "artifacts\setup-publish\$setupPublishName"
if (Test-Path -LiteralPath $setupPublishRoot) { Remove-Item -LiteralPath $setupPublishRoot -Recurse -Force }
& dotnet publish $setupProject --configuration $Configuration --runtime $RuntimeIdentifier `
    --self-contained true --property:PublishSingleFile=true `
    --property:IncludeNativeLibrariesForSelfExtract=true --property:PublishTrimmed=false `
    "--property:FocusGuardPayloadArchive=$payloadArchive" `
    --output $setupPublishRoot
if ($LASTEXITCODE -ne 0) { throw "Publishing FocusGuard.Setup failed with exit code $LASTEXITCODE." }
$embeddedHashReceipt = Join-Path $setupPublishRoot 'embedded-ui.sha256'
$hashProcess = Start-Process `
    -FilePath (Join-Path $setupPublishRoot 'FocusGuard.Setup.exe') `
    -ArgumentList @(
        '--package-file-hash',
        '"publish\FocusGuard.Host.Windows\FocusGuard.UI.dll"',
        ('"{0}"' -f $embeddedHashReceipt)) `
    -Wait `
    -PassThru `
    -WindowStyle Hidden
if ($hashProcess.ExitCode -ne 0) { throw "Reading the embedded UI payload failed with exit code $($hashProcess.ExitCode)." }
if (-not (Test-Path -LiteralPath $embeddedHashReceipt)) { throw 'Standalone Setup did not produce the embedded UI hash receipt.' }
$expectedUiHash = (Get-FileHash `
    -LiteralPath (Join-Path $publishRoot 'FocusGuard.Host.Windows\FocusGuard.UI.dll') `
    -Algorithm SHA256).Hash
$embeddedUiHash = (Get-Content -LiteralPath $embeddedHashReceipt -Raw).Trim()
if ($embeddedUiHash -ne $expectedUiHash) {
    throw "Standalone Setup embedded a stale UI payload. Expected $expectedUiHash but found $embeddedUiHash."
}
Remove-Item -LiteralPath $embeddedHashReceipt -Force
foreach ($relative in @(
    'publish\FocusGuard.Host.Windows\FocusGuard.UI.dll',
    'publish\FocusGuard.Host.Windows\ui\index.html',
    'publish\FocusGuard.Host.Windows\ui\host.js',
    'publish\FocusGuard.Host.Windows\ui\host.css',
    'publish\FocusGuard.Host.Windows\ui\chunks\ScheduleEditor.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\PreferencesPanel.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\ProfileEditor.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\SessionHistoryDetail.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\DashboardStart.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\FocusOrbScene.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\jsx-runtime.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\CompactFocus.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\SessionEndExperience.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\StrictReviewPanel.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\WorkflowViews.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\strict.js',
    'publish\FocusGuard.Host.Windows\ui\chunks\count-label.js')) {
    $receipt = Join-Path $setupPublishRoot 'embedded-host.sha256'
    $process = Start-Process -FilePath (Join-Path $setupPublishRoot 'FocusGuard.Setup.exe') `
        -ArgumentList @('--package-file-hash', ('"{0}"' -f $relative), ('"{0}"' -f $receipt)) `
        -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Reading embedded host file failed: $relative" }
    $expected = (Get-FileHash -LiteralPath (Join-Path $outputRoot $relative) -Algorithm SHA256).Hash
    if ((Get-Content -LiteralPath $receipt -Raw).Trim() -ne $expected) {
        throw "Standalone Setup embedded a stale host file: $relative"
    }
    Remove-Item -LiteralPath $receipt -Force
}
Copy-Item -LiteralPath (Join-Path $setupPublishRoot 'FocusGuard.Setup.exe') -Destination (Join-Path $outputRoot 'FocusGuard.Setup.exe')
$standaloneSetup = Join-Path (Split-Path $outputRoot -Parent) "FocusGuard-$Version-Setup.exe"
Copy-Item -LiteralPath (Join-Path $setupPublishRoot 'FocusGuard.Setup.exe') -Destination $standaloneSetup -Force

$archiveName = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    "FocusGuard-$Version-win-x64.zip"
} else {
    "FocusGuard-$Version-win-x64-menu.zip"
}
$archive = Join-Path (Split-Path $outputRoot -Parent) $archiveName
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $outputRoot '*') -DestinationPath $archive
Write-Output "Created $archive"
Write-Output "Created standalone installer $standaloneSetup"
