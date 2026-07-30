[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\artifacts\installer\FocusGuard-0.1.0'),
    [switch]$InjectFailureAfterCopyForRollbackTest
)

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard'
$installRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramFiles} 'FocusGuard'))
$dataRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramData} 'FocusGuard'))
$allowedProgramFilesRoot = [IO.Path]::GetFullPath(${env:ProgramFiles})
$allowedProgramDataRoot = [IO.Path]::GetFullPath(${env:ProgramData})
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
$required = @(
    (Join-Path $packageRoot 'publish\FocusGuard.Service\FocusGuard.Service.exe'),
    (Join-Path $packageRoot 'publish\FocusGuard.Recovery\FocusGuard.Recovery.exe'),
    (Join-Path $packageRoot 'publish\FocusGuard.Desktop\FocusGuard.Desktop.exe'),
    (Join-Path $packageRoot 'publish\FocusGuard.ProcessAgent\FocusGuard.ProcessAgent.exe'),
    (Join-Path $packageRoot 'publish\FocusGuard.NativeMessaging\FocusGuard.NativeMessaging.exe'),
    (Join-Path $packageRoot 'native-messaging\chrome.json'),
    (Join-Path $packageRoot 'native-messaging\edge.json'),
    (Join-Path $packageRoot 'native-messaging\focusguard.native.json'),
    (Join-Path $packageRoot 'browser-extension\distribution.json'),
    (Join-Path $packageRoot 'manifest.json')
)
$chromeHostRegistry = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.focusguard.native'
$edgeHostRegistry = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.focusguard.native'
$startMenuDirectory = Join-Path ${env:ProgramData} 'Microsoft\Windows\Start Menu\Programs'
$startMenuShortcut = Join-Path $startMenuDirectory 'FocusGuard.lnk'

