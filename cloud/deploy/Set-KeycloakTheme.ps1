param([string]$Theme='focusguard')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Gcp-Staging.ps1')
$base=(gcloud run services describe $gcpStaging.identityService --project=$projectId --region=$($gcpStaging.region) --account=$($gcpStaging.account) --format='value(status.url)').Trim()
if ($LASTEXITCODE -ne 0 -or -not $base.StartsWith('https://')) { throw 'Staging identity service URL unavailable.' }
$password=(gcloud secrets versions access latest --secret="$infrastructurePrefix-keycloak-bootstrap-admin-password" --project=$projectId 2>$null | Out-String).Trim()
if ([string]::IsNullOrWhiteSpace($password)) { throw 'Keycloak admin secret unavailable.' }
try {
    $login=Invoke-RestMethod -Method Post -Uri ($base+'/realms/master/protocol/openid-connect/token') -ContentType 'application/x-www-form-urlencoded' `
        -Body @{client_id='admin-cli';grant_type='password';username='focusguard-admin';password=$password} -TimeoutSec 30
} finally { $password=$null }
$headers=@{Authorization='Bearer '+$login.access_token}
$realm=Invoke-RestMethod -Method Get -Uri ($base+'/admin/realms/focusguard') -Headers $headers -TimeoutSec 30
$update=@{loginTheme=$Theme;accountTheme=$Theme} | ConvertTo-Json -Compress
Invoke-RestMethod -Method Put -Uri ($base+'/admin/realms/focusguard') -Headers $headers -ContentType 'application/json' -Body $update -TimeoutSec 30 | Out-Null
$after=Invoke-RestMethod -Method Get -Uri ($base+'/admin/realms/focusguard') -Headers $headers -TimeoutSec 30
Write-Output "KEYCLOAK_THEME_PREVIOUS=login:$($realm.loginTheme);account:$($realm.accountTheme)"
Write-Output "KEYCLOAK_THEME_ACTIVE=login:$($after.loginTheme);account:$($after.accountTheme)"
