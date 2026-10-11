$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$helperPath = Join-Path $PSScriptRoot 'New-FocusGuardIpcPolicy.ps1'
$testOwnerSid = 'S-1-5-21-1111111111-2222222222-3333333333-1001'
$tempRoot = Join-Path $repositoryRoot ('.fg173-ipc-policy-test-' + [Guid]::NewGuid().ToString('N'))
$fixtureRoot = Join-Path $tempRoot 'install'
$missingRoot = Join-Path $tempRoot 'missing'
$aliasRoot = Join-Path $tempRoot 'install-alias'

function Assert-TestCondition {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Throws {
    param([Parameter(Mandatory)][scriptblock]$Action, [Parameter(Mandatory)][string]$Message)
    $threw = $false
    try { & $Action }
    catch { $threw = $true }
    if (-not $threw) { throw $Message }
}

function Add-FakeRoleFiles {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [switch]$OmitNativeMessaging
    )

    [void](New-Item -ItemType Directory -Path $Directory -Force)
    $names = @(
        'FocusGuard.Service.exe', 'FocusGuard.Service.dll',
        'FocusGuard.UI.exe', 'FocusGuard.UI.dll',
        'FocusGuard.ProcessAgent.exe', 'FocusGuard.ProcessAgent.dll'
    )
    if (-not $OmitNativeMessaging) {
        $names += @('FocusGuard.NativeMessaging.exe', 'FocusGuard.NativeMessaging.dll')
    }
    foreach ($name in $names) {
        $payload = [Text.Encoding]::UTF8.GetBytes("fixture:$name")
        [IO.File]::WriteAllBytes((Join-Path $Directory $name), $payload)
    }
}

