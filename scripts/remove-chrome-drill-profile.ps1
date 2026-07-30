[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$profilePath = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot 'chrome-fg103-profile'))
$expectedPath = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\chrome-fg103-profile'))

if (-not [string]::Equals(
    $profilePath,
    $expectedPath,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove unexpected Chrome profile: $profilePath"
}

if (-not $profilePath.StartsWith(
    $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove Chrome profile outside artifacts: $profilePath"
}

$running = Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" |
    Where-Object { $_.CommandLine -like "*$profilePath*" }
if ($running) {
    throw 'Refusing to remove the temporary profile while its Chrome processes are running.'
}

if (Test-Path -LiteralPath $profilePath) {
    Remove-Item -LiteralPath $profilePath -Recurse -Force
    Write-Output "Removed temporary Chrome drill profile: $profilePath"
} else {
    Write-Output "Temporary Chrome drill profile is already absent: $profilePath"
}

foreach ($name in @('chrome-fg103-stderr.log', 'chrome-fg103-stdout.log')) {
    $logPath = Join-Path $artifactsRoot $name
    if (Test-Path -LiteralPath $logPath) {
        Remove-Item -LiteralPath $logPath -Force
    }
}
