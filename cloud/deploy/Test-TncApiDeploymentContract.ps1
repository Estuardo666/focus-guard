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

function New-ServiceState {
    param(
        [string]$LatestCreated = 'tnc-api-staging-00017',
        [string]$LatestReady = 'tnc-api-staging-00017',
        [string]$Ready = 'True',
        [switch]$OmitReadyCondition,
        [switch]$OmitRevisionNames
    )

    $status = [ordered]@{ conditions = @() }
    if (-not $OmitRevisionNames) {
        $status.latestCreatedRevisionName = $LatestCreated
        $status.latestReadyRevisionName = $LatestReady
    }
    if (-not $OmitReadyCondition) {
        $status.conditions = @([pscustomobject]@{ type = 'Ready'; status = $Ready })
    }
    [pscustomobject]@{ status = [pscustomobject]$status }
}

if (-not (Test-TncApiServiceStable -Service (New-ServiceState))) {
    throw 'A fully ready service with no in-progress revision should pass the deployment preflight.'
}
foreach ($serviceState in @(
    (New-ServiceState -LatestCreated 'tnc-api-staging-00018' -LatestReady 'tnc-api-staging-00017'),
    (New-ServiceState -Ready 'False'),
    (New-ServiceState -OmitReadyCondition),
    (New-ServiceState -OmitRevisionNames),
    ([pscustomobject]@{})
)) {
    if (Test-TncApiServiceStable -Service $serviceState) {
        throw 'An incomplete or unsettled service state must be rejected before deployment.'
    }
}

$source = Get-Content -LiteralPath $deploymentScript -Raw
$tagGuardIndex = $source.IndexOf('Test-TncApiImageTag -ImageTag $ImageTag', [StringComparison]::Ordinal)
$cloudCommandLookupIndex = $source.IndexOf('Get-Command gcloud', [StringComparison]::Ordinal)
if ($tagGuardIndex -lt 0 -or $cloudCommandLookupIndex -lt 0 -or $tagGuardIndex -gt $cloudCommandLookupIndex) {
    throw 'API image tag validation must run before checking for Cloud SDK commands.'
}
$phase3RouteGuardIndex = $source.IndexOf("if (`$ImageTag.StartsWith('phase3-', [StringComparison]::Ordinal)) {", [StringComparison]::Ordinal)
$deploymentAttemptIndex = $source.IndexOf('$deploymentAttempted = $true', [StringComparison]::Ordinal)
$cursorRouteCheckIndex = $source.IndexOf("Wait-ApiStatus `$serviceUrl '/v1/sync/cursor' 401", [StringComparison]::Ordinal)
$pinWriteIndex = $source.IndexOf('$defaults.apiImageDigest = $digest', [StringComparison]::Ordinal)
$rollbackIndex = $source.IndexOf('if ($deploymentAttempted) {', [StringComparison]::Ordinal)
if ($deploymentAttemptIndex -lt 0 -or $phase3RouteGuardIndex -le $deploymentAttemptIndex -or
    $cursorRouteCheckIndex -le $phase3RouteGuardIndex -or $pinWriteIndex -le $cursorRouteCheckIndex -or
    $rollbackIndex -le $pinWriteIndex) {
    throw 'Phase 3 cursor route verification must run after deployment is marked attempted, before pin persistence, and inside the rollback-protected path.'
}
if ($source -match '\$ImageTag\s*=\s*''[^'']+''') {
    throw 'API deployment must not silently default to an old image tag.'
}
foreach ($guard in @(
    "projectId -cne 'tnc-staging-2026'",
    'The current API digest differs from gcp-staging.json; refusing to replace unexpected state.',
    'if ($deploymentAttempted) {',
    'Wait-ApiRevision $previousImage',
    "`$ImageTag.StartsWith('phase3-', [StringComparison]::Ordinal)",
    "Wait-ApiStatus `$serviceUrl '/v1/sync/cursor' 401",
    'Wait-ApiStatus $serviceUrl $path 200',
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

$stableServiceCheckIndex = $source.IndexOf('Test-TncApiServiceStable -Service $service', [StringComparison]::Ordinal)
$imageBuildIndex = $source.IndexOf('& $docker.Source build', [StringComparison]::Ordinal)
if ($stableServiceCheckIndex -lt 0 -or $imageBuildIndex -lt 0 -or $stableServiceCheckIndex -gt $imageBuildIndex) {
    throw 'Cloud Run revision stability must be checked before building or pushing a replacement image.'
}

Write-Output 'TNC API deployment contract tests passed; no Cloud commands were run.'
