# Shared non-secret deployment defaults. Product domains and OIDC realm names are separate contracts.
$gcpStaging = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'gcp-staging.json') -Raw | ConvertFrom-Json
if ($gcpStaging.projectId -notmatch '^tnc-[a-z0-9-]+$' -or $gcpStaging.prefix -cne 'tnc') {
    throw 'New Google Cloud infrastructure must use the authorized tnc naming convention.'
}
$projectId = [string]$gcpStaging.projectId
$infrastructurePrefix = [string]$gcpStaging.prefix
if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Google\Cloud SDK\google-cloud-sdk\bin'
    if (-not (Test-Path -LiteralPath (Join-Path $sdkBin 'gcloud.cmd'))) { throw 'Google Cloud CLI unavailable.' }
    $env:PATH = $sdkBin + ';' + $env:PATH
}
