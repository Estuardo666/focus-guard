param([switch]$BffOnly,[switch]$NativeOnly,[switch]$LocalPostgres,[switch]$Phase3SyncOnly)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runtimeRoot=Join-Path $repoRoot 'artifacts/cloud-phase1/staging'
$runId=[Guid]::NewGuid().ToString('N')
$containerName=if ($LocalPostgres) { 'focusguard-keycloak-phase3-local-' + $runId } else { 'focusguard-keycloak-staging-local' }
$postgresContainerName='focusguard-phase3-postgres-' + $runId
$owned=(docker ps -aq --filter ('name=^/' + $containerName + '$') --filter 'label=focusguard.scope=cloud-phase1-local' | Out-String).Trim()
$ownedRunning=(docker ps -q --filter ('name=^/' + $containerName + '$') --filter 'label=focusguard.scope=cloud-phase1-local' | Out-String).Trim()
$portInUse=Get-NetTCPConnection -LocalPort 18443 -State Listen -ErrorAction SilentlyContinue
if ($portInUse -and -not $ownedRunning) { throw 'Fixture port occupied by another service; refusing to stop it.' }
$started=$false
$postgresStarted=$false
try {
Add-Type -AssemblyName System.Security
if ($Phase3SyncOnly -and -not $LocalPostgres) { throw '-Phase3SyncOnly requires an isolated local PostgreSQL fixture.' }
if ($LocalPostgres -and (-not $NativeOnly -or $BffOnly)) { throw '-LocalPostgres requires -NativeOnly without -BffOnly; it never runs the BFF fixture.' }
if ($LocalPostgres) {
    $postgresImage=(docker image ls --filter reference=postgres:17-alpine --format '{{.Repository}}:{{.Tag}}' | Select-Object -First 1)
    if (-not $postgresImage) { throw 'Local PostgreSQL image postgres:17-alpine is not available.' }
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start(); $postgresPort=$listener.LocalEndpoint.Port; $listener.Stop()
    $postgresPassword=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)).ToLowerInvariant()
    try {
        docker run --detach --name $postgresContainerName --label focusguard.scope=cloud-phase3-local-test --memory 768m --cpus 1 --publish ('127.0.0.1:' + $postgresPort + ':5432') --env 'POSTGRES_USER=focusguard' --env ('POSTGRES_PASSWORD=' + $postgresPassword) --env 'POSTGRES_DB=focusguard_keycloak' $postgresImage | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Isolated local PostgreSQL fixture failed to start.' }
        $postgresStarted=$true
        $postgresReady=$false
        for ($attempt=0; $attempt -lt 60; $attempt++) {
            docker exec $postgresContainerName pg_isready -U focusguard -d focusguard_keycloak *> $null
            if ($LASTEXITCODE -eq 0) { $postgresReady=$true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $postgresReady) { throw 'Isolated local PostgreSQL fixture did not become ready.' }
        $productDatabaseReady=$false
        for ($attempt=0; $attempt -lt 30; $attempt++) {
            $existingDatabase=docker exec $postgresContainerName psql -U focusguard -d focusguard_keycloak -tAc "SELECT 1 FROM pg_database WHERE datname='focusguard_product'" 2>$null
            if ($LASTEXITCODE -eq 0 -and (($existingDatabase -join '').Trim() -eq '1')) {
                $productDatabaseReady=$true
                break
            }
            docker exec $postgresContainerName createdb -U focusguard focusguard_product 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { $productDatabaseReady=$true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $productDatabaseReady) { throw 'Unable to create the isolated product test database after readiness retries.' }
    } catch {
        if ($postgresStarted) { docker rm --volumes --force $postgresContainerName | Out-Null; $postgresStarted=$false }
        throw
    }
    $env:KC_DB_URL='jdbc:postgresql://host.docker.internal:' + $postgresPort + '/focusguard_keycloak?sslmode=disable'
    $env:KC_DB_USERNAME='focusguard'
    $env:KC_DB_PASSWORD=$postgresPassword
    $env:FG_API_TEST_CONNECTION='Host=127.0.0.1;Port=' + $postgresPort + ';Database=focusguard_product;Username=focusguard;Password=' + $postgresPassword + ';SSL Mode=Disable;Timeout=15;Command Timeout=10'
    $env:FG_PHASE3_API_SYNC_TEST='1'
    if ($Phase3SyncOnly) {
        $env:FG_PHASE3_SYNC_ONLY='1'
        $env:ConnectionStrings__Product=$env:FG_API_TEST_CONNECTION
        dotnet run --project (Join-Path $repoRoot 'cloud/identity/Tests/FocusGuard.Identity.Tests.csproj') -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Automatic settings sync/PostgreSQL checks failed.' }
        return
    }
    $env:FG_PHASE3_HOST_IDENTITY_TEST='1'
} else {
    $protected=[IO.File]::ReadAllBytes((Join-Path $runtimeRoot 'neon-credentials.dpapi'))
    $clear=[Security.Cryptography.ProtectedData]::Unprotect($protected,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { $credentials=[Text.Encoding]::UTF8.GetString($clear) | ConvertFrom-Json } finally { [Array]::Clear($clear,0,$clear.Length) }
    if ($credentials.failed) { throw 'Database provisioning incomplete.' }
    $env:KC_DB_URL='jdbc:postgresql://' + $credentials.keycloak.host + '/focusguard_keycloak?sslmode=verify-full&sslrootcert=/etc/pki/tls/certs/ca-bundle.crt'
    $env:KC_DB_USERNAME=$credentials.keycloak.role
    $env:KC_DB_PASSWORD=$credentials.keycloak.password
    $env:FG_API_TEST_CONNECTION='Host=' + $credentials.product.host + ';Database=focusguard_product;Username=' + $credentials.product.role + ';Password=' + $credentials.product.password + ';SSL Mode=VerifyFull'
}
$env:KC_HOSTNAME='http://127.0.0.1:18443'
$env:KC_LOG_LEVEL='info'
$seedFile=Join-Path $runtimeRoot 'identity-fixture.dpapi'
if (Test-Path -LiteralPath $seedFile) {
    $seedBytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($seedFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { $seed=[Text.Encoding]::UTF8.GetString($seedBytes) | ConvertFrom-Json } finally { [Array]::Clear($seedBytes,0,$seedBytes.Length) }
} else {
    $seed=@{password=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24));bffSecret=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32));adminPassword=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))}
    $seedBytes=[Text.Encoding]::UTF8.GetBytes(($seed | ConvertTo-Json))
    try { [IO.File]::WriteAllBytes($seedFile,[Security.Cryptography.ProtectedData]::Protect($seedBytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)) } finally { [Array]::Clear($seedBytes,0,$seedBytes.Length) }
}
$env:FG_TEST_PASSWORD=$seed.password
$env:KC_BOOTSTRAP_ADMIN_USERNAME='focusguard-admin'
$env:KC_BOOTSTRAP_ADMIN_PASSWORD=$seed.adminPassword
$env:Identity__Authority='https://identity-fixture.example.invalid/realms/focusguard-local-test'
$env:ConnectionStrings__Product=$env:FG_API_TEST_CONNECTION
$fixtureRealm=Get-Content -LiteralPath (Join-Path $repoRoot 'cloud/identity/realm-template.json') -Raw | ConvertFrom-Json
$fixtureRealm.realm='focusguard-local-test'; $fixtureRealm.sslRequired='none'; $fixtureRealm.verifyEmail=$false; $fixtureRealm.requiredActions=@()
$fixtureRealm.registrationAllowed=$false
$fixtureBff=$fixtureRealm.clients | Where-Object clientId -eq 'focusguard-bff' | Select-Object -First 1
if (-not $fixtureBff) { throw 'Fixture BFF client is missing.' }
$fixtureBff | Add-Member -NotePropertyName secret -NotePropertyValue $seed.bffSecret
$apiAudienceMapper=@{
    name='focusguard-api-audience'; protocol='openid-connect'; protocolMapper='oidc-audience-mapper'; consentRequired=$false
    config=@{'included.custom.audience'='focusguard-api';'access.token.claim'='true';'id.token.claim'='false'}
}
foreach ($client in $fixtureRealm.clients) {
    $client.protocolMappers=@(@($client.protocolMappers | Where-Object name -ne $apiAudienceMapper.name) + $apiAudienceMapper)
}
$fixtureRealm | Add-Member -NotePropertyName users -NotePropertyValue @(
    @{
        username='phase1@example.invalid';email='phase1@example.invalid';enabled=$true;emailVerified=$true;firstName='Phase';lastName='Test';requiredActions=@()
        credentials=@(@{type='password';value=$env:FG_TEST_PASSWORD;temporary=$false})
    },
    @{
        username='phase1-unverified@example.invalid';email='phase1-unverified@example.invalid';enabled=$true;emailVerified=$false;firstName='Phase';lastName='Unverified';requiredActions=@()
        credentials=@(@{type='password';value=$env:FG_TEST_PASSWORD;temporary=$false})
    }
)
# Docker Desktop cannot reliably bind this repository's exFAT E: directory.
# Keep the temporary credential-bearing fixture on the user's NTFS volume.
$import=Join-Path $env:USERPROFILE '.codex/focusguard-staging-build/identity-import'
New-Item -ItemType Directory -Force $import | Out-Null
$fixtureFile=Join-Path $import 'focusguard-local-test-realm.json'
$fixtureRealm | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $fixtureFile -Encoding utf8
    if ($owned) {
        if (-not $ownedRunning) {
            docker rm $owned | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Unable to replace stopped owned fixture.' }
        } else {
            $started=$true
        }
    }
    if (-not $ownedRunning) {
        $imageId=(docker image ls --filter reference=focusguard-keycloak:26.8.0-staging --no-trunc --format '{{.ID}}' | Select-Object -First 1).Trim()
        if (-not $imageId) { throw 'Keycloak image not built.' }
        $mount='type=bind,source=' + $import + ',target=/opt/keycloak/data/import,readonly'
        docker run --detach --name $containerName --label focusguard.scope=cloud-phase1-local --memory 2g --cpus 1 --publish 127.0.0.1:18443:8080 --mount $mount --env KC_DB_URL --env KC_DB_USERNAME --env KC_DB_PASSWORD --env KC_HOSTNAME --env KC_LOG_LEVEL --env KC_BOOTSTRAP_ADMIN_USERNAME --env KC_BOOTSTRAP_ADMIN_PASSWORD $imageId start --optimized --import-realm | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Keycloak fixture start failed.' }; $started=$true
    }
    $deadline=[DateTime]::UtcNow.AddMinutes(8)
    while ([DateTime]::UtcNow -lt $deadline) {
        try { $discovery=Invoke-RestMethod 'http://127.0.0.1:18443/realms/focusguard-local-test/.well-known/openid-configuration' -TimeoutSec 8; break } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $discovery) { throw 'Identity fixture not ready.' }
    # Reconcile only our synthetic realm's audience mapper on repeat runs.
    $admin=Invoke-RestMethod 'http://127.0.0.1:18443/realms/master/protocol/openid-connect/token' -Method Post -Body @{client_id='admin-cli';grant_type='password';username=$env:KC_BOOTSTRAP_ADMIN_USERNAME;password=$seed.adminPassword} -TimeoutSec 20
    $adminHeaders=@{Authorization='Bearer ' + $admin.access_token}
    . (Join-Path $PSScriptRoot 'Native-SessionPolicy.ps1')
    Set-FocusGuardNativeSessionPolicy -Base 'http://127.0.0.1:18443' -Realm 'focusguard-local-test' -Headers $adminHeaders
    $nativeClients=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients?clientId=focusguard-native' -Headers $adminHeaders -TimeoutSec 20
    $nativeClient=$nativeClients | Select-Object -First 1
    $mapper=$nativeClient.protocolMappers | Where-Object name -eq 'focusguard-api-audience' | Select-Object -First 1
    if ($mapper) {
        $mapper.config=@{'included.custom.audience'='focusguard-api';'access.token.claim'='true';'id.token.claim'='false'}
        Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients/' + $nativeClient.id + '/protocol-mappers/models/' + $mapper.id) -Method Put -Headers $adminHeaders -ContentType application/json -Body ($mapper | ConvertTo-Json -Depth 8) -TimeoutSec 20 | Out-Null
    } elseif ($nativeClient) {
        Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients/' + $nativeClient.id + '/protocol-mappers/models') -Method Post -Headers $adminHeaders -ContentType application/json -Body ($apiAudienceMapper | ConvertTo-Json -Depth 8) -TimeoutSec 20 | Out-Null
    }
    $androidClients=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients?clientId=focusguard-android' -Headers $adminHeaders -TimeoutSec 20
    $androidClient=$androidClients | Select-Object -First 1
    # Realm import is create-only for an existing realm. Reconcile the test realm
    # from the reviewed template before asserting its public-client policy.
    if (-not $androidClient) {
        $androidTemplate=$fixtureRealm.clients | Where-Object clientId -eq 'focusguard-android' | Select-Object -First 1
        if (-not $androidTemplate) { throw 'Reviewed Android OIDC client template is missing.' }
        Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients' -Method Post -Headers $adminHeaders -ContentType application/json -Body ($androidTemplate | ConvertTo-Json -Depth 20) -TimeoutSec 20 | Out-Null
        $androidClients=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients?clientId=focusguard-android' -Headers $adminHeaders -TimeoutSec 20
        $androidClient=$androidClients | Select-Object -First 1
    }
    if (-not $androidClient) { throw 'Synthetic Android OIDC client could not be provisioned.' }
    $androidClient.publicClient=$true
    $androidClient.standardFlowEnabled=$true
    $androidClient.implicitFlowEnabled=$false
    $androidClient.directAccessGrantsEnabled=$false
    $androidClient.serviceAccountsEnabled=$false
    $androidClient.redirectUris=@('com.totemmassmedia.focusguard:/oauth2redirect')
    if (-not $androidClient.attributes) { $androidClient | Add-Member -NotePropertyName attributes -NotePropertyValue @{} -Force }
    $androidClient.attributes.'pkce.code.challenge.method'='S256'
    Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients/' + $androidClient.id) -Method Put -Headers $adminHeaders -ContentType application/json -Body ($androidClient | ConvertTo-Json -Depth 20) -TimeoutSec 20 | Out-Null
    $androidAudienceMapper=$androidClient.protocolMappers | Where-Object name -eq 'focusguard-api-audience' | Select-Object -First 1
    if (-not $androidAudienceMapper) {
        Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients/' + $androidClient.id + '/protocol-mappers/models') -Method Post -Headers $adminHeaders -ContentType application/json -Body ($apiAudienceMapper | ConvertTo-Json -Depth 8) -TimeoutSec 20 | Out-Null
    } else {
        $androidAudienceMapper.config=@{'included.custom.audience'='focusguard-api';'access.token.claim'='true';'id.token.claim'='false'}
        Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients/' + $androidClient.id + '/protocol-mappers/models/' + $androidAudienceMapper.id) -Method Put -Headers $adminHeaders -ContentType application/json -Body ($androidAudienceMapper | ConvertTo-Json -Depth 8) -TimeoutSec 20 | Out-Null
    }
    $androidClients=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients?clientId=focusguard-android' -Headers $adminHeaders -TimeoutSec 20
    $androidClient=$androidClients | Select-Object -First 1
    $androidAudienceMapper=$androidClient.protocolMappers | Where-Object name -eq 'focusguard-api-audience' | Select-Object -First 1
    if (-not $androidClient.publicClient -or -not $androidClient.standardFlowEnabled -or
        $androidClient.implicitFlowEnabled -or $androidClient.directAccessGrantsEnabled -or $androidClient.serviceAccountsEnabled -or
        @($androidClient.redirectUris).Count -ne 1 -or
        $androidClient.redirectUris[0] -cne 'com.totemmassmedia.focusguard:/oauth2redirect' -or
        $androidClient.attributes.'pkce.code.challenge.method' -cne 'S256' -or -not $androidAudienceMapper) {
        throw 'Android OIDC client settings are not restricted to the exact public PKCE redirect.'
    }
    Write-Output 'PASS Android client is public, PKCE S256, exact redirect, and has the API audience mapper.'
    $bffClients=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients?clientId=focusguard-bff' -Headers $adminHeaders -TimeoutSec 20
    $bffClient=$bffClients | Select-Object -First 1
    if (-not $bffClient) { throw 'Synthetic BFF client missing.' }
    $bffClient.redirectUris=@('https://focusguard.totemmassmedia.com/api/auth/callback')
    $bffClient.attributes.'post.logout.redirect.uris'='https://focusguard.totemmassmedia.com/'
    Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/clients/' + $bffClient.id) -Method Put -Headers $adminHeaders -ContentType application/json -Body ($bffClient | ConvertTo-Json -Depth 12) -TimeoutSec 20 | Out-Null
    $bffTestUsers=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/users?username=phase1-unverified%40example.invalid' -Headers $adminHeaders -TimeoutSec 20
    $bffTestUser=$bffTestUsers | Select-Object -First 1
    if (-not $bffTestUser) {
        $newBffUser=@{username='phase1-unverified@example.invalid';email='phase1-unverified@example.invalid';enabled=$true;emailVerified=$false;firstName='Phase';lastName='Unverified';requiredActions=@()} | ConvertTo-Json -Depth 8
        Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/users' -Method Post -Headers $adminHeaders -ContentType application/json -Body $newBffUser -TimeoutSec 20 | Out-Null
        $bffTestUsers=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/users?username=phase1-unverified%40example.invalid' -Headers $adminHeaders -TimeoutSec 20
        $bffTestUser=$bffTestUsers | Select-Object -First 1
    }
    if (-not $bffTestUser) { throw 'Synthetic unverified BFF user missing.' }
    $bffTestUser.enabled=$true; $bffTestUser.emailVerified=$false; $bffTestUser.requiredActions=@()
    Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/users/' + $bffTestUser.id) -Method Put -Headers $adminHeaders -ContentType application/json -Body ($bffTestUser | ConvertTo-Json -Depth 10) -TimeoutSec 20 | Out-Null
    $bffTestCredential=@{type='password';value=$seed.password;temporary=$false} | ConvertTo-Json -Depth 4
    Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/users/' + $bffTestUser.id + '/reset-password') -Method Put -Headers $adminHeaders -ContentType application/json -Body $bffTestCredential -TimeoutSec 20 | Out-Null
    $otherAccountUsers=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/users?username=phase1-other%40example.invalid' -Headers $adminHeaders -TimeoutSec 20
    $otherAccountUser=$otherAccountUsers | Select-Object -First 1
    if (-not $otherAccountUser) {
        $newOtherAccountUser=@{username='phase1-other@example.invalid';email='phase1-other@example.invalid';enabled=$true;emailVerified=$true;firstName='Other';lastName='Account';requiredActions=@()} | ConvertTo-Json -Depth 8
        Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/users' -Method Post -Headers $adminHeaders -ContentType application/json -Body $newOtherAccountUser -TimeoutSec 20 | Out-Null
        $otherAccountUsers=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/users?username=phase1-other%40example.invalid' -Headers $adminHeaders -TimeoutSec 20
        $otherAccountUser=$otherAccountUsers | Select-Object -First 1
    }
    if (-not $otherAccountUser) { throw 'Synthetic second verified account missing.' }
    $otherAccountUser.enabled=$true; $otherAccountUser.emailVerified=$true; $otherAccountUser.requiredActions=@()
    Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/users/' + $otherAccountUser.id) -Method Put -Headers $adminHeaders -ContentType application/json -Body ($otherAccountUser | ConvertTo-Json -Depth 10) -TimeoutSec 20 | Out-Null
    Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/users/' + $otherAccountUser.id + '/reset-password') -Method Put -Headers $adminHeaders -ContentType application/json -Body $bffTestCredential -TimeoutSec 20 | Out-Null
    # The previous acceptance run intentionally resets this synthetic password.
    # Restore only the fixture user before repeating the browser login tests.
    $mainTestUsers=Invoke-RestMethod 'http://127.0.0.1:18443/admin/realms/focusguard-local-test/users?username=phase1%40example.invalid' -Headers $adminHeaders -TimeoutSec 20
    $mainTestUser=$mainTestUsers | Where-Object username -ceq 'phase1@example.invalid' | Select-Object -First 1
    if (-not $mainTestUser) { throw 'Synthetic verified fixture user missing.' }
    Invoke-RestMethod ('http://127.0.0.1:18443/admin/realms/focusguard-local-test/users/' + $mainTestUser.id + '/reset-password') -Method Put -Headers $adminHeaders -ContentType application/json -Body $bffTestCredential -TimeoutSec 20 | Out-Null
    $admin=$null; $adminHeaders=$null
    if (-not $BffOnly) {
        dotnet run --project (Join-Path $repoRoot 'cloud/identity/Tests/FocusGuard.Identity.Tests.csproj') -c Release
        if ($LASTEXITCODE -ne 0) {
            if ($LocalPostgres) { throw 'Identity/API checks against isolated local PostgreSQL failed.' }
            else { throw 'Identity/API checks against Neon failed.' }
        }
    }
    if (-not $NativeOnly) {
        $env:NODE_ENV='test'
        $env:PORTAL_ORIGIN='https://focusguard.totemmassmedia.com'
        $env:OIDC_ISSUER='http://127.0.0.1:18443/realms/focusguard-local-test'
        $env:OIDC_CLIENT_SECRET=$seed.bffSecret
        $env:DATABASE_URL=$credentials.bff.url
        $env:FOCUSGUARD_API_ORIGIN='https://api.example.invalid'
        $bffKeyBytes=New-Object byte[] 32
        $bffKeyGenerator=[Security.Cryptography.RandomNumberGenerator]::Create()
        try { $bffKeyGenerator.GetBytes($bffKeyBytes); $env:BFF_ENCRYPTION_KEY=[Convert]::ToBase64String($bffKeyBytes) }
        finally { [Array]::Clear($bffKeyBytes,0,$bffKeyBytes.Length); $bffKeyGenerator.Dispose() }
        $env:FG_BFF_TEST_PASSWORD=$seed.password
        node (Join-Path $repoRoot 'cloud/portal/tests/bff.integration.mjs')
        if ($LASTEXITCODE -ne 0) { throw 'Portal BFF integration checks against Keycloak and Neon failed.' }
    }
    docker restart $containerName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Identity restart failed.' }
    $discovery=$null; $deadline=[DateTime]::UtcNow.AddMinutes(3)
    while ([DateTime]::UtcNow -lt $deadline) {
        try { $discovery=Invoke-RestMethod 'http://127.0.0.1:18443/realms/focusguard-local-test/.well-known/openid-configuration' -TimeoutSec 8; break } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $discovery) { throw 'Identity fixture not restored after restart.' }
    if ($LocalPostgres) { Write-Output 'PASS realm/signing configuration survives container restart with isolated local PostgreSQL persistence.' }
    else { Write-Output 'PASS realm/signing configuration survives real container restart with Neon persistence.' }
} finally {
    if ($started) {
        if ($LocalPostgres) { docker rm --force $containerName | Out-Null }
        else { docker stop $containerName | Out-Null }
    }
    if ($postgresStarted) { docker rm --volumes --force $postgresContainerName | Out-Null }
    if ($fixtureFile) { Remove-Item -LiteralPath $fixtureFile -ErrorAction SilentlyContinue }
    foreach ($name in @('KC_DB_URL','KC_DB_USERNAME','KC_DB_PASSWORD','KC_HOSTNAME','KC_LOG_LEVEL','FG_TEST_PASSWORD','KC_BOOTSTRAP_ADMIN_USERNAME','KC_BOOTSTRAP_ADMIN_PASSWORD','Identity__Authority','FG_API_TEST_CONNECTION','ConnectionStrings__Product','FG_PHASE3_API_SYNC_TEST','FG_PHASE3_SYNC_ONLY','FG_PHASE3_HOST_IDENTITY_TEST','NODE_ENV','PORTAL_ORIGIN','OIDC_ISSUER','OIDC_CLIENT_SECRET','DATABASE_URL','FOCUSGUARD_API_ORIGIN','BFF_ENCRYPTION_KEY','FG_BFF_TEST_PASSWORD')) { Remove-Item -LiteralPath ('Env:'+$name) -ErrorAction SilentlyContinue }
}
