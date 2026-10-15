param([switch]$VerifyOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Gcp-Staging.ps1')
$base=(gcloud run services describe $gcpStaging.identityService --project=$projectId --region=$($gcpStaging.region) --account=$($gcpStaging.account) --format='value(status.url)').Trim()
if ($LASTEXITCODE -ne 0 -or -not $base.StartsWith('https://')) { throw 'Staging identity service URL unavailable.' }
$issuer='https://auth.focusguard.totemmassmedia.com/realms/focusguard'
$clientId='focusguard-android'
$redirect='com.totemmassmedia.focusguard:/oauth2redirect'
$discovery=Invoke-RestMethod ($issuer+'/.well-known/openid-configuration') -TimeoutSec 30
if ($discovery.issuer -cne $issuer -or $discovery.authorization_endpoint -notlike 'https://auth.focusguard.totemmassmedia.com/*' -or
    $discovery.token_endpoint -notlike 'https://auth.focusguard.totemmassmedia.com/*') { throw 'Staging OIDC issuer/endpoints mismatch.' }

$headers=$null
$adminAccessToken=$null
$adminPassword=$null
try {
    $adminSecretName="$infrastructurePrefix-keycloak-bootstrap-admin-password"
    $adminPassword=(gcloud secrets versions access latest --secret=$adminSecretName --project=$projectId --account=$($gcpStaging.account) 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($adminPassword)) { throw 'Staging Keycloak administrator secret unavailable.' }
    $adminLogin=Invoke-RestMethod -Method Post -Uri ($base+'/realms/master/protocol/openid-connect/token') `
        -ContentType 'application/x-www-form-urlencoded' `
        -Body @{client_id='admin-cli';grant_type='password';username='focusguard-admin';password=$adminPassword} -TimeoutSec 30
    $adminAccessToken=$adminLogin.access_token
    if ([string]::IsNullOrWhiteSpace($adminAccessToken)) { throw 'Staging Keycloak administrator authentication failed.' }
    $headers=@{Authorization='Bearer '+$adminAccessToken}
    $clientList=Invoke-RestMethod ($base+'/admin/realms/focusguard/clients?clientId='+$clientId) -Headers $headers -TimeoutSec 30
    $client=$clientList | Select-Object -First 1
    if ($VerifyOnly) {
        if (-not $client) { throw 'Android public client is not configured in staging.' }
    } elseif (-not $client) {
        $client=@{
            clientId=$clientId;name='FocusGuard Android';protocol='openid-connect';enabled=$true
            publicClient=$true;standardFlowEnabled=$true;implicitFlowEnabled=$false;directAccessGrantsEnabled=$false
            serviceAccountsEnabled=$false;redirectUris=@($redirect);webOrigins=@()
            attributes=@{'pkce.code.challenge.method'='S256';'post.logout.redirect.uris'=$redirect}
            defaultClientScopes=@('profile','email');optionalClientScopes=@()
        }
        Invoke-RestMethod -Method Post -Uri ($base+'/admin/realms/focusguard/clients') -Headers $headers `
            -ContentType 'application/json' -Body ($client | ConvertTo-Json -Depth 20 -Compress) -TimeoutSec 30 | Out-Null
        $clientList=Invoke-RestMethod ($base+'/admin/realms/focusguard/clients?clientId='+$clientId) -Headers $headers -TimeoutSec 30
        $client=$clientList | Select-Object -First 1
    }
    if (-not $client) { throw 'Android public client could not be read after provisioning.' }

    if (-not $VerifyOnly) {
        $client | Add-Member -NotePropertyName name -NotePropertyValue 'FocusGuard Android' -Force
        $client | Add-Member -NotePropertyName enabled -NotePropertyValue $true -Force
        $client | Add-Member -NotePropertyName publicClient -NotePropertyValue $true -Force
        $client | Add-Member -NotePropertyName standardFlowEnabled -NotePropertyValue $true -Force
        $client | Add-Member -NotePropertyName implicitFlowEnabled -NotePropertyValue $false -Force
        $client | Add-Member -NotePropertyName directAccessGrantsEnabled -NotePropertyValue $false -Force
        $client | Add-Member -NotePropertyName serviceAccountsEnabled -NotePropertyValue $false -Force
        $client | Add-Member -NotePropertyName redirectUris -NotePropertyValue @($redirect) -Force
        $client | Add-Member -NotePropertyName webOrigins -NotePropertyValue @() -Force
        $attributes=@{}
        if ($client.attributes) {
            if ($client.attributes -is [System.Collections.IDictionary]) {
                foreach ($key in $client.attributes.Keys) { $attributes[[string]$key]=$client.attributes[$key] }
            } else {
                foreach ($entry in $client.attributes.PSObject.Properties) { $attributes[$entry.Name]=$entry.Value }
            }
        }
        $attributes['pkce.code.challenge.method']='S256'
        $attributes['post.logout.redirect.uris']=$redirect
        $client | Add-Member -NotePropertyName attributes -NotePropertyValue $attributes -Force
        $client | Add-Member -NotePropertyName defaultClientScopes -NotePropertyValue @('profile','email') -Force
        $client | Add-Member -NotePropertyName optionalClientScopes -NotePropertyValue @() -Force
        if ($client.PSObject.Properties['protocolMappers']) { $client.PSObject.Properties.Remove('protocolMappers') }
        Invoke-RestMethod -Method Put -Uri ($base+'/admin/realms/focusguard/clients/'+$client.id) -Headers $headers `
            -ContentType 'application/json' -Body ($client | ConvertTo-Json -Depth 24 -Compress) -TimeoutSec 30 | Out-Null
        $clientList=Invoke-RestMethod ($base+'/admin/realms/focusguard/clients?clientId='+$clientId) -Headers $headers -TimeoutSec 30
        $client=$clientList | Select-Object -First 1
    }

    $mapperPath='/admin/realms/focusguard/clients/'+$client.id+'/protocol-mappers/models'
    $mapperList=Invoke-RestMethod ($base+$mapperPath) -Headers $headers -TimeoutSec 30
    $mapper=$mapperList | Where-Object name -eq 'focusguard-api-audience' | Select-Object -First 1
    if (-not $VerifyOnly) {
        $desiredMapper=@{name='focusguard-api-audience';protocol='openid-connect';protocolMapper='oidc-audience-mapper';
            consentRequired=$false;config=@{'included.custom.audience'='focusguard-api';'access.token.claim'='true';'id.token.claim'='false'}}
        if ($mapper) {
            $desiredMapper.id=$mapper.id
            Invoke-RestMethod -Method Put -Uri ($base+$mapperPath+'/'+$mapper.id) -Headers $headers `
                -ContentType 'application/json' -Body ($desiredMapper | ConvertTo-Json -Depth 10 -Compress) -TimeoutSec 30 | Out-Null
        } else {
            Invoke-RestMethod -Method Post -Uri ($base+$mapperPath) -Headers $headers `
                -ContentType 'application/json' -Body ($desiredMapper | ConvertTo-Json -Depth 10 -Compress) -TimeoutSec 30 | Out-Null
        }
        $mapperList=Invoke-RestMethod ($base+$mapperPath) -Headers $headers -TimeoutSec 30
        $mapper=$mapperList | Where-Object name -eq 'focusguard-api-audience' | Select-Object -First 1
    }
    if (-not $client.enabled -or -not $client.publicClient -or -not $client.standardFlowEnabled -or
        $client.implicitFlowEnabled -or $client.directAccessGrantsEnabled -or $client.serviceAccountsEnabled -or
        @($client.redirectUris).Count -ne 1 -or $client.redirectUris[0] -cne $redirect -or
        $client.attributes.'pkce.code.challenge.method' -cne 'S256' -or -not $mapper -or
        $mapper.config.'included.custom.audience' -cne 'focusguard-api') {
        throw 'Android OIDC client did not match the exact public PKCE policy.'
    }
    [PSCustomObject]@{Issuer=$discovery.issuer;ClientId=$clientId;RedirectUri=$redirect;PublicClient=$client.publicClient;
        StandardFlow=$client.standardFlowEnabled;Pkce=$client.attributes.'pkce.code.challenge.method';ApiAudience=$mapper.config.'included.custom.audience'} | ConvertTo-Json -Compress
} finally {
    $headers=$null
    $adminAccessToken=$null
    $adminPassword=$null
}
