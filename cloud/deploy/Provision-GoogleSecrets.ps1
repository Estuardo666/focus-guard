$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stagePath = Join-Path $repoRoot 'artifacts/cloud-phase1/staging'
. (Join-Path $PSScriptRoot 'Gcp-Staging.ps1')
$accessToken = (gcloud auth print-access-token --account=transcitynorwalk@gmail.com).Trim()
if (-not $accessToken) { throw 'Google Cloud credentials unavailable.' }
$headers = @{Authorization = 'Bearer ' + $accessToken }
Add-Type -AssemblyName System.Security
function Add-Secret([string]$SecretName, [byte[]]$SecretValue) {
    $encoded = [Convert]::ToBase64String($SecretValue)
    $payload = @{payload=@{data=$encoded}} | ConvertTo-Json -Depth 3 -Compress
    $encoded=$null
    $resource = "projects/$projectId/secrets/$SecretName"
    try {
        $null = Invoke-RestMethod -Method Post -Uri "https://secretmanager.googleapis.com/v1/projects/$projectId/secrets?secretId=$SecretName" -Headers $headers -ContentType 'application/json' -Body (@{replication=@{automatic=@{}}} | ConvertTo-Json -Compress) -TimeoutSec 30
    } catch {
        if ([int]$_.Exception.Response.StatusCode -ne 409) { $payload=$null; throw "Unable to create Secret Manager resource $SecretName." }
    }
    try { $null = Invoke-RestMethod -Method Post -Uri "https://secretmanager.googleapis.com/v1/${resource}:addVersion" -Headers $headers -ContentType 'application/json' -Body $payload -TimeoutSec 30 }
    catch {
        $status=[int]$_.Exception.Response.StatusCode
        $safeReason='request failed'
        try {
            $stream=$_.Exception.Response.GetResponseStream(); $reader=[IO.StreamReader]::new($stream); $errorBody=$reader.ReadToEnd() | ConvertFrom-Json
            $safeReason=$errorBody.error.message
            $reader.Dispose()
        } catch {}
        $payload=$null
        throw "Unable to add version for $SecretName (HTTP ${status}: $safeReason)."
    }
    $payload=$null
}

$dbBytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $stagePath 'neon-credentials.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
$fixtureBytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $stagePath 'identity-fixture.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
try {
    $db = [Text.Encoding]::UTF8.GetString($dbBytes) | ConvertFrom-Json
    $fixture = [Text.Encoding]::UTF8.GetString($fixtureBytes) | ConvertFrom-Json
    $apiConnection='Host=' + $db.product.host + ';Database=' + $db.product.database + ';Username=' + $db.product.role + ';Password=' + $db.product.password + ';SSL Mode=VerifyFull;Maximum Pool Size=4;Minimum Pool Size=0;Timeout=15;Command Timeout=10'
    $apiBytes=[Text.Encoding]::UTF8.GetBytes($apiConnection)
    try { Add-Secret "$infrastructurePrefix-api-product-db-url" $apiBytes } finally { [Array]::Clear($apiBytes,0,$apiBytes.Length) }
    Add-Secret "$infrastructurePrefix-keycloak-db-password" ([Text.Encoding]::UTF8.GetBytes($db.keycloak.password))
    Add-Secret "$infrastructurePrefix-keycloak-bootstrap-admin-password" ([Text.Encoding]::UTF8.GetBytes($fixture.adminPassword))
    Add-Secret "$infrastructurePrefix-bff-db-url" ([Text.Encoding]::UTF8.GetBytes($db.bff.url))
    Add-Secret "$infrastructurePrefix-bff-client-secret" ([Text.Encoding]::UTF8.GetBytes($fixture.bffSecret))
    $bffKeyPath=Join-Path $stagePath 'bff-encryption.dpapi'
    if (-not (Test-Path -LiteralPath $bffKeyPath)) {
        $bffKey=[Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        try { [IO.File]::WriteAllBytes($bffKeyPath,[Security.Cryptography.ProtectedData]::Protect($bffKey,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)) } finally { [Array]::Clear($bffKey,0,$bffKey.Length) }
    }
    $bffKey=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($bffKeyPath),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { Add-Secret "$infrastructurePrefix-bff-encryption-key" $bffKey } finally { [Array]::Clear($bffKey,0,$bffKey.Length) }
    Write-Output 'SECRETS_READY: api-db, keycloak-db, keycloak-bootstrap, bff-db, bff-client, bff-encryption. No values were printed.'
} finally {
    [Array]::Clear($dbBytes,0,$dbBytes.Length); [Array]::Clear($fixtureBytes,0,$fixtureBytes.Length)
    $accessToken=$null; $headers=$null; $db=$null; $fixture=$null; $apiConnection=$null
}
