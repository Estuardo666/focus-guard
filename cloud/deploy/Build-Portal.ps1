$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
# exFAT does not implement reparse points/junctions required by Next's filesystem
# tooling. Build an explicit source mirror on the user's NTFS volume; no elevation.
$buildRoot = Join-Path $env:USERPROFILE '.codex/focusguard-staging-build/portal'
New-Item -ItemType Directory -Force $buildRoot | Out-Null
robocopy (Join-Path $repoRoot 'cloud/portal') $buildRoot /E /XD node_modules .next .vercel /XF .env* /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw 'Source copy failed.' }
Push-Location $buildRoot
try {
    $env:NEXT_TELEMETRY_DISABLED = '1'
    npm ci --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restoration failed.' }
    npm test
    if ($LASTEXITCODE -ne 0) { throw 'Portal checks failed.' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Portal build failed.' }
    Write-Output ('PORTAL_BUILD_READY=' + $buildRoot)
} finally { Pop-Location }
