[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [switch]$PreflightOnly,
    [switch]$Execute,
    [switch]$OwnerPresent
)

$ErrorActionPreference = 'Stop'
$ownerPresentMode = $OwnerPresent.IsPresent
if ($PreflightOnly.IsPresent -eq $Execute.IsPresent) {
    throw 'Specify exactly one of -PreflightOnly or -Execute.'
}
if ($Execute -and -not $ownerPresentMode) {
    throw 'The rollback drill changes Program Files and service state; pass -OwnerPresent only while the owner is present.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramFiles} 'FocusGuard'))
$installedIpcPolicyPath = Join-Path $installRoot 'ipc-policy.json'
if (-not (Test-Path -LiteralPath $installedIpcPolicyPath -PathType Leaf)) {
    throw 'Installed FocusGuard IPC policy is missing.'
}
$installedIpcPolicy = Get-Content -LiteralPath $installedIpcPolicyPath -Raw | ConvertFrom-Json
$clientMetadataQueryConsent = [bool]$installedIpcPolicy.clientMetadataQueryConsent
if (-not (Test-Path -LiteralPath $installRoot -PathType Container)) {
    throw "Installed FocusGuard path is missing: $installRoot"
}
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
    throw "Candidate package directory is missing: $packageRoot"
}
$packageManifestPath = Join-Path $packageRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $packageManifestPath -PathType Leaf)) {
    throw 'Candidate package has no manifest.json.'
}
$packageManifest = Get-Content -LiteralPath $packageManifestPath -Raw | ConvertFrom-Json
if ($packageManifest.Product -ne 'FocusGuard' -or
    $packageManifest.SchemaVersion -ne 1 -or
    $packageManifest.RuntimeIdentifier -ne 'win-x64' -or
    [string]::IsNullOrWhiteSpace([string]$packageManifest.Version) -or
    $null -eq $packageManifest.Files -or
    $packageManifest.Files.Count -notin 1..256) {
    throw 'Candidate package manifest is invalid.'
}
$packagePrefix = $packageRoot + [IO.Path]::DirectorySeparatorChar
$packageFiles = @($packageManifest.Files | ForEach-Object { [string]$_ })
if (@($packageFiles | Select-Object -Unique).Count -ne $packageFiles.Count) {
    throw 'Candidate package manifest repeats a file path.'
}
foreach ($relativePath in $packageFiles) {
    if ([IO.Path]::IsPathRooted($relativePath)) { throw 'Candidate package paths must be relative.' }
    $candidatePath = [IO.Path]::GetFullPath((Join-Path $packageRoot $relativePath))
    if (-not $candidatePath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
        throw "Candidate package file is missing or outside the package root: $relativePath"
    }
}
foreach ($required in @('FocusGuard.Setup.exe', 'install-focusguard.ps1', 'publish\FocusGuard.Service\FocusGuard.Service.exe')) {
    if ($required -notin $packageFiles) { throw "Candidate package manifest omits $required" }
}

$installedExtensionManifest = Join-Path $installRoot 'browser-extension\staging\manifest.json'
$installedVersion = if (Test-Path -LiteralPath $installedExtensionManifest -PathType Leaf) {
    (Get-Content -LiteralPath $installedExtensionManifest -Raw | ConvertFrom-Json).version
} else { $null }
if ([string]::IsNullOrWhiteSpace([string]$installedVersion) -or
    [version]$packageManifest.Version -le [version]$installedVersion) {
    throw 'Candidate package version must be newer than the installed FocusGuard version.'
}

$service = Get-CimInstance Win32_Service -Filter "Name='FocusGuard'"
$drillClient = Join-Path $installRoot 'FocusGuard.DrillClient.exe'
if ($null -eq $service -or $service.State -ne 'Running' -or
    $service.StartName -ne 'NT AUTHORITY\LocalService' -or
    -not (Test-Path -LiteralPath $drillClient -PathType Leaf)) {
    throw 'Rollback preflight requires the installed LocalService instance to be running.'
}
$statusOutput = (& $drillClient status | Out-String)
$statusExitCode = $LASTEXITCODE
if ($statusExitCode -ne 0) { throw "Installed DrillClient status failed with exit code $statusExitCode." }
$status = $statusOutput | ConvertFrom-Json
$terminalStates = @('Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed')
if (-not $status.serviceReady -or $status.sessionState -notin $terminalStates -or $status.strictMode) {
    throw 'Rollback preflight requires a ready service with no active or strict session.'
}

