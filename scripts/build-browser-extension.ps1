param(
    [string]$Version = '0.1.0',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\browser-extension')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$extensionRoot = Join-Path $repositoryRoot 'extension'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$versionRoot = Join-Path $outputRoot $Version
$stagingRoot = Join-Path $versionRoot 'staging'

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must contain three or four numeric components.' }
$manifest = Get-Content -Raw -LiteralPath (Join-Path $extensionRoot 'manifest.json') | ConvertFrom-Json
if ($manifest.manifest_version -ne 3 -or $manifest.version -ne $Version) {
    throw 'Extension manifest version must be MV3 and match the requested package version.'
}

& (Join-Path $PSScriptRoot 'generate-extension-icons.ps1') | Out-Null

if (Test-Path -LiteralPath $versionRoot) { Remove-Item -LiteralPath $versionRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$relativeFiles = @(
    'manifest.json',
    'status.html',
    'src/native-health.js',
    'src/native-port-client.js',
    'src/navigation-enforcement.js',
    'src/policy-integrity.js',
    'src/status-page.js',
    'src/status-page.css',
    'fonts/Geist-Regular.ttf',
    'fonts/Geist-SemiBold.ttf',
    'fonts/OFL-Geist.txt',
    '_locales/en/messages.json',
    '_locales/es/messages.json'
    'icons/icon-16.png'
    'icons/icon-32.png'
    'icons/icon-48.png'
    'icons/icon-128.png'
)
foreach ($relativeFile in $relativeFiles) {
    $source = Join-Path $extensionRoot $relativeFile
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required extension file is missing: $relativeFile" }
    $destination = Join-Path $stagingRoot $relativeFile
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fixedTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
function New-DeterministicArchive([string]$archivePath) {
    $stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($relativeFile in $relativeFiles | Sort-Object) {
                $entry = $archive.CreateEntry($relativeFile.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $input = [IO.File]::OpenRead((Join-Path $stagingRoot $relativeFile))
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

$packages = @()
foreach ($browser in @('chrome', 'edge')) {
    $archivePath = Join-Path $versionRoot "FocusGuard-$Version-$browser.zip"
    New-DeterministicArchive $archivePath
    $packages += [ordered]@{
        browser = $browser
        file = [IO.Path]::GetFileName($archivePath)
        sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$distribution = [ordered]@{
    version = $Version
    manifestVersion = 3
    extensionId = 'emhdinkjpinjnmldipnejnbeefgonjnj'
    signing = 'Chrome Web Store and Microsoft Edge Add-ons apply publisher signatures after owner upload.'
    packages = $packages
}
$utf8WithoutBom = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText(
    (Join-Path $versionRoot 'distribution.json'),
    ($distribution | ConvertTo-Json -Depth 5),
    $utf8WithoutBom)

Write-Output "Browser extension packages created in $versionRoot"
