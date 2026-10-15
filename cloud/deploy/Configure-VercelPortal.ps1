$ErrorActionPreference='Stop'
$repoRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stagePath=Join-Path $repoRoot 'artifacts/cloud-phase1/staging'
$vercelRoot=Join-Path $env:USERPROFILE '.codex/focusguard-staging-build/portal'
Add-Type -AssemblyName System.Security

if (-not (Test-Path -LiteralPath (Join-Path $vercelRoot '.vercel/project.json'))) {
    throw 'The linked FocusGuard staging Vercel project is unavailable.'
}

function Read-DpapiJson([string]$Name) {
    $bytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $stagePath $Name)),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { return [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json }
    finally { [Array]::Clear($bytes,0,$bytes.Length) }
}

function Set-VercelValue([string]$Name,[string]$Value,[bool]$Secret) {
    $kind=if($Secret){'--sensitive'}else{''}
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$env:ComSpec
    $start.Arguments="/d /q /c vercel env add $Name production --force --yes $kind"
    $start.WorkingDirectory=$vercelRoot
    $start.UseShellExecute=$false
    $start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true
    $start.RedirectStandardOutput=$true
    $start.RedirectStandardError=$true
    $process=[Diagnostics.Process]::new()
    $process.StartInfo=$start
    if(-not $process.Start()){throw "Could not start Vercel CLI for $Name."}
    $stdoutTask=$process.StandardOutput.ReadToEndAsync()
    $stderrTask=$process.StandardError.ReadToEndAsync()
    try {
        # Vercel stores stdin verbatim. A line terminator corrupts canonical
        # base64 encryption keys and exact client-secret comparisons.
        $process.StandardInput.Write($Value)
        $process.StandardInput.Close()
        $process.WaitForExit()
        $stdout=$stdoutTask.GetAwaiter().GetResult()
        $stderr=$stderrTask.GetAwaiter().GetResult()
        if($process.ExitCode -ne 0){throw "Vercel rejected environment variable $Name (exit $($process.ExitCode))."}
        Write-Output "STORED_VERCEL_PRODUCTION_VARIABLE=$Name;TYPE=$(if($Secret){'Secret'}else{'Config'})"
    } finally {
        if(-not $process.HasExited){$process.Kill($true)}
        $stdout=$null;$stderr=$null
        $process.Dispose()
    }
}

$databaseBytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $stagePath 'neon-credentials.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
$encryptionBytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes((Join-Path $stagePath 'bff-encryption.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
try {
    $database=[Text.Encoding]::UTF8.GetString($databaseBytes) | ConvertFrom-Json
    $fixture=Read-DpapiJson 'identity-fixture.dpapi'
    $validDatabase=([string]$database.bff.url).StartsWith('postgresql://',[StringComparison]::OrdinalIgnoreCase)
    if(-not $validDatabase -or [string]::IsNullOrWhiteSpace($fixture.bffSecret) -or $encryptionBytes.Length -ne 32){
        throw 'Protected staging identity material failed validation.'
    }
    $values=@(
        @{Name='PORTAL_ORIGIN';Value='https://focusguard.totemmassmedia.com';Secret=$false},
        @{Name='OIDC_ISSUER';Value='https://auth.focusguard.totemmassmedia.com/realms/focusguard';Secret=$false},
        @{Name='FOCUSGUARD_API_ORIGIN';Value='https://api.focusguard.totemmassmedia.com';Secret=$false},
        @{Name='DATABASE_URL';Value=[string]$database.bff.url;Secret=$true},
        @{Name='OIDC_CLIENT_SECRET';Value=[string]$fixture.bffSecret;Secret=$true},
        @{Name='BFF_ENCRYPTION_KEY';Value=[Convert]::ToBase64String($encryptionBytes);Secret=$true}
    )
    foreach($item in $values){Set-VercelValue $item.Name $item.Value $item.Secret}
    Write-Output 'VERCEL_VARIABLES_STORED;NO_DEPLOYMENT_STARTED'
} finally {
    [Array]::Clear($databaseBytes,0,$databaseBytes.Length)
    [Array]::Clear($encryptionBytes,0,$encryptionBytes.Length)
    $database=$null;$fixture=$null;$values=$null
}