if ($PreflightOnly) {
    [pscustomobject]@{
        Mode = 'PreflightOnly'
        CandidateVersion = $packageManifest.Version
        InstalledVersion = $installedVersion
        PackageFilesPresent = $packageFiles.Count
        ServiceState = $service.State
        ServicePid = $service.ProcessId
        ServiceAccount = $service.StartName
        SessionState = $status.sessionState
        StrictMode = $status.strictMode
        ClientMetadataQueryConsent = $clientMetadataQueryConsent
        InstallerInvoked = $false
        MachineStateChanged = $false
    }
    return
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Rollback execution requires an already elevated PowerShell process; this script never auto-elevates.'
}
if (-not $clientMetadataQueryConsent) {
    throw 'Rollback execution requires previously recorded client-metadata consent; the drill never invents consent.'
}

function Get-TreeFingerprint([string]$Root) {
    $prefixLength = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($prefixLength)
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "$relativePath|$hash"
        } | Sort-Object)
}

try {
    $before = Get-TreeFingerprint $installRoot
    $desktopPath = Join-Path $installRoot 'FocusGuard.UI.exe'
    $ipcPolicyPath = Join-Path $installRoot 'ipc-policy.json'
    if (-not (Test-Path -LiteralPath $ipcPolicyPath -PathType Leaf)) {
        throw 'Rollback drill requires the installed owner-scoped IPC policy.'
    }
    $expectedOwnerSid = [string](Get-Content -LiteralPath $ipcPolicyPath -Raw | ConvertFrom-Json).ownerUserSid
    if ([string]::IsNullOrWhiteSpace($expectedOwnerSid)) {
        throw 'Rollback drill could not read the configured interactive owner SID.'
    }
    $expectedSessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $desktopCandidates = @(
        foreach ($candidate in @(Get-CimInstance -ClassName Win32_Process `
            -Filter "Name = 'FocusGuard.UI.exe'" -ErrorAction Stop)) {
            if ([string]::IsNullOrWhiteSpace([string]$candidate.ExecutablePath) -or
                -not [IO.Path]::GetFullPath($candidate.ExecutablePath).Equals(
                    [IO.Path]::GetFullPath($desktopPath), [StringComparison]::OrdinalIgnoreCase) -or
                $candidate.SessionId -ne $expectedSessionId) {
                continue
            }

            $owner = Invoke-CimMethod -InputObject $candidate -MethodName GetOwnerSid -ErrorAction Stop
            if ($owner.ReturnValue -ne 0 -or [string]$owner.Sid -cne $expectedOwnerSid) {
                continue
            }

            Get-Process -Id $candidate.ProcessId -ErrorAction Stop
        }
    )
    if ($desktopCandidates.Count -ne 1) {
        throw 'Rollback drill requires exactly one installed FocusGuard UI process in the configured owner session.'
    }
    $desktop = $desktopCandidates[0]
    $desktop.Refresh()
    if ($desktop.HasExited) { throw 'Installed FocusGuard UI exited before installer drill.' }

    $expectedFailure = $null
    try {
        & (Join-Path $PSScriptRoot 'install-focusguard.ps1') `
            -PackageDirectory $packageRoot `
            -ClientMetadataQueryConsent `
            -InjectFailureAfterCopyForRollbackTest
    }
    catch {
        $expectedFailure = $_.Exception.Message
    }
    if ($null -eq $expectedFailure) { throw 'Failure-injection update unexpectedly succeeded.' }
    if ($expectedFailure -cne 'Injected rollback verification failure after binary replacement.') {
        throw "Rollback drill did not reach its injected post-copy failure. Installer reported: $expectedFailure"
    }

    $after = Get-TreeFingerprint $installRoot
    $differences = @(Compare-Object $before $after)
    $service = Get-CimInstance Win32_Service -Filter "Name='FocusGuard'"
    $desktopStillRunning = @(Get-Process -Name 'FocusGuard.UI' -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Path -eq (Join-Path $installRoot 'FocusGuard.UI.exe')
        }).Count -gt 0
    $agentRunning = @(Get-Process -Name 'FocusGuard.ProcessAgent' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Join-Path $installRoot 'FocusGuard.ProcessAgent.exe') }).Count -gt 0
    $result = [pscustomobject]@{
        ExpectedFailure = $expectedFailure
        DesktopProcessId = $desktop.Id
        DesktopWindowWasVisible = ($desktop.MainWindowHandle -ne 0)
        TreeDifferences = $differences.Count
        ServiceState = $service.State
        DesktopStillRunning = $desktopStillRunning
        AgentRunning = $agentRunning
    }
    $result
    if ($differences.Count -ne 0 -or $service.State -ne 'Running' -or $desktopStillRunning -or -not $agentRunning) {
        throw "Rollback drill verification failed: TreeDifferences=$($differences.Count); ServiceState=$($service.State); DesktopStillRunning=$desktopStillRunning; AgentRunning=$agentRunning; ExpectedFailure=$expectedFailure"
    }
}
finally {
    # The drill uses only the immutable package and the installer's own backup.
}
