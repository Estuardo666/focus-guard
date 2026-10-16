$ErrorActionPreference = 'Stop'
$contractModule = Join-Path $PSScriptRoot 'TncApiDeploymentContract.psm1'
$deploymentScript = Join-Path $PSScriptRoot 'Deploy-TncApi.ps1'
Import-Module -Name $contractModule -Force

foreach ($tag in @('phase2-revocation-20261012', 'phase3-settings-cursor-20261015')) {
    if (-not (Test-TncApiImageTag -ImageTag $tag)) { throw "Expected API image tag to be accepted: $tag" }
}
foreach ($tag in @('', 'latest', 'phase1-old', 'phase4-new', 'phase3-', 'phase3-UpperCase', ('phase3-' + ('x' * 49)))) {
    if (Test-TncApiImageTag -ImageTag $tag) { throw "Expected API image tag to be rejected: $tag" }
}

$source = Get-Content -LiteralPath $deploymentScript -Raw
$tagGuardIndex = $source.IndexOf('Test-TncApiImageTag -ImageTag $ImageTag', [StringComparison]::Ordinal)
$cloudCommandLookupIndex = $source.IndexOf('Get-Command gcloud', [StringComparison]::Ordinal)
if ($tagGuardIndex -lt 0 -or $cloudCommandLookupIndex -lt 0 -or $tagGuardIndex -gt $cloudCommandLookupIndex) {
    throw 'API image tag validation must run before checking for Cloud SDK commands.'
}
if ($source -match '\$ImageTag\s*=\s*''[^'']+''') {
    throw 'API deployment must not silently default to an old image tag.'
}
foreach ($guard in @(
    "projectId -cne 'tnc-staging-2026'",
    'The current API digest differs from gcp-staging.json; refusing to replace unexpected state.',
    'if ($deploymentAttempted) {',
    'Wait-ApiRevision $previousImage',
    "[IO.File]::WriteAllText(`$defaultsPath, `$originalDefaults"
)) {
    if (-not $source.Contains($guard, [StringComparison]::Ordinal)) {
        throw "API deployment safety contract is missing: $guard"
    }
}

$missingTagRejected = $false
try { & $deploymentScript }
catch { $missingTagRejected = $_.Exception.Message -like '*supported phase2-/phase3-*' }
if (-not $missingTagRejected) { throw 'Running the deployment script without an explicit image tag was not rejected offline.' }

Write-Output 'TNC API deployment contract tests passed; no Cloud commands were run.'
