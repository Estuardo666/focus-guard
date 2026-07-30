$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

& dotnet restore FocusGuard.slnx
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
& dotnet format FocusGuard.slnx --verify-no-changes --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet format verification failed with exit code $LASTEXITCODE." }
& dotnet build FocusGuard.slnx --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

$artifactDirectory = Join-Path (Get-Location) "artifacts\test-results"
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
& dotnet test FocusGuard.slnx --configuration Release --no-build --collect:"XPlat Code Coverage" --results-directory $artifactDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }

Push-Location extension
try {
    & npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    & npm run check
    if ($LASTEXITCODE -ne 0) { throw "TypeScript validation failed with exit code $LASTEXITCODE." }
    & npm test
    if ($LASTEXITCODE -ne 0) { throw "Extension tests failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

& (Join-Path $PSScriptRoot "verify-report.ps1")
if ($LASTEXITCODE -ne 0) { throw "Report verification failed with exit code $LASTEXITCODE." }
& (Join-Path $PSScriptRoot "test-installer.ps1")
if ($LASTEXITCODE -ne 0) { throw "Installer tests failed with exit code $LASTEXITCODE." }
& (Join-Path $PSScriptRoot "test-fg112-reboot-drill.ps1")
if ($LASTEXITCODE -ne 0) { throw "FG-112 reboot drill tests failed with exit code $LASTEXITCODE." }
& (Join-Path $PSScriptRoot "test-browser-extension-package.ps1")
if ($LASTEXITCODE -ne 0) { throw "Browser extension package tests failed with exit code $LASTEXITCODE." }
