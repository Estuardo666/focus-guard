$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

dotnet restore FocusGuard.slnx
dotnet build FocusGuard.slnx --configuration Release --no-restore
