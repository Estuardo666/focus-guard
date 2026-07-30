[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$profilePath = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot 'edge-fg103-profile'))
$expectedPath = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\edge-fg103-profile'))

if (-not [string]::Equals(
    $profilePath,
    $expectedPath,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove unexpected Edge profile: $profilePath"
}

if (-not $profilePath.StartsWith(
    $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove Edge profile outside artifacts: $profilePath"
}

$running = Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
    Where-Object { $_.CommandLine -like "*$profilePath*" }
if ($running) {
    throw 'Refusing to remove the temporary profile while its Edge processes are running.'
}

if (Test-Path -LiteralPath $profilePath) {
    Remove-Item -LiteralPath $profilePath -Recurse -Force
    Write-Output "Removed temporary Edge drill profile: $profilePath"
} else {
    Write-Output "Temporary Edge drill profile is already absent: $profilePath"
}

$negativeTargetPath = Join-Path $artifactsRoot 'edge-fg103-negative-target.txt'
if (Test-Path -LiteralPath $negativeTargetPath) {
    Remove-Item -LiteralPath $negativeTargetPath -Force
}
