[CmdletBinding()]
param(
    [string]$ImageTag = '',
    [int]$ReadyTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$deploymentContractPath = Join-Path $PSScriptRoot 'TncApiDeploymentContract.psm1'
Import-Module -Name $deploymentContractPath -Force
$defaultsPath = Join-Path $PSScriptRoot 'gcp-staging.json'
$defaults = Get-Content -LiteralPath $defaultsPath -Raw | ConvertFrom-Json
if ($defaults.account -cne 'transcitynorwalk@gmail.com' -or $defaults.projectId -cne 'tnc-staging-2026' -or
    $defaults.apiService -cne 'tnc-api-staging' -or $defaults.region -cne 'us-east4') {
    throw 'API deployment target does not match the authorized TNC staging project.'
}
if (-not (Test-TncApiImageTag -ImageTag $ImageTag) -or $ReadyTimeoutSeconds -notin 60..1800) {
    throw 'API image tag must use a supported phase2-/phase3- prefix and readiness timeout must be 60-1800 seconds.'
}
$gcloud = Get-Command gcloud -ErrorAction Stop
$docker = Get-Command docker -ErrorAction Stop
$account = [string]$defaults.account
$projectId = [string]$defaults.projectId
$region = [string]$defaults.region
$serviceName = [string]$defaults.apiService
$repository = [string]$defaults.artifactRepository
$registry = "${region}-docker.pkg.dev/$projectId/$repository"
$taggedImage = "$registry/api`:$ImageTag"
$serviceArgs = @('run', 'services', 'describe', $serviceName, "--project=$projectId",
    "--region=$region", "--account=$account", '--format=json')
$activeAccount = (& $gcloud.Source config get-value account "--account=$account" 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $activeAccount -cne $account) { throw 'The selected Google account is not authenticated.' }

$service = (& $gcloud.Source @serviceArgs | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not (Test-TncApiServiceStable -Service $service)) {
    throw 'The existing API staging service is not fully Ready or has an in-progress revision; refusing deployment.'
}
$previousImage = [string]$service.spec.template.spec.containers[0].image
$previousDigest = ($previousImage -split '@')[-1]
if ($previousDigest -cne [string]$defaults.apiImageDigest) {
    throw 'The current API digest differs from gcp-staging.json; refusing to replace unexpected state.'
}
$serviceUrl = [string]$service.status.url
$deploymentAttempted = $false
$originalDefaults = Get-Content -LiteralPath $defaultsPath -Raw

function Wait-ApiRevision([string]$ExpectedImage, [string]$Description) {
    $deadline = [DateTime]::UtcNow.AddSeconds($ReadyTimeoutSeconds)
    do {
        $state = (& $gcloud.Source @serviceArgs | ConvertFrom-Json)
        if ($LASTEXITCODE -ne 0) { throw "Could not read API service state while waiting for $Description." }
        $readyCondition = $state.status.conditions | Where-Object type -eq 'Ready'
        $readyImage = [string]$state.spec.template.spec.containers[0].image
        if ($state.status.latestReadyRevisionName -eq $state.status.latestCreatedRevisionName -and
            $readyCondition.status -eq 'True' -and $readyImage -ceq $ExpectedImage) {
            return $state
        }
        if ($readyCondition.status -eq 'False') {
            throw "API revision did not become Ready ($($readyCondition.reason))."
        }
        Start-Sleep -Seconds 5
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "API readiness timed out while waiting for $Description."
}

function Wait-ApiStatus([string]$BaseUrl, [string]$Path, [int]$ExpectedStatus) {
    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Min($ReadyTimeoutSeconds, 180))
    $lastStatus = 0
    $lastFailure = 'no response'
    do {
        try {
            $response = Invoke-WebRequest -Uri ($BaseUrl + $Path) -TimeoutSec 30 -UseBasicParsing -ErrorAction Stop
            $lastStatus = [int]$response.StatusCode
            $lastFailure = 'HTTP ' + $lastStatus
        }
        catch {
            $requestException = $_.Exception
            $response = $requestException.Response
            if ($null -ne $response) {
                try { $lastStatus = [int]$response.StatusCode } catch { $lastStatus = 0 }
                $lastFailure = if ($lastStatus -gt 0) { 'HTTP ' + $lastStatus } else { $requestException.GetType().Name }
            } else {
                $lastStatus = 0
                $lastFailure = $requestException.GetType().Name
            }
        }
        if ($lastStatus -eq $ExpectedStatus) { return }
        if ($lastStatus -ne 0 -and $lastStatus -ne 429 -and $lastStatus -lt 500) {
            throw "API smoke check failed for $Path; expected HTTP $ExpectedStatus ($lastFailure)."
        }
        Start-Sleep -Seconds 3
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "API smoke check did not reach HTTP $ExpectedStatus for $Path ($lastFailure)."
}

try {
    & $docker.Source build --platform linux/amd64 --file (Join-Path $repositoryRoot 'cloud/api/Dockerfile') --tag $taggedImage $repositoryRoot
    if ($LASTEXITCODE -ne 0) { throw 'Building the API image failed.' }
    & $gcloud.Source auth configure-docker "$region-docker.pkg.dev" "--account=$account" --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Configuring Artifact Registry authentication failed.' }
    & $docker.Source push $taggedImage
    if ($LASTEXITCODE -ne 0) { throw 'Pushing the API image failed.' }

    $describeArgs = @('artifacts', 'docker', 'images', 'describe', $taggedImage,
        "--project=$projectId", "--account=$account", '--format=json')
    $imageDescription = (& $gcloud.Source @describeArgs | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or $imageDescription.image_summary.digest -notmatch '^sha256:[0-9a-f]{64}$') {
        throw 'Artifact Registry did not return an immutable image digest.'
    }
    $digest = [string]$imageDescription.image_summary.digest
    $pinnedImage = "$registry/api@$digest"

    $deploymentAttempted = $true
    $updateArgs = @('run', 'services', 'update', $serviceName, "--image=$pinnedImage",
        "--project=$projectId", "--region=$region", "--account=$account", '--quiet')
    & $gcloud.Source @updateArgs
    if ($LASTEXITCODE -ne 0) { throw 'Cloud Run rejected the API image update.' }
    $ready = Wait-ApiRevision $pinnedImage 'new API revision'
    foreach ($path in @('/health/live', '/health/ready')) { Wait-ApiStatus $serviceUrl $path 200 }
    if ($ImageTag.StartsWith('phase3-', [StringComparison]::Ordinal)) {
        Wait-ApiStatus $serviceUrl '/v1/sync/cursor' 401
    }

    $defaults.apiImageDigest = $digest
    [IO.File]::WriteAllText($defaultsPath, ($defaults | ConvertTo-Json -Depth 10) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
    Write-Output "API deployed: $($ready.status.latestReadyRevisionName)"
    Write-Output "Image digest: $digest"
    Write-Output 'Health smoke checks: /health/live 200; /health/ready 200.'
    if ($ImageTag.StartsWith('phase3-', [StringComparison]::Ordinal)) {
        Write-Output 'Phase 3 route smoke check: /v1/sync/cursor without credentials returns 401.'
    }
}
catch {
    $failure = $_
    if ($deploymentAttempted) {
        Write-Warning 'API update failed verification; restoring the previously pinned image.'
        $rollbackArgs = @('run', 'services', 'update', $serviceName, "--image=$previousImage",
            "--project=$projectId", "--region=$region", "--account=$account", '--quiet')
        & $gcloud.Source @rollbackArgs
        if ($LASTEXITCODE -ne 0) { throw 'API deployment failed and the previous image rollback command also failed.' }
        $null = Wait-ApiRevision $previousImage 'previous API image rollback'
        [IO.File]::WriteAllText($defaultsPath, $originalDefaults, [Text.UTF8Encoding]::new($false))
    }
    throw $failure
}
finally {
    $service = $null
    $imageDescription = $null
}
