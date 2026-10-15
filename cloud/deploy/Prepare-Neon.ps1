param([string]$ProjectId = 'twilight-dust-71788827')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$vaultPath = Join-Path $repoRoot 'artifacts/cloud-phase1/staging/neon-credentials.dpapi'
if (Test-Path -LiteralPath $vaultPath) { throw 'Credentials already provisioned; refusing repeat creation.' }
$ownerConnection = (neon connection-string --project-id $ProjectId --role-name neondb_owner --database-name neondb --ssl verify-full --no-analytics | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $ownerConnection.StartsWith('postgres')) { throw 'Unable to obtain connection privately.' }
$credentialJson = $ownerConnection | node (Join-Path $repoRoot 'cloud/portal/scripts/provision-neon.mjs') | Out-String
$provisionExit = $LASTEXITCODE
Add-Type -AssemblyName System.Security
$bytes = [Text.Encoding]::UTF8.GetBytes($credentialJson)
try {
    $protected = [Security.Cryptography.ProtectedData]::Protect($bytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes($vaultPath, $protected)
} finally { [Array]::Clear($bytes, 0, $bytes.Length); $ownerConnection = $null; $credentialJson = $null }
if ($provisionExit -ne 0) { throw 'Provisioning failed; partial recovery state protected in local vault. Do not rerun blindly.' }
Write-Output 'NEON_DATABASES_PROVISIONED keycloak/product/bff; credentials protected with CurrentUser DPAPI.'
