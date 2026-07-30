[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\FocusGuard.Service\FocusGuard.Service.csproj'
$outputPath = Join-Path $repositoryRoot 'artifacts\FocusGuard.Service\win-x64'
$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Publish from a normal non-elevated PowerShell session.'
}

if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw "dotnet executable not found: $dotnetPath"
}

& $dotnetPath publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$serviceBinary = Join-Path $outputPath 'FocusGuard.Service.exe'
if (-not (Test-Path -LiteralPath $serviceBinary -PathType Leaf)) {
    throw "Published service executable not found: $serviceBinary"
}

Get-Item -LiteralPath $serviceBinary | Select-Object FullName, Length, LastWriteTimeUtc
