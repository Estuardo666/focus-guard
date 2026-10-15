param(
    [string]$KeycloakHome,
    [string]$JavaHome
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $KeycloakHome) { $KeycloakHome = Join-Path $repoRoot 'artifacts/cloud-phase1/keycloak-26.8.0' }
if (-not $JavaHome) { $JavaHome = Join-Path $repoRoot 'artifacts/cloud-phase0/toolchains/jdk21/jdk-21.0.12.1+1' }
if (-not (Test-Path -LiteralPath (Join-Path $KeycloakHome 'bin/kc.bat'))) { throw 'Keycloak portable installation missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $JavaHome 'bin/java.exe'))) { throw 'Portable JDK missing.' }
if (Get-NetTCPConnection -LocalPort 18443 -State Listen -ErrorAction SilentlyContinue) { throw 'Test port already in use; will not stop foreign process.' }
$previousEnvironment = @{}
foreach ($variable in @('JAVA_HOME','FG_TEST_PASSWORD','KC_HTTP_HOST','KC_HTTP_PORT','KC_HOSTNAME','KC_LOG_LEVEL','KC_BOOTSTRAP_ADMIN_USERNAME','KC_BOOTSTRAP_ADMIN_PASSWORD')) {
    $previousEnvironment[$variable] = [Environment]::GetEnvironmentVariable($variable, 'Process')
}
$env:JAVA_HOME = $JavaHome
$env:FG_TEST_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$env:KC_BOOTSTRAP_ADMIN_USERNAME = 'focusguard-admin'
$env:KC_BOOTSTRAP_ADMIN_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$realm = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'realm-template.json') -Raw | ConvertFrom-Json
$realm.realm = 'focusguard-local-test'
$realm.sslRequired = 'none'
$realm.verifyEmail = $false
$realm.requiredActions = @()
$testBff=$realm.clients | Where-Object clientId -eq 'focusguard-bff' | Select-Object -First 1
if (-not $testBff) { throw 'BFF client missing from identity realm template.' }
$testBff.redirectUris = @('https://localhost:18445/signin-oidc')
$testBff | Add-Member -NotePropertyName secret -NotePropertyValue ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))
$realm | Add-Member -NotePropertyName users -NotePropertyValue @(@{
    username='phase1@example.invalid'; email='phase1@example.invalid'; enabled=$true; emailVerified=$true
    firstName='Phase'; lastName='Test'; requiredActions=@()
    credentials=@(@{type='password'; value=$env:FG_TEST_PASSWORD; temporary=$false})
})
$import = Join-Path $KeycloakHome 'data/import'
New-Item -ItemType Directory -Path $import -Force | Out-Null
$realm | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath (Join-Path $import 'focusguard-local-test-realm.json') -Encoding utf8
$logDirectory = Join-Path $repoRoot 'artifacts/cloud-phase1'
$process = $null
try {
    $env:KC_HTTP_HOST = '127.0.0.1'
    $env:KC_HTTP_PORT = '18443'
    $env:KC_HOSTNAME = 'http://127.0.0.1:18443'
    $env:KC_LOG_LEVEL = 'warn'
    $process = Start-Process -FilePath (Join-Path $KeycloakHome 'bin/kc.bat') -ArgumentList 'start-dev --db=dev-mem --import-realm' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory 'keycloak-stdout.log') -RedirectStandardError (Join-Path $logDirectory 'keycloak-stderr.log')
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        try { $discovery = Invoke-RestMethod 'http://127.0.0.1:18443/realms/focusguard-local-test/.well-known/openid-configuration'; $ready = $true; break } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw 'Keycloak did not become ready; inspect local logs.' }
    if ($discovery.issuer -ne 'http://127.0.0.1:18443/realms/focusguard-local-test') { throw 'Unexpected issuer.' }
    $admin = Invoke-RestMethod 'http://127.0.0.1:18443/realms/master/protocol/openid-connect/token' -Method Post -Body @{
        client_id = 'admin-cli'
        grant_type = 'password'
        username = $env:KC_BOOTSTRAP_ADMIN_USERNAME
        password = $env:KC_BOOTSTRAP_ADMIN_PASSWORD
    }
    . (Join-Path $repoRoot 'cloud/deploy/Native-SessionPolicy.ps1')
    Set-FocusGuardNativeSessionPolicy -Base 'http://127.0.0.1:18443' -Realm 'focusguard-local-test' -Headers @{ Authorization = 'Bearer ' + $admin.access_token }
    dotnet run --project (Join-Path $PSScriptRoot 'Tests/FocusGuard.Identity.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Identity integration checks failed.' }
} finally {
    if ($process) {
        # Only descendants of the helper created by this script; never all Java processes.
        $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)")
        foreach ($child in $children) { Stop-Process -Id $child.ProcessId -ErrorAction SilentlyContinue }
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath (Join-Path $import 'focusguard-local-test-realm.json') -ErrorAction SilentlyContinue
    foreach ($variable in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($variable, $previousEnvironment[$variable], 'Process') }
}
