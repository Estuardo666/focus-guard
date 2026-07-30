$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

dotnet --info
dotnet restore FocusGuard.slnx
Write-Host "Bootstrap completed."
