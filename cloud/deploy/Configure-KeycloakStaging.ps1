param([switch]$TestSmtp,[switch]$EnableRegistration)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Gcp-Staging.ps1')
$base=(gcloud run services describe $gcpStaging.identityService --project=$projectId --region=$($gcpStaging.region) --account=$($gcpStaging.account) --format='value(status.url)').Trim()
if ($LASTEXITCODE -ne 0 -or -not $base.StartsWith('https://')) { throw 'Staging identity service URL unavailable.' }
$stagePath=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts/cloud-phase1/staging'
Add-Type -AssemblyName System.Security

function Read-LocalSecret([string]$Path) {
    $bytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($Path),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { return [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json }
    finally { [Array]::Clear($bytes,0,$bytes.Length) }
}

function Get-GoogleSecret([string]$Name) {
    $value=(gcloud secrets versions access latest --secret=$Name --project=$projectId 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) { throw "Required staging secret unavailable: $Name" }
    return $value
}

function Invoke-Admin([string]$Method,[string]$Path,[hashtable]$Headers,$Body=$null) {
    $parameters=@{Method=$Method;Uri=($base+$Path);Headers=$Headers;TimeoutSec=30}
    if ($null -ne $Body) { $parameters.ContentType='application/json'; $parameters.Body=$Body | ConvertTo-Json -Depth 30 -Compress }
    return Invoke-RestMethod @parameters
}

$fixture=Read-LocalSecret (Join-Path $stagePath 'identity-fixture.dpapi')
$adminPassword=Get-GoogleSecret "$infrastructurePrefix-keycloak-bootstrap-admin-password"
$bffSecret=Get-GoogleSecret "$infrastructurePrefix-bff-client-secret"
$resendKey=Get-GoogleSecret "$infrastructurePrefix-resend-smtp-api-key"
$adminAccessToken=$null
$headers=$null
$masterAdmin=$null
$realm=$null
$bffClient=$null
$smtpTestUser=$null
$adminLoginUsername=$null
$adminOriginalEmail=$null
$adminOriginalEmailVerified=$false

try {
    if ([string]$fixture.adminPassword -cne $adminPassword -or [string]$fixture.bffSecret -cne $bffSecret) {
        throw 'Local and Secret Manager staging credentials do not match.'
    }

    # The shared Neon master realm was initially bootstrapped by the local fixture. Use that admin only to
    # provision the configured staging service identity, then remove the synthetic admin account.
    try {
        $adminLogin=Invoke-RestMethod -Method Post -Uri ($base+'/realms/master/protocol/openid-connect/token') `
            -ContentType 'application/x-www-form-urlencoded' `
            -Body @{client_id='admin-cli';grant_type='password';username='focusguard-fixture-admin';password=$adminPassword} -TimeoutSec 30
        $adminLoginUsername='focusguard-fixture-admin'
    } catch {
        $adminLogin=Invoke-RestMethod -Method Post -Uri ($base+'/realms/master/protocol/openid-connect/token') `
            -ContentType 'application/x-www-form-urlencoded' `
            -Body @{client_id='admin-cli';grant_type='password';username='focusguard-admin';password=$adminPassword} -TimeoutSec 30
        $adminLoginUsername='focusguard-admin'
    }
    $adminAccessToken=$adminLogin.access_token
    $headers=@{Authorization='Bearer '+$adminAccessToken}
    $serviceAdmins=Invoke-Admin Get '/admin/realms/master/users?username=focusguard-admin' $headers
    $serviceAdmin=$serviceAdmins | Select-Object -First 1
    if (-not $serviceAdmin) {
        $newAdmin=@{username='focusguard-admin';enabled=$true;firstName='FocusGuard';lastName='Administrator';requiredActions=@()}
        Invoke-Admin Post '/admin/realms/master/users' $headers $newAdmin | Out-Null
        $serviceAdmins=Invoke-Admin Get '/admin/realms/master/users?username=focusguard-admin' $headers
        $serviceAdmin=$serviceAdmins | Select-Object -First 1
    }
    if (-not $serviceAdmin) { throw 'Could not provision the Keycloak staging service administrator.' }
    $serviceAdmin.enabled=$true
    $serviceAdmin | Add-Member -NotePropertyName firstName -NotePropertyValue 'FocusGuard' -Force
    $serviceAdmin | Add-Member -NotePropertyName lastName -NotePropertyValue 'Administrator' -Force
    $serviceAdmin | Add-Member -NotePropertyName requiredActions -NotePropertyValue @() -Force
    Invoke-Admin Put ('/admin/realms/master/users/'+$serviceAdmin.id) $headers $serviceAdmin | Out-Null
    if ($adminLoginUsername -eq 'focusguard-fixture-admin') {
        $adminCredential=@{type='password';value=$adminPassword;temporary=$false}
        Invoke-Admin Put ('/admin/realms/master/users/'+$serviceAdmin.id+'/reset-password') $headers $adminCredential | Out-Null
    }
    $adminRole=Invoke-Admin Get '/admin/realms/master/roles/admin' $headers
    $adminMappings=Invoke-Admin Get ('/admin/realms/master/users/'+$serviceAdmin.id+'/role-mappings/realm') $headers
    if (-not (@($adminMappings | Where-Object name -eq 'admin').Count)) {
        $roleBody=ConvertTo-Json -InputObject @($adminRole) -Depth 12 -Compress
        Invoke-RestMethod -Method Post -Uri ($base+'/admin/realms/master/users/'+$serviceAdmin.id+'/role-mappings/realm') `
            -Headers $headers -ContentType 'application/json' -Body $roleBody -TimeoutSec 30 | Out-Null
    }
    $serviceLogin=Invoke-RestMethod -Method Post -Uri ($base+'/realms/master/protocol/openid-connect/token') `
        -ContentType 'application/x-www-form-urlencoded' `
        -Body @{client_id='admin-cli';grant_type='password';username='focusguard-admin';password=$adminPassword} -TimeoutSec 30
    if (-not $serviceLogin.access_token) { throw 'Staging service administrator could not authenticate.' }
    $adminAccessToken=$serviceLogin.access_token
    $headers=@{Authorization='Bearer '+$adminAccessToken}
    $masterAdmin=$serviceAdmin
    . (Join-Path $PSScriptRoot 'Native-SessionPolicy.ps1')
    Set-FocusGuardNativeSessionPolicy -Base $base -Realm 'focusguard' -Headers $headers
    $fixtureAdmins=Invoke-Admin Get '/admin/realms/master/users?username=focusguard-fixture-admin' $headers
    foreach ($fixtureAdmin in $fixtureAdmins) { Invoke-Admin Delete ('/admin/realms/master/users/'+$fixtureAdmin.id) $headers | Out-Null }

    # Reconcile the BFF confidential client with its separately stored Secret Manager value.
    $bffClients=Invoke-Admin Get '/admin/realms/focusguard/clients?clientId=focusguard-bff' $headers
    $bffClient=$bffClients | Select-Object -First 1
    if (-not $bffClient) { throw 'Production BFF client was not found.' }
    $bffClient | Add-Member -NotePropertyName secret -NotePropertyValue $bffSecret -Force
    $bffClient.redirectUris=@('https://focusguard.totemmassmedia.com/api/auth/callback')
    $bffClient.attributes.'post.logout.redirect.uris'='https://focusguard.totemmassmedia.com/'
    Invoke-Admin Put ('/admin/realms/focusguard/clients/'+$bffClient.id) $headers $bffClient | Out-Null

    # Android is a separate public native client. Keep its verified app-scheme callback exact,
    # and never copy the BFF secret or enable password/implicit grants.
    $androidClientId='focusguard-android'
    $androidRedirect='com.totemmassmedia.focusguard:/oauth2redirect'
    $androidClients=Invoke-Admin Get ('/admin/realms/focusguard/clients?clientId='+$androidClientId) $headers
    $androidClient=$androidClients | Select-Object -First 1
    if (-not $androidClient) {
        $androidClient=@{
            clientId=$androidClientId;name='FocusGuard Android';protocol='openid-connect';enabled=$true
            publicClient=$true;standardFlowEnabled=$true;implicitFlowEnabled=$false;directAccessGrantsEnabled=$false
            serviceAccountsEnabled=$false;redirectUris=@($androidRedirect);webOrigins=@()
            attributes=@{'pkce.code.challenge.method'='S256';'post.logout.redirect.uris'=$androidRedirect}
            protocolMappers=@();defaultClientScopes=@('profile','email');optionalClientScopes=@()
        }
        Invoke-Admin Post '/admin/realms/focusguard/clients' $headers $androidClient | Out-Null
        $androidClients=Invoke-Admin Get ('/admin/realms/focusguard/clients?clientId='+$androidClientId) $headers
        $androidClient=$androidClients | Select-Object -First 1
    }
    if (-not $androidClient) { throw 'Android public client could not be provisioned.' }
    $androidClient | Add-Member -NotePropertyName name -NotePropertyValue 'FocusGuard Android' -Force
    $androidClient | Add-Member -NotePropertyName enabled -NotePropertyValue $true -Force
    $androidClient | Add-Member -NotePropertyName publicClient -NotePropertyValue $true -Force
    $androidClient | Add-Member -NotePropertyName standardFlowEnabled -NotePropertyValue $true -Force
    $androidClient | Add-Member -NotePropertyName implicitFlowEnabled -NotePropertyValue $false -Force
    $androidClient | Add-Member -NotePropertyName directAccessGrantsEnabled -NotePropertyValue $false -Force
    $androidClient | Add-Member -NotePropertyName serviceAccountsEnabled -NotePropertyValue $false -Force
    $androidClient | Add-Member -NotePropertyName redirectUris -NotePropertyValue @($androidRedirect) -Force
    $androidClient | Add-Member -NotePropertyName webOrigins -NotePropertyValue @() -Force
    $androidAttributes=@{}
    if ($androidClient.attributes) { foreach ($entry in $androidClient.attributes.PSObject.Properties) { $androidAttributes[$entry.Name]=$entry.Value } }
    $androidAttributes['pkce.code.challenge.method']='S256'
    $androidAttributes['post.logout.redirect.uris']=$androidRedirect
    $androidClient | Add-Member -NotePropertyName attributes -NotePropertyValue $androidAttributes -Force
    $androidMappers=@($androidClient.protocolMappers | Where-Object name -ne 'focusguard-api-audience')
    $androidMappers+=@{name='focusguard-api-audience';protocol='openid-connect';protocolMapper='oidc-audience-mapper';consentRequired=$false;config=@{ 'included.custom.audience'='focusguard-api';'access.token.claim'='true';'id.token.claim'='false' }}
    $androidClient | Add-Member -NotePropertyName protocolMappers -NotePropertyValue $androidMappers -Force
    $androidClient | Add-Member -NotePropertyName defaultClientScopes -NotePropertyValue @('profile','email') -Force
    $androidClient | Add-Member -NotePropertyName optionalClientScopes -NotePropertyValue @() -Force
    Invoke-Admin Put ('/admin/realms/focusguard/clients/'+$androidClient.id) $headers $androidClient | Out-Null
    $androidVerified=Invoke-Admin Get ('/admin/realms/focusguard/clients?clientId='+$androidClientId) $headers | Select-Object -First 1
    $androidMapper=$androidVerified.protocolMappers | Where-Object name -eq 'focusguard-api-audience' | Select-Object -First 1
    if (-not $androidVerified.publicClient -or -not $androidVerified.standardFlowEnabled -or
        $androidVerified.implicitFlowEnabled -or $androidVerified.directAccessGrantsEnabled -or
        $androidVerified.serviceAccountsEnabled -or @($androidVerified.redirectUris).Count -ne 1 -or
        $androidVerified.redirectUris[0] -cne $androidRedirect -or
        $androidVerified.attributes.'pkce.code.challenge.method' -cne 'S256' -or -not $androidMapper) {
        throw 'Keycloak Android client reconciliation did not persist the expected public PKCE configuration.'
    }
    Write-Output 'PASS Android client is public, exact-redirect, PKCE S256, and has the API audience mapper.'

    # Password creation must remain available when importing a restricted list
    # of required actions. Email verification alone is not account activation.
    $requiredActions=Invoke-Admin Get '/admin/realms/focusguard/authentication/required-actions' $headers
    if (-not (@($requiredActions | Where-Object alias -eq 'UPDATE_PASSWORD').Count)) {
        Invoke-Admin Post '/admin/realms/focusguard/authentication/register-required-action' $headers @{providerId='UPDATE_PASSWORD';name='Update Password'} | Out-Null
    }
    Invoke-Admin Put '/admin/realms/focusguard/authentication/required-actions/UPDATE_PASSWORD' $headers @{
        alias='UPDATE_PASSWORD';name='Update Password';providerId='UPDATE_PASSWORD';enabled=$true;defaultAction=$true;priority=30;config=@{}
    } | Out-Null
    # A password recovery email must not also remove/reset a second factor.
    # MFA recovery is a separate owner-present flow, not email-only recovery.
    $resetExecutions=Invoke-Admin Get '/admin/realms/focusguard/authentication/flows/reset%20credentials/executions' $headers
    $resetOtpBranch=$resetExecutions | Where-Object displayName -eq 'Reset - Conditional OTP'
    if ($resetOtpBranch) {
        $resetOtpBranch.requirement='DISABLED'
        Invoke-Admin Put '/admin/realms/focusguard/authentication/flows/reset%20credentials/executions' $headers $resetOtpBranch | Out-Null
    }

    # Store the restricted Resend SMTP credential in Keycloak's realm config without enabling registration yet.
    $realm=Invoke-Admin Get '/admin/realms/focusguard' $headers
    $smtp=@{
        host='smtp.resend.com';port='465';from='noreply@focusguard.totemmassmedia.com';fromDisplayName='FocusGuard'
        auth='true';user='resend';password=$resendKey;ssl='true';starttls='false'
    }
    $realm | Add-Member -NotePropertyName smtpServer -NotePropertyValue $smtp -Force
    $realm.passwordPolicy='length(15) and notUsername and notEmail'
    if ($EnableRegistration) {
        $discovery=Invoke-RestMethod 'https://auth.focusguard.totemmassmedia.com/realms/focusguard/.well-known/openid-configuration' -TimeoutSec 45
        if ($discovery.issuer -cne 'https://auth.focusguard.totemmassmedia.com/realms/focusguard') { throw 'Public identity issuer mismatch.' }
        $readiness=Invoke-WebRequest 'https://api.focusguard.totemmassmedia.com/health/ready' -TimeoutSec 45 -SkipHttpErrorCheck
        if ($readiness.StatusCode -ne 200) { throw 'Public API not ready.' }
    }
    $realm.registrationAllowed=[bool]$EnableRegistration
    Invoke-Admin Put '/admin/realms/focusguard' $headers $realm | Out-Null

    if ($TestSmtp) {
        # Use the owner's explicitly designated test mailbox only for Keycloak's SMTP connection test.
        $smtpTestUser=$masterAdmin
        $adminOriginalEmail=$smtpTestUser.email
        $adminOriginalEmailVerified=[bool]$smtpTestUser.emailVerified
        $smtpTestUser | Add-Member -NotePropertyName email -NotePropertyValue 'estuarlito@gmail.com' -Force
        $smtpTestUser | Add-Member -NotePropertyName emailVerified -NotePropertyValue $true -Force
        Invoke-Admin Put ('/admin/realms/master/users/'+$smtpTestUser.id) $headers $smtpTestUser | Out-Null
        try {
        $probe=Invoke-WebRequest -Method Post -Uri ($base+'/admin/realms/focusguard/testSMTPConnection') `
            -Headers $headers -ContentType 'application/json' -Body ($smtp | ConvertTo-Json -Compress) `
            -TimeoutSec 30 -SkipHttpErrorCheck
        if ([int]$probe.StatusCode -ne 204) { throw "SMTP connection test failed with HTTP $([int]$probe.StatusCode)." }
        Write-Output 'PASS Keycloak SMTP connection test returned 204.'
        } finally {
        $smtpTestUser.email=$adminOriginalEmail
        $smtpTestUser.emailVerified=$adminOriginalEmailVerified
        Invoke-Admin Put ('/admin/realms/master/users/'+$smtpTestUser.id) $headers $smtpTestUser | Out-Null
        }
    } else {
        Write-Output "SMTP_CONFIGURED_TEST_NOT_SENT; REGISTRATION_ENABLED=$([bool]$EnableRegistration)"
    }

    Write-Output 'PASS Keycloak staging administrator matches the configured Secret Manager credential.'
} finally {
    if ($null -ne $adminAccessToken -and $null -ne $smtpTestUser -and $smtpTestUser.email -eq 'estuarlito@gmail.com') {
        $smtpTestUser.email=$adminOriginalEmail; $smtpTestUser.emailVerified=$adminOriginalEmailVerified
        try { Invoke-Admin Put ('/admin/realms/master/users/'+$smtpTestUser.id) $headers $smtpTestUser | Out-Null } catch {}
    }
    $adminAccessToken=$null;$headers=$null;$fixture=$null;$adminPassword=$null;$bffSecret=$null;$resendKey=$null
    $masterAdmin=$null;$realm=$null;$bffClient=$null;$smtpTestUser=$null;$smtp=$null
}
