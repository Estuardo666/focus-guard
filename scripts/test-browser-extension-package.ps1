$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildScript = Join-Path $PSScriptRoot 'build-browser-extension.ps1'
$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile($buildScript, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -ne 0) { throw 'Browser extension build script has PowerShell parse errors.' }

& $buildScript
if (-not $?) { throw 'Browser extension package build failed.' }
$versionRoot = Join-Path $repositoryRoot 'artifacts\browser-extension\0.1.0'
$distribution = Get-Content -Raw -LiteralPath (Join-Path $versionRoot 'distribution.json') | ConvertFrom-Json
if ($distribution.packages.Count -ne 2 -or $distribution.extensionId -ne 'emhdinkjpinjnmldipnejnbeefgonjnj') {
    throw 'Distribution manifest does not bind both packages to the expected extension identity.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $distribution.packages) {
    $archivePath = Join-Path $versionRoot $package.file
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $package.sha256) { throw "Package hash mismatch: $($package.file)" }
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $names = @($archive.Entries | ForEach-Object FullName)
        foreach ($required in @('manifest.json', '_locales/en/messages.json', '_locales/es/messages.json', 'src/navigation-enforcement.js', 'icons/icon-16.png', 'icons/icon-32.png', 'icons/icon-48.png', 'icons/icon-128.png')) {
            if ($names -cnotcontains $required) { throw "$($package.file) is missing $required" }
        }
        if ($names | Where-Object { $_ -match 'node_modules|test/|package-lock|\.pem$|\.key$' }) {
            throw "$($package.file) contains development files or signing secrets."
        }
    } finally { $archive.Dispose() }
}

Write-Output 'Chrome and Edge extension package tests passed.'
