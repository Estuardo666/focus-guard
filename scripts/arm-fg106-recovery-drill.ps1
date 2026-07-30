[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard.Development'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$assemblyDirectory = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\FocusGuard.Service\win-x64'))
$stateDirectory = [IO.Path]::GetFullPath(
    (Join-Path $env:ProgramData 'FocusGuard\Development\StrictPersistence'))
$expectedStateDirectory = 'C:\ProgramData\FocusGuard\Development\StrictPersistence'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}
if (-not [string]::Equals(
    $stateDirectory,
    $expectedStateDirectory,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unexpected state directory: $stateDirectory"
}
if (Test-Path -LiteralPath $stateDirectory) {
    throw "Refusing to overwrite existing strict state: $stateDirectory"
}

$service = Get-CimInstance Win32_Service -Filter "Name = '$serviceName'" -ErrorAction Stop
$expectedBinary = 'C:\ProgramData\FocusGuard\Development\Service\FocusGuard.Service.exe'
if (-not [string]::Equals(
    [IO.Path]::GetFullPath($service.PathName.Trim('"')),
    $expectedBinary,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The development service binary path is unexpected.'
}

$domainAssembly = Join-Path $assemblyDirectory 'FocusGuard.Domain.dll'
$serviceAssembly = Join-Path $assemblyDirectory 'FocusGuard.Service.dll'
if (-not (Test-Path -LiteralPath $domainAssembly -PathType Leaf) -or
    -not (Test-Path -LiteralPath $serviceAssembly -PathType Leaf)) {
    throw 'Publish the win-x64 service before arming the drill.'
}
[Reflection.Assembly]::LoadFrom($domainAssembly) | Out-Null
[Reflection.Assembly]::LoadFrom($serviceAssembly) | Out-Null

New-Item -ItemType Directory -Path $stateDirectory | Out-Null
& icacls.exe $stateDirectory `
    '/inheritance:r' `
    '/grant:r' `
    '*S-1-5-18:(OI)(CI)(F)' `
    '*S-1-5-32-544:(OI)(CI)(F)' `
    '*S-1-5-19:(OI)(CI)(M)'
if ($LASTEXITCODE -ne 0) {
    throw "icacls failed with exit code $LASTEXITCODE."
}

$integrityKey = [byte[]]::new(32)
[Security.Cryptography.RandomNumberGenerator]::Fill($integrityKey)
try {
    $protectedKey = [Security.Cryptography.ProtectedData]::Protect(
        $integrityKey,
        $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    $keyPath = Join-Path $stateDirectory 'integrity-key.dpapi'
    [IO.File]::WriteAllBytes($keyPath, $protectedKey)

    $generated = [FocusGuard.Service.RecoveryCodeService]::Generate()
    $now = [DateTimeOffset]::UtcNow
    $end = $now.AddMinutes(10)
    $policy = [FocusGuard.Domain.EnforcementPolicy]::new(
        [Guid]::NewGuid(),
        [FocusGuard.Domain.ProfileId]::new([Guid]::NewGuid()),
        $now,
        $end,
        $true,
        [FocusGuard.Domain.ApplicationRule[]]@(),
        [FocusGuard.Domain.WebsiteRule[]]@())
    $snapshot = [FocusGuard.Service.StrictPolicySnapshot]::new(
        1,
        $policy,
        $now,
        [FocusGuard.Service.RebootExpirationDrillWorker]::GetCurrentBootId(),
        $generated.Credential,
        [TimeSpan]::FromMinutes(1))
    $store = [FocusGuard.Service.StrictPolicySnapshotStore]::new(
        $integrityKey,
        $stateDirectory)
    $store.SaveAsync(
        $snapshot,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    $audit = [FocusGuard.Service.RecoveryAuditLog]::new($stateDirectory)
    $audit.WriteAsync(
        $policy.SessionId,
        $now,
        'DrillArmed',
        'SyntheticEmptyPolicy',
        [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null

    [pscustomobject]@{
        SessionId = $policy.SessionId
        RecoveryCode = $generated.DisplayCode
        ArmedUtc = $now
        ExpiresUtc = $end
        RecoveryDelaySeconds = 60
        ApplicationRules = 0
        WebsiteRules = 0
        StateDirectory = $stateDirectory
    }
}
catch {
    if (Test-Path -LiteralPath $stateDirectory) {
        Remove-Item -LiteralPath $stateDirectory -Recurse -Force
    }
    throw
}
finally {
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($integrityKey)
}