function Get-FocusGuardTreeFingerprint([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { return @() }
    $prefixLength = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($prefixLength)
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "$relativePath|$hash"
        } | Sort-Object)
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window.'
}
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Package file not found: $path" }
}
if (-not $installRoot.StartsWith(
    $allowedProgramFilesRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to install outside Program Files: $installRoot"
}
if (-not $dataRoot.StartsWith(
    $allowedProgramDataRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create machine state outside ProgramData: $dataRoot"
}

$serviceBinary = Join-Path $installRoot 'FocusGuard.Service.exe'
$existing = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    $existingPath = $existing.PathName.Trim('"')
    if (-not [IO.Path]::GetFullPath($existingPath).Equals($serviceBinary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify $serviceName because it points outside the FocusGuard installation root."
    }
}

if ($PSCmdlet.ShouldProcess($installRoot, 'Stage FocusGuard binaries and register service')) {
    $installExisted = Test-Path -LiteralPath $installRoot -PathType Container
    $dataExisted = Test-Path -LiteralPath $dataRoot -PathType Container
    $installAcl = if ($installExisted) { Get-Acl -LiteralPath $installRoot } else { $null }
    $dataAcl = if ($dataExisted) { Get-Acl -LiteralPath $dataRoot } else { $null }
    $existingServiceWasRunning = $null -ne $existing -and $existing.State -eq 'Running'
    $serviceCreated = $false
    $backupDirectory = $null
    $backupFingerprint = @()
    $agentWasRunning = $false
    $previousHostRegistrations = @{}
    $shortcutCreated = $false
    foreach ($registryPath in @($chromeHostRegistry, $edgeHostRegistry)) {
        $previousHostRegistrations[$registryPath] = if (Test-Path -LiteralPath $registryPath) {
            (Get-Item -LiteralPath $registryPath).GetValue('')
        } else { $null }
    }

    try {
        $desktopProcesses = @(Get-Process -Name 'FocusGuard.Desktop' -ErrorAction SilentlyContinue |
            Where-Object {
                try {
                    $null -ne $_.Path -and
                        [IO.Path]::GetFullPath($_.Path).Equals(
                            (Join-Path $installRoot 'FocusGuard.Desktop.exe'),
                            [StringComparison]::OrdinalIgnoreCase)
                } catch { $false }
            })
        foreach ($desktopProcess in $desktopProcesses) {
            Write-Output "Closing FocusGuard Desktop process $($desktopProcess.Id) before update."
            $null = $desktopProcess.CloseMainWindow()
        }

        $agentProcesses = @(Get-Process -Name 'FocusGuard.ProcessAgent' -ErrorAction SilentlyContinue |
            Where-Object {
                try {
                    $null -ne $_.Path -and
                        [IO.Path]::GetFullPath($_.Path).Equals(
                            (Join-Path $installRoot 'FocusGuard.ProcessAgent.exe'),
                            [StringComparison]::OrdinalIgnoreCase)
                } catch { $false }
            })
        $agentWasRunning = $agentProcesses.Count -gt 0
        foreach ($agentProcess in $agentProcesses) {
            Write-Output "Stopping FocusGuard ProcessAgent process $($agentProcess.Id) before update."
            Stop-Process -Id $agentProcess.Id
            $agentProcess.WaitForExit(15000)
            if (-not $agentProcess.HasExited) {
                throw "FocusGuard ProcessAgent process $($agentProcess.Id) did not stop; no files were replaced."
            }
        }

        # Chrome and Edge keep this process alive across native-messaging connections, so
        # it can still be running (and holding its own runtime DLLs open) even though
        # nothing FocusGuard-branded is visibly on screen. Stopping it here, the same way
        # ProcessAgent is stopped above, is what actually fixed the "clrjit.dll is being
        # used by another process" failure -- the browser simply restarts the host the
        # next time it needs it.
        $nativeMessagingProcesses = @(Get-Process -Name 'FocusGuard.NativeMessaging' -ErrorAction SilentlyContinue |
            Where-Object {
                try {
                    $null -ne $_.Path -and
                        [IO.Path]::GetFullPath($_.Path).Equals(
                            (Join-Path $installRoot 'FocusGuard.NativeMessaging.exe'),
                            [StringComparison]::OrdinalIgnoreCase)
                } catch { $false }
            })
        foreach ($nativeMessagingProcess in $nativeMessagingProcesses) {
            Write-Output "Stopping FocusGuard NativeMessaging process $($nativeMessagingProcess.Id) before update."
            Stop-Process -Id $nativeMessagingProcess.Id
            $nativeMessagingProcess.WaitForExit(15000)
            if (-not $nativeMessagingProcess.HasExited) {
                throw "FocusGuard NativeMessaging process $($nativeMessagingProcess.Id) did not stop; no files were replaced."
            }
        }
        foreach ($desktopProcess in $desktopProcesses) {
            try { $desktopProcess.WaitForExit(2000) } catch { }
            if (-not $desktopProcess.HasExited) {
                Write-Output "FocusGuard Desktop minimized to the notification area; stopping its UI process for the update."
                Stop-Process -Id $desktopProcess.Id
                try { $desktopProcess.WaitForExit(15000) } catch { }
            }
            if (-not $desktopProcess.HasExited) {
                throw "FocusGuard Desktop process $($desktopProcess.Id) did not close; no files were replaced."
            }
        }

        if ($installExisted) {
            $backupDirectory = Join-Path ([IO.Path]::GetTempPath()) "FocusGuard-install-backup-$([Guid]::NewGuid().ToString('N'))"
            New-Item -ItemType Directory -Path $backupDirectory | Out-Null
            Get-ChildItem -LiteralPath $installRoot -Force |
                Copy-Item -Destination $backupDirectory -Recurse -Force
            $backupFingerprint = Get-FocusGuardTreeFingerprint $backupDirectory
        }

        if ($existingServiceWasRunning) {
            Stop-Service -Name $serviceName
            (Get-Service -Name $serviceName).WaitForStatus(
                [ServiceProcess.ServiceControllerStatus]::Stopped,
                [TimeSpan]::FromSeconds(15))
        }

        New-Item -ItemType Directory -Path $installRoot, $dataRoot -Force | Out-Null
        foreach ($component in @('FocusGuard.Service', 'FocusGuard.Recovery', 'FocusGuard.Desktop', 'FocusGuard.ProcessAgent', 'FocusGuard.NativeMessaging')) {
            Get-ChildItem -LiteralPath (Join-Path $packageRoot "publish\$component") -Force |
                Copy-Item -Destination $installRoot -Recurse -Force
        }
        $installedNativeManifests = Join-Path $installRoot 'native-messaging'
        $installedBrowserPackages = Join-Path $installRoot 'browser-extension'
        New-Item -ItemType Directory -Path $installedNativeManifests, $installedBrowserPackages -Force | Out-Null
        Copy-Item -Path (Join-Path $packageRoot 'native-messaging\*') -Destination $installedNativeManifests -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $installedNativeManifests 'focusguard.native.json') `
            -Destination (Join-Path $installRoot 'focusguard.native.json') -Force
        Copy-Item -Path (Join-Path $packageRoot 'browser-extension\*') -Destination $installedBrowserPackages -Recurse -Force
        New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
        if (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf) {
            $existingShortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($startMenuShortcut)
            if (-not [IO.Path]::GetFullPath($existingShortcut.TargetPath).Equals(
                (Join-Path $installRoot 'FocusGuard.Desktop.exe'),
                [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to overwrite an unrelated Start Menu shortcut: $startMenuShortcut"
            }
        } else {
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $shell.CreateShortcut($startMenuShortcut)
            $shortcut.TargetPath = Join-Path $installRoot 'FocusGuard.Desktop.exe'
            $shortcut.WorkingDirectory = $installRoot
            $shortcut.IconLocation = Join-Path $installRoot 'FocusGuard.Setup.exe'
            $shortcut.Description = 'Open FocusGuard'
            $shortcut.Save()
            $shortcutCreated = $true
        }
        New-Item -Path $chromeHostRegistry -Force | Out-Null
        New-Item -Path $edgeHostRegistry -Force | Out-Null
        Set-Item -LiteralPath $chromeHostRegistry -Value (Join-Path $installedNativeManifests 'chrome.json')
        Set-Item -LiteralPath $edgeHostRegistry -Value (Join-Path $installedNativeManifests 'edge.json')
        $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        New-Item -Path $runKey -Force | Out-Null
        Set-ItemProperty -Path $runKey -Name 'FocusGuard.ProcessAgent' -Value ('"' + (Join-Path $installRoot 'FocusGuard.ProcessAgent.exe') + '"')
        Set-ItemProperty -Path $runKey -Name 'FocusGuard.Desktop' -Value ('"' + (Join-Path $installRoot 'FocusGuard.Desktop.exe') + '"')

        if ($InjectFailureAfterCopyForRollbackTest) {
            throw 'Injected rollback verification failure after binary replacement.'
        }

        & icacls.exe $installRoot /inheritance:r /grant:r `
            '*S-1-5-18:(OI)(CI)(F)' `
            '*S-1-5-32-544:(OI)(CI)(F)' `
            '*S-1-5-19:(OI)(CI)(RX)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Installation ACL provisioning failed with exit code $LASTEXITCODE." }

        & icacls.exe $dataRoot /inheritance:r /grant:r `
            '*S-1-5-18:(OI)(CI)(F)' `
            '*S-1-5-32-544:(OI)(CI)(F)' `
            '*S-1-5-19:(OI)(CI)(M)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Machine-state ACL provisioning failed with exit code $LASTEXITCODE." }

        if ($null -eq $existing) {
            & sc.exe create $serviceName binPath= "`"$serviceBinary`"" start= auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'FocusGuard Service'
            if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE." }
            $serviceCreated = $true
            & sc.exe description $serviceName 'Authoritative FocusGuard policy service.' | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "sc.exe description failed with exit code $LASTEXITCODE." }
        }

        Start-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromSeconds(15))
        Start-Sleep -Seconds 2
        $startedService = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
        if ($startedService.State -ne 'Running' -or $startedService.ProcessId -le 0) {
            throw 'FocusGuard service did not remain running after startup verification.'
        }
        Start-Process -FilePath (Join-Path $installRoot 'FocusGuard.ProcessAgent.exe') -WindowStyle Hidden
    }
    catch {
        $installationError = $_
        Write-Warning "Installation failed; restoring the previous FocusGuard state."
        try {
            Get-Process -Name 'FocusGuard.ProcessAgent' -ErrorAction SilentlyContinue |
                Where-Object {
                    try {
                        $null -ne $_.Path -and
                            [IO.Path]::GetFullPath($_.Path).Equals(
                                (Join-Path $installRoot 'FocusGuard.ProcessAgent.exe'),
                                [StringComparison]::OrdinalIgnoreCase)
                    } catch { $false }
                } |
                Stop-Process -ErrorAction Stop
            $currentService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if ($null -ne $currentService -and
                $currentService.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
                Stop-Service -Name $serviceName -Force
                $currentService.WaitForStatus(
                    [ServiceProcess.ServiceControllerStatus]::Stopped,
                    [TimeSpan]::FromSeconds(15))
            }

            if ($serviceCreated) {
                & sc.exe delete $serviceName | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "Rollback service deletion failed with exit code $LASTEXITCODE." }
            }

            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force
            }
            if ($installExisted) {
                New-Item -ItemType Directory -Path $installRoot | Out-Null
                Get-ChildItem -LiteralPath $backupDirectory -Force |
                    Copy-Item -Destination $installRoot -Recurse -Force
                Set-Acl -LiteralPath $installRoot -AclObject $installAcl
                $restoredFingerprint = Get-FocusGuardTreeFingerprint $installRoot
                if (@(Compare-Object $backupFingerprint $restoredFingerprint).Count -ne 0) {
                    throw 'Rollback verification failed: restored installation files do not match the backup.'
                }
            }

            if ($dataExisted) {
                Set-Acl -LiteralPath $dataRoot -AclObject $dataAcl
            } elseif (Test-Path -LiteralPath $dataRoot) {
                Remove-Item -LiteralPath $dataRoot -Recurse -Force
            }

            foreach ($registryPath in @($chromeHostRegistry, $edgeHostRegistry)) {
                $previousValue = $previousHostRegistrations[$registryPath]
                if ($null -eq $previousValue) {
                    Remove-Item -LiteralPath $registryPath -Recurse -Force -ErrorAction SilentlyContinue
                } else {
                    New-Item -Path $registryPath -Force | Out-Null
                    Set-Item -LiteralPath $registryPath -Value $previousValue
                }
            }
            if ($shortcutCreated -and (Test-Path -LiteralPath $startMenuShortcut)) {
                Remove-Item -LiteralPath $startMenuShortcut -Force
            }

            if ($null -ne $existing -and $existingServiceWasRunning) {
                Start-Service -Name $serviceName
                (Get-Service -Name $serviceName).WaitForStatus(
                    [ServiceProcess.ServiceControllerStatus]::Running,
                    [TimeSpan]::FromSeconds(15))
            }
            if ($agentWasRunning -and $installExisted) {
                Start-Process -FilePath (Join-Path $installRoot 'FocusGuard.ProcessAgent.exe') -WindowStyle Hidden
            }
        }
        catch {
            throw "Installation failed: $($installationError.Exception.Message) Rollback also failed: $($_.Exception.Message)"
        }

        throw $installationError
    }
    finally {
        if ($null -ne $backupDirectory -and (Test-Path -LiteralPath $backupDirectory)) {
            $resolvedBackup = [IO.Path]::GetFullPath($backupDirectory)
            $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
            if (-not $resolvedBackup.StartsWith(
                $temporaryRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove unexpected backup path: $resolvedBackup"
            }
            Remove-Item -LiteralPath $resolvedBackup -Recurse -Force
        }
    }
}

Get-CimInstance Win32_Service -Filter "Name='$serviceName'" | Select-Object Name, State, StartMode, StartName, PathName
