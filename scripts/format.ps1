$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

& dotnet format FocusGuard.slnx --verify-no-changes --no-restore --verbosity minimal
