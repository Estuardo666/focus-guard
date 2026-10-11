[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$OwnerSid,
    [string]$InstallRoot,
    [string]$OutputPath,
    [System.Collections.IDictionary]$OptionalBinaries = @{},
    [switch]$ClientMetadataQueryConsent,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function ConvertTo-FocusGuardCanonicalDirectoryPath {
    param([Parameter(Mandatory)][string]$Path)

    if (-not [IO.Path]::IsPathRooted($Path)) {
        throw 'InstallRoot must be an absolute filesystem path.'
    }
    if ($Path.StartsWith('\\?\', [StringComparison]::Ordinal) -or
        $Path.StartsWith('\\.\', [StringComparison]::Ordinal)) {
        throw 'Device namespace paths are not supported.'
    }

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    $trimCharacters = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $fullPath = $fullPath.TrimEnd($trimCharacters)
    if ([string]::IsNullOrWhiteSpace($fullPath) -or
        [string]::Equals($fullPath, $pathRoot.TrimEnd($trimCharacters), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'InstallRoot must name a directory below a filesystem root.'
    }

    return $fullPath
}

function Assert-FocusGuardNoReparseDirectoryTraversal {
    param([Parameter(Mandatory)][string]$DirectoryPath)

    $fullPath = [IO.Path]::GetFullPath($DirectoryPath)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    $segments = $fullPath.Substring($pathRoot.Length).Split(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)
    $currentPath = $pathRoot

    foreach ($segment in $segments) {
        $currentPath = Join-Path $currentPath $segment
        $item = Get-Item -LiteralPath $currentPath -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse-point traversal is not allowed in the installation path: $currentPath"
        }
        if (-not $item.PSIsContainer) {
            throw "An installation path component is not a directory: $currentPath"
        }
    }
}

function Assert-FocusGuardUserSid {
    param([Parameter(Mandatory)][string]$SidText)

    # Accept only conventional per-user domain SIDs and Azure AD user SIDs.
    # Do not infer an owner from the elevated process running this script.
    $classicUserSid = '^S-1-5-21-(?:0|[1-9][0-9]{0,9})-(?:0|[1-9][0-9]{0,9})-(?:0|[1-9][0-9]{0,9})-(?:0|[1-9][0-9]{0,9})$'
    $azureAdUserSid = '^S-1-12-1-(?:0|[1-9][0-9]{0,9})-(?:0|[1-9][0-9]{0,9})-(?:0|[1-9][0-9]{0,9})-(?:0|[1-9][0-9]{0,9})$'
    if ($SidText -notmatch "(?i)(?:$classicUserSid|$azureAdUserSid)") {
        throw 'OwnerSid must be an explicit S-1-5-21 user SID or S-1-12-1 Azure AD user SID.'
    }

    try {
        $sid = [Security.Principal.SecurityIdentifier]::new($SidText)
    }
    catch {
        throw 'OwnerSid is not a valid Windows SID.'
    }
    if (-not [string]::Equals($sid.Value, $SidText, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OwnerSid contains a subauthority outside the Windows SID range.'
    }

    if ($sid.Value -in @('S-1-5-18', 'S-1-5-19')) {
        throw 'SYSTEM and LocalService cannot own the IPC policy.'
    }
    return $sid.Value
}

function Assert-FocusGuardLeafName {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][ValidateSet('.exe', '.dll')][string]$Extension
    )

    if ($FileName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,119}$' -or
        -not $FileName.EndsWith($Extension, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([IO.Path]::GetFileName($FileName), $FileName, [StringComparison]::Ordinal)) {
        throw "Invalid packaged $Extension leaf name: $FileName"
    }
}

function Get-FocusGuardLeafSha256 {
    param(
        [Parameter(Mandatory)][string]$InstallRootPath,
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][ValidateSet('.exe', '.dll')][string]$Extension
    )

    Assert-FocusGuardLeafName -FileName $FileName -Extension $Extension
    $filePath = Join-Path $InstallRootPath $FileName
    $canonicalFilePath = [IO.Path]::GetFullPath($filePath)
    $canonicalParent = ConvertTo-FocusGuardCanonicalDirectoryPath -Path ([IO.Path]::GetDirectoryName($canonicalFilePath))
    if (-not [string]::Equals($canonicalParent, $InstallRootPath, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([IO.Path]::GetFileName($canonicalFilePath), $FileName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Packaged file must be a direct installation-root leaf: $FileName"
    }

    $file = Get-Item -LiteralPath $canonicalFilePath -Force -ErrorAction Stop
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Packaged file must be a regular, non-reparse leaf: $FileName"
    }

    $stream = $null
    $algorithm = $null
    try {
        # FileShare.Read blocks a concurrent writer or delete while this hash is
        # read. The installation root must already be protected by its caller.
        $stream = [IO.File]::Open($canonicalFilePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $algorithm = [Security.Cryptography.SHA256]::Create()
        $digest = $algorithm.ComputeHash($stream)
        return ([BitConverter]::ToString($digest).Replace('-', '')).ToUpperInvariant()
    }
    finally {
        if ($null -ne $algorithm) { $algorithm.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Get-FocusGuardOptionalRoleSpec {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$OptionalBinaries,
        [Parameter(Mandatory)][string]$Role
    )

    $matchingKeys = @($OptionalBinaries.Keys | Where-Object {
        [string]::Equals([string]$_, $Role, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matchingKeys.Count -eq 0) { return $null }
    if ($matchingKeys.Count -ne 1) { throw "Optional binary role is repeated: $Role" }

    $value = $OptionalBinaries[$matchingKeys[0]]
    if ($Role -eq 'WebViewHost') {
        if ($value -is [bool] -and $value) {
            return [ordered]@{ fileName = 'FocusGuard.UI.exe'; managedFileName = 'FocusGuard.UI.dll' }
        }
        if ($value -is [System.Collections.IDictionary] -and
            [string]::Equals([string]$value['fileName'], 'FocusGuard.UI.exe', [StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals([string]$value['managedFileName'], 'FocusGuard.UI.dll', [StringComparison]::OrdinalIgnoreCase) -and
            @($value.Keys).Count -eq 2) {
            return [ordered]@{ fileName = 'FocusGuard.UI.exe'; managedFileName = 'FocusGuard.UI.dll' }
        }
        throw 'WebViewHost may be provisioned only as FocusGuard.UI.exe with FocusGuard.UI.dll.'
    }

    if ($Role -eq 'Diagnostic') {
        if ($value -isnot [System.Collections.IDictionary] -or @($value.Keys).Count -ne 2) {
            throw 'Diagnostic must be explicitly provisioned with fileName and managedFileName.'
        }
        $keys = @($value.Keys | ForEach-Object { [string]$_ })
        if (@($keys | Where-Object { $_ -notin @('fileName', 'managedFileName') }).Count -ne 0 -or
            @($keys | Where-Object { $_ -in @('fileName', 'managedFileName') }).Count -ne 2) {
            throw 'Diagnostic descriptor must contain exactly fileName and managedFileName.'
        }
        return [ordered]@{
            fileName = [string]$value['fileName']
            managedFileName = [string]$value['managedFileName']
        }
    }

    throw "Unsupported optional binary role: $Role"
}

function New-FocusGuardIpcPolicyManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OwnerSid,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$InstallRoot,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OutputPath,
        [System.Collections.IDictionary]$OptionalBinaries = @{},
        [switch]$ClientMetadataQueryConsent
    )

    if ($null -eq $OptionalBinaries) { throw 'OptionalBinaries must be an explicit dictionary, even when empty.' }
    $unknownOptionalRoles = @($OptionalBinaries.Keys | Where-Object {
        [string]$_ -notin @('WebViewHost', 'Diagnostic')
    })
    if ($unknownOptionalRoles.Count -gt 0) {
        throw "Unsupported optional binary role: $([string]$unknownOptionalRoles[0])"
    }

    $canonicalOwnerSid = Assert-FocusGuardUserSid -SidText $OwnerSid
    $canonicalRoot = ConvertTo-FocusGuardCanonicalDirectoryPath -Path $InstallRoot
    if (-not (Test-Path -LiteralPath $canonicalRoot -PathType Container)) {
        throw 'InstallRoot must be an existing directory.'
    }
    Assert-FocusGuardNoReparseDirectoryTraversal -DirectoryPath $canonicalRoot

    $canonicalOutputPath = [IO.Path]::GetFullPath($OutputPath)
    $expectedOutputPath = Join-Path $canonicalRoot 'ipc-policy.json'
    if (-not [string]::Equals($canonicalOutputPath, $expectedOutputPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputPath must be exactly ipc-policy.json inside InstallRoot.'
    }
    if (Test-Path -LiteralPath $canonicalOutputPath) {
        $existingOutput = Get-Item -LiteralPath $canonicalOutputPath -Force -ErrorAction Stop
        if ($existingOutput.PSIsContainer -or ($existingOutput.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'The IPC policy output must be a regular file, not a reparse point or directory.'
        }
    }

    $serviceFileName = 'FocusGuard.Service.exe'
    $serviceManagedFileName = 'FocusGuard.Service.dll'
    $service = [ordered]@{
        serviceName = 'FocusGuard'
        fileName = $serviceFileName
        sha256 = Get-FocusGuardLeafSha256 -InstallRootPath $canonicalRoot -FileName $serviceFileName -Extension '.exe'
        managedFileName = $serviceManagedFileName
        managedSha256 = Get-FocusGuardLeafSha256 -InstallRootPath $canonicalRoot -FileName $serviceManagedFileName -Extension '.dll'
    }

    $binarySpecs = [System.Collections.Generic.List[object]]::new()
    foreach ($required in @(
        # The React host is FocusGuard's interface; the Avalonia Desktop app is no longer installed or trusted.
        [ordered]@{ role = 'WebViewHost'; fileName = 'FocusGuard.UI.exe'; managedFileName = 'FocusGuard.UI.dll' },
        [ordered]@{ role = 'ProcessAgent'; fileName = 'FocusGuard.ProcessAgent.exe'; managedFileName = 'FocusGuard.ProcessAgent.dll' },
        [ordered]@{ role = 'NativeMessaging'; fileName = 'FocusGuard.NativeMessaging.exe'; managedFileName = 'FocusGuard.NativeMessaging.dll' }
    )) {
        $binarySpecs.Add($required)
    }

    # WebViewHost is required now; an explicit optional entry is still validated so old callers fail closed on bad input.
    $null = Get-FocusGuardOptionalRoleSpec -OptionalBinaries $OptionalBinaries -Role 'WebViewHost'
    foreach ($role in @('Diagnostic')) {
        $optional = Get-FocusGuardOptionalRoleSpec -OptionalBinaries $OptionalBinaries -Role $role
        if ($null -ne $optional) {
            $binarySpecs.Add([ordered]@{
                role = $role
                fileName = $optional.fileName
                managedFileName = $optional.managedFileName
            })
        }
    }

    $claimedNames = [System.Collections.Generic.List[string]]::new()
    $claimedNames.Add($service.fileName)
    $claimedNames.Add($service.managedFileName)
    $binaries = [System.Collections.Generic.List[object]]::new()
    foreach ($spec in $binarySpecs) {
        Assert-FocusGuardLeafName -FileName $spec.fileName -Extension '.exe'
        Assert-FocusGuardLeafName -FileName $spec.managedFileName -Extension '.dll'
        if (-not [string]::Equals(
            [IO.Path]::GetFileNameWithoutExtension($spec.fileName),
            [IO.Path]::GetFileNameWithoutExtension($spec.managedFileName),
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "The managed assembly must correspond to the packaged executable role: $($spec.role)"
        }
        foreach ($name in @($spec.fileName, $spec.managedFileName)) {
            if (@($claimedNames | Where-Object {
                [string]::Equals($_, $name, [StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0) {
                throw "A packaged file cannot be aliased across policy roles: $name"
            }
            $claimedNames.Add($name)
        }

        $binaries.Add([ordered]@{
            role = $spec.role
            fileName = $spec.fileName
            sha256 = Get-FocusGuardLeafSha256 -InstallRootPath $canonicalRoot -FileName $spec.fileName -Extension '.exe'
            managedFileName = $spec.managedFileName
            managedSha256 = Get-FocusGuardLeafSha256 -InstallRootPath $canonicalRoot -FileName $spec.managedFileName -Extension '.dll'
        })
    }

    return [ordered]@{
        schemaVersion = 1
        clientMetadataQueryConsent = [bool]$ClientMetadataQueryConsent
        ownerUserSid = $canonicalOwnerSid
        installRoot = $canonicalRoot
        service = $service
        binaries = [object[]]$binaries.ToArray()
    }
}

function Write-FocusGuardIpcPolicyManifestAtomically {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][string]$Json
    )

    $directoryPath = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))
    Assert-FocusGuardNoReparseDirectoryTraversal -DirectoryPath $directoryPath
    $temporaryStem = '.ipc-policy.' + [Guid]::NewGuid().ToString('N')
    $temporaryPath = Join-Path $directoryPath ($temporaryStem + '.tmp')
    $backupPath = Join-Path $directoryPath ($temporaryStem + '.bak')
    $stream = $null
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Json + "`n")
        $stream = [IO.FileStream]::new($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
        $stream.Dispose()
        $stream = $null

        if (Test-Path -LiteralPath $OutputPath) {
            $existingOutput = Get-Item -LiteralPath $OutputPath -Force -ErrorAction Stop
            if ($existingOutput.PSIsContainer -or ($existingOutput.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'The IPC policy output must be a regular file, not a reparse point or directory.'
            }
            [IO.File]::Replace($temporaryPath, $OutputPath, $backupPath)
            if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath -Force }
        }
        else {
            [IO.File]::Move($temporaryPath, $OutputPath)
        }
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
        if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath -Force }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $manifest = New-FocusGuardIpcPolicyManifest -OwnerSid $OwnerSid -InstallRoot $InstallRoot `
        -OutputPath $OutputPath -OptionalBinaries $OptionalBinaries -ClientMetadataQueryConsent:$ClientMetadataQueryConsent
    $json = ConvertTo-Json -InputObject $manifest -Depth 6

    if (-not $DryRun -and $PSCmdlet.ShouldProcess($OutputPath, 'Atomically write protected FocusGuard IPC policy JSON')) {
        Write-FocusGuardIpcPolicyManifestAtomically -OutputPath $OutputPath -Json $json
    }

    Write-Output -NoEnumerate $manifest
}
