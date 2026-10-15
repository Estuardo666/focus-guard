param([switch]$VerifyOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Gcp-Staging.ps1')
. (Join-Path $PSScriptRoot 'Native-SessionPolicy.ps1')
$base=(gcloud run services describe $gcpStaging.identityService --project=$projectId --region=$($gcpStaging.region) --account=$($gcpStaging.account) --format='value(status.url)').Trim()
if ($LASTEXITCODE -ne 0 -or -not $base.StartsWith('https://')) { throw 'Staging identity URL unavailable.' }
$password=$null; $login=$null; $headers=$null
try {
    $password=(gcloud secrets versions access latest --secret="$infrastructurePrefix-keycloak-bootstrap-admin-password" --project=$projectId --account=$($gcpStaging.account) 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($password)) { throw 'Staging administrator secret unavailable.' }
    $login=Invoke-RestMethod -Method Post -Uri ($base+'/realms/master/protocol/openid-connect/token') -ContentType 'application/x-www-form-urlencoded' -Body @{client_id='admin-cli';grant_type='password';username='focusguard-admin';password=$password} -TimeoutSec 30
    if (-not $login.access_token) { throw 'Staging administrator authentication failed.' }
    $headers=@{Authorization='Bearer '+$login.access_token}
    $before=Invoke-RestMethod ($base+'/admin/realms/focusguard') -Headers $headers -TimeoutSec 30
    Set-FocusGuardNativeSessionPolicy -Base $base -Realm 'focusguard' -Headers $headers -VerifyOnly:$VerifyOnly
    $after=Invoke-RestMethod ($base+'/admin/realms/focusguard') -Headers $headers -TimeoutSec 30
    foreach ($property in @('ssoSessionIdleTimeout','ssoSessionMaxLifespan','accessTokenLifespan','verifyEmail','passwordPolicy')) {
        if ($before.$property -cne $after.$property) { throw 'Unrelated realm policy changed.' }
    }
    Write-Output 'PASS staging: browser session, access token, email and password policies preserved.'
} finally { $password=$null; $login=$null; $headers=$null }
