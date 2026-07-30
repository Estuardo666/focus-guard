$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

dotnet run --project src/FocusGuard.Desktop/FocusGuard.Desktop.csproj
