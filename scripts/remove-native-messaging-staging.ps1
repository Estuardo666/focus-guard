[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$stagingDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot 'FocusGuard.NativeMessaging'))
$expectedDirectory = Join-Path $artifactsRoot 'FocusGuard.NativeMessaging'

if (-not [string]::Equals(
    $stagingDirectory,
    [IO.Path]::GetFullPath($expectedDirectory),
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove unexpected staging path: $stagingDirectory"
}

if (-not $stagingDirectory.StartsWith(
    $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove path outside repository artifacts: $stagingDirectory"
}

if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    Write-Output "Removed native messaging staging: $stagingDirectory"
} else {
    Write-Output "Native messaging staging is already absent: $stagingDirectory"
}
