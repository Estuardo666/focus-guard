$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$artifactDirectory = Join-Path (Get-Location) "artifacts\test-results"
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
dotnet test FocusGuard.slnx --configuration Release --collect:"XPlat Code Coverage" --results-directory $artifactDirectory

Push-Location extension
try {
    npm test
}
finally {
    Pop-Location
}