try {
    [void](New-Item -ItemType Directory -Path $tempRoot -Force)
    Add-FakeRoleFiles -Directory $fixtureRoot
    Add-FakeRoleFiles -Directory $missingRoot -OmitNativeMessaging
    . $helperPath

    $outputPath = Join-Path $fixtureRoot 'ipc-policy.json'
    $manifest = New-FocusGuardIpcPolicyManifest -OwnerSid $testOwnerSid -InstallRoot $fixtureRoot -OutputPath $outputPath
    Assert-TestCondition ($manifest.schemaVersion -eq 1) 'Manifest did not use schemaVersion 1.'
    Assert-TestCondition ($manifest.ownerUserSid -eq $testOwnerSid) 'Manifest changed the explicit owner SID.'
    Assert-TestCondition ($manifest.installRoot -eq [IO.Path]::GetFullPath($fixtureRoot)) 'Manifest installRoot was not canonical.'
    Assert-TestCondition ($manifest.service.serviceName -eq 'FocusGuard') 'Manifest service name was incorrect.'
    Assert-TestCondition ($manifest.service.fileName -eq 'FocusGuard.Service.exe') 'Manifest service executable was incorrect.'
    Assert-TestCondition ($manifest.service.managedFileName -eq 'FocusGuard.Service.dll') 'Manifest omitted the service managed assembly.'
    Assert-TestCondition ($manifest.binaries.Count -eq 3) 'Manifest did not contain exactly the three required binary roles.'
    Assert-TestCondition ((@($manifest.binaries | ForEach-Object role) -join ',') -eq 'WebViewHost,ProcessAgent,NativeMessaging') 'Required role ordering or values changed.'
    $aadSidManifest = New-FocusGuardIpcPolicyManifest -OwnerSid 'S-1-12-1-123-456-789-1001' -InstallRoot $fixtureRoot -OutputPath $outputPath
    Assert-TestCondition ($aadSidManifest.ownerUserSid -eq 'S-1-12-1-123-456-789-1001') 'A valid Azure AD user SID was not retained.'

    foreach ($record in @($manifest.service) + @($manifest.binaries)) {
        $exePath = Join-Path $fixtureRoot $record.fileName
        $managedPath = Join-Path $fixtureRoot $record.managedFileName
        $expectedExeHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToUpperInvariant()
        $expectedManagedHash = (Get-FileHash -LiteralPath $managedPath -Algorithm SHA256).Hash.ToUpperInvariant()
        Assert-TestCondition ($record.sha256 -eq $expectedExeHash) "Executable hash mismatch for $($record.fileName)."
        Assert-TestCondition ($record.managedSha256 -eq $expectedManagedHash) "Managed assembly hash mismatch for $($record.managedFileName)."
        Assert-TestCondition ($record.sha256 -match '^[A-F0-9]{64}$' -and $record.managedSha256 -match '^[A-F0-9]{64}$') 'A policy hash was not uppercase SHA-256.'
    }

    $optionalRoot = Join-Path $tempRoot 'optional'
    Add-FakeRoleFiles -Directory $optionalRoot
    foreach ($name in @('FocusGuard.Diagnostic.exe', 'FocusGuard.Diagnostic.dll')) {
        [IO.File]::WriteAllBytes((Join-Path $optionalRoot $name), [Text.Encoding]::UTF8.GetBytes("fixture:$name"))
    }
    $optionalManifest = New-FocusGuardIpcPolicyManifest -OwnerSid $testOwnerSid -InstallRoot $optionalRoot `
        -OutputPath (Join-Path $optionalRoot 'ipc-policy.json') -OptionalBinaries ([ordered]@{
            WebViewHost = $true
            Diagnostic = @{ fileName = 'FocusGuard.Diagnostic.exe'; managedFileName = 'FocusGuard.Diagnostic.dll' }
        })
    Assert-TestCondition ($optionalManifest.binaries.Count -eq 4) 'Explicit optional roles were not included.'
    Assert-TestCondition ($optionalManifest.binaries[0].role -eq 'WebViewHost' -and $optionalManifest.binaries[0].fileName -eq 'FocusGuard.UI.exe') 'WebViewHost was not provisioned with the fixed host executable.'
    Assert-TestCondition ($optionalManifest.binaries[3].role -eq 'Diagnostic') 'Explicit diagnostic role was not included.'

    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid 'S-1-5-18' -InstallRoot $fixtureRoot -OutputPath $outputPath | Out-Null
    } 'SYSTEM was accepted as the owner SID.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid 'S-1-5-19' -InstallRoot $fixtureRoot -OutputPath $outputPath | Out-Null
    } 'LocalService was accepted as the owner SID.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid 'S-1-5-32-544' -InstallRoot $fixtureRoot -OutputPath $outputPath | Out-Null
    } 'A builtin group SID was accepted as the owner SID.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid 'not-a-sid' -InstallRoot $fixtureRoot -OutputPath $outputPath | Out-Null
    } 'Malformed owner SID was accepted.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid 'S-1-12-1-123-456-789-4444444444' -InstallRoot $fixtureRoot -OutputPath $outputPath | Out-Null
    } 'An owner SID with a subauthority outside the Windows SID range was accepted.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid $testOwnerSid -InstallRoot $missingRoot -OutputPath (Join-Path $missingRoot 'ipc-policy.json') | Out-Null
    } 'A missing required NativeMessaging payload was accepted.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid $testOwnerSid -InstallRoot $fixtureRoot -OutputPath (Join-Path $tempRoot 'outside.json') | Out-Null
    } 'An output path outside InstallRoot was accepted.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid $testOwnerSid -InstallRoot $fixtureRoot -OutputPath $outputPath `
            -OptionalBinaries @{ Diagnostic = @{ fileName = 'FocusGuard.ProcessAgent.exe'; managedFileName = 'FocusGuard.ProcessAgent.dll' } } | Out-Null
    } 'An optional role aliasing a required executable and assembly was accepted.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid $testOwnerSid -InstallRoot $optionalRoot -OutputPath (Join-Path $optionalRoot 'ipc-policy.json') `
            -OptionalBinaries @{ Diagnostic = @{ fileName = 'FocusGuard.Diagnostic.exe'; managedFileName = 'FocusGuard.UI.dll' } } | Out-Null
    } 'An optional role with a non-corresponding managed assembly was accepted.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid $testOwnerSid -InstallRoot $fixtureRoot -OutputPath $outputPath `
            -OptionalBinaries @{ Diagnostic = @{ fileName = '..\outside.exe'; managedFileName = '..\outside.dll' } } | Out-Null
    } 'A traversal path in an optional descriptor was accepted.'
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid $testOwnerSid -InstallRoot $fixtureRoot -OutputPath $outputPath `
            -OptionalBinaries @{ Unknown = $true } | Out-Null
    } 'An unknown optional role was silently accepted.'

    $junctionOutput = & $env:ComSpec /d /c "mklink /J `"$aliasRoot`" `"$fixtureRoot`"" 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $aliasRoot)) {
        throw "Could not create the temporary junction needed for the reparse-path test: $junctionOutput"
    }
    Assert-Throws {
        New-FocusGuardIpcPolicyManifest -OwnerSid $testOwnerSid -InstallRoot $aliasRoot -OutputPath (Join-Path $aliasRoot 'ipc-policy.json') | Out-Null
    } 'An installation root reached through a reparse point was accepted.'

    $dryRunOutput = Join-Path $fixtureRoot 'ipc-policy.json'
    $dryRunResult = & $helperPath -OwnerSid $testOwnerSid -InstallRoot $fixtureRoot -OutputPath $dryRunOutput -DryRun
    Assert-TestCondition (-not (Test-Path -LiteralPath $dryRunOutput)) 'DryRun wrote policy material to disk.'
    Assert-TestCondition ($dryRunResult.schemaVersion -eq 1) 'DryRun did not return the manifest object.'

    $writtenManifest = & $helperPath -OwnerSid $testOwnerSid -InstallRoot $fixtureRoot -OutputPath $outputPath
    $writtenJson = Get-Content -Raw -LiteralPath $outputPath | ConvertFrom-Json
    Assert-TestCondition ($writtenJson.schemaVersion -eq 1 -and $writtenJson.binaries.Count -eq 3) 'Atomic output was not valid version 1 JSON.'
    Assert-TestCondition ($writtenManifest.ownerUserSid -eq $testOwnerSid) 'The writer did not return the generated schema.'
    Assert-TestCondition (@(Get-ChildItem -LiteralPath $fixtureRoot -Force -Filter '.ipc-policy.*.tmp').Count -eq 0) 'Atomic output left a temporary file behind.'

    [IO.File]::WriteAllBytes((Join-Path $fixtureRoot 'FocusGuard.UI.dll'), [Text.Encoding]::UTF8.GetBytes('fixture:changed UI assembly'))
    $null = & $helperPath -OwnerSid $testOwnerSid -InstallRoot $fixtureRoot -OutputPath $outputPath
    $replacedJson = Get-Content -Raw -LiteralPath $outputPath | ConvertFrom-Json
    $expectedUpdatedHash = (Get-FileHash -LiteralPath (Join-Path $fixtureRoot 'FocusGuard.UI.dll') -Algorithm SHA256).Hash.ToUpperInvariant()
    Assert-TestCondition ($replacedJson.binaries[0].managedSha256 -eq $expectedUpdatedHash) 'Atomic replacement retained a stale managed assembly hash.'

    Write-Output 'IPC policy provisioning fixture tests passed. Fake fixture hashes and temporary junction only; no installation, process, service, ACL, registry, or deployed identity changes.'
}
finally {
    $canonicalWorkspace = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    $canonicalTempRoot = [IO.Path]::GetFullPath($tempRoot)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($canonicalTempRoot), $canonicalWorkspace, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($canonicalTempRoot) -notmatch '^\.fg173-ipc-policy-test-[a-f0-9]{32}$') {
        throw "Refusing fixture cleanup outside the direct test workspace child: $canonicalTempRoot"
    }

    $canonicalAliasRoot = [IO.Path]::GetFullPath($aliasRoot)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($canonicalAliasRoot), $canonicalTempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([IO.Path]::GetFileName($canonicalAliasRoot), 'install-alias', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing junction cleanup outside the generated test directory: $canonicalAliasRoot"
    }
    if (Test-Path -LiteralPath $canonicalAliasRoot) {
        $aliasItem = Get-Item -LiteralPath $canonicalAliasRoot -Force
        if (($aliasItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
            throw 'The expected test alias is no longer a reparse point; refusing junction cleanup.'
        }
        # Remove the known junction itself without recursive traversal. On the
        # current PowerShell 7 host, Remove-Item on mklink /J throws a provider
        # NullReferenceException; Directory.Delete(path, false) unlinks the
        # junction and leaves its target directory intact.
        [IO.Directory]::Delete($canonicalAliasRoot, $false)
    }

    if (Test-Path -LiteralPath $canonicalTempRoot) {
        $tempItem = Get-Item -LiteralPath $canonicalTempRoot -Force
        if (-not $tempItem.PSIsContainer -or ($tempItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'The generated fixture root changed type; refusing recursive cleanup.'
        }
        # The absolute target was checked to be one generated direct child of
        # this explicit D:\TEMP workspace before recursive cleanup.
        Remove-Item -LiteralPath $canonicalTempRoot -Recurse -Force
    }
}
