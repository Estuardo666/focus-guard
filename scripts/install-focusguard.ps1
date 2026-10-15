[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\artifacts\installer\FocusGuard-0.1.0'),
    [string]$OwnerSid,
    [switch]$ClientMetadataQueryConsent,
    [switch]$InjectFailureAfterCopyForRollbackTest
)

$ErrorActionPreference = 'Stop'
$serviceName = 'FocusGuard'
$installRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramFiles} 'FocusGuard'))
$dataRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramData} 'FocusGuard'))
$allowedProgramFilesRoot = [IO.Path]::GetFullPath(${env:ProgramFiles})
$allowedProgramDataRoot = [IO.Path]::GetFullPath(${env:ProgramData})
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)

function Get-FocusGuardBrowserPackageEntries([string]$PackageDirectory) {
    $setupManifest = Get-Content -LiteralPath (Join-Path $PackageDirectory 'manifest.json') -Raw | ConvertFrom-Json
    $distributionPath = Join-Path $PackageDirectory 'browser-extension\distribution.json'
    $distribution = Get-Content -LiteralPath $distributionPath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$setupManifest.Version) -or
        [string]$distribution.version -cne [string]$setupManifest.Version) {
        throw 'Browser extension distribution version does not match the Setup package.'
    }

    $packages = @($distribution.packages)
    if ($packages.Count -ne 2) {
        throw 'Browser extension distribution must contain exactly Chrome and Edge packages.'
    }

    $extensionRoot = [IO.Path]::GetFullPath((Join-Path $PackageDirectory 'browser-extension')) +
        [IO.Path]::DirectorySeparatorChar
    $seenBrowsers = @{}
    $entries = @()
    foreach ($browserPackage in $packages) {
        $browser = [string]$browserPackage.browser
        if ($browser -notin @('chrome', 'edge') -or $seenBrowsers.ContainsKey($browser)) {
            throw 'Browser extension distribution has an unknown or duplicate browser.'
        }
        $expectedFile = 'FocusGuard-{0}-{1}.zip' -f $setupManifest.Version, $browser
        $file = [string]$browserPackage.file
        if (-not $file.Equals($expectedFile, [StringComparison]::Ordinal) -or
            -not [IO.Path]::GetFileName($file).Equals($file, [StringComparison]::Ordinal)) {
            throw "Browser extension archive name is not valid for $browser."
        }
        $expectedHash = [string]$browserPackage.sha256
        if ($expectedHash -notmatch '^[0-9a-fA-F]{64}$') {
            throw "Browser extension archive hash is invalid for $browser."
        }

        $source = [IO.Path]::GetFullPath((Join-Path $extensionRoot $file))
        if (-not $source.StartsWith($extensionRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Browser extension archive is missing or outside its package directory: $file"
        }
        $actualHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        if (-not $actualHash.Equals($expectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Browser extension archive does not match its distribution hash: $file"
        }

        $seenBrowsers[$browser] = $true
        $entries += [pscustomobject]@{
            Browser = $browser
            File = $file
            Source = $source
        }
    }

    if (-not $seenBrowsers.ContainsKey('chrome') -or -not $seenBrowsers.ContainsKey('edge')) {
        throw 'Browser extension distribution must contain both Chrome and Edge.'
    }
    return $entries
}

$browserPackageEntries = @(Get-FocusGuardBrowserPackageEntries -PackageDirectory $packageRoot)
$required = @(
    (Join-Path $packageRoot 'publish\FocusGuard.Service\FocusGuard.Service.exe'),
    (Join-Path $packageRoot 'publish\FocusGuard.Recovery\FocusGuard.Recovery.exe'),
    (Join-Path $packageRoot 'publish\FocusGuard.ProcessAgent\FocusGuard.ProcessAgent.exe'),
    (Join-Path $packageRoot 'publish\FocusGuard.NativeMessaging\FocusGuard.NativeMessaging.exe'),
    (Join-Path $packageRoot 'publish\FocusGuard.Host.Windows\FocusGuard.UI.exe'),
    (Join-Path $packageRoot 'publish\FocusGuard.Host.Windows\FocusGuard.UI.dll'),
    (Join-Path $packageRoot 'publish\FocusGuard.Host.Windows\ui\index.html'),
    (Join-Path $packageRoot 'native-messaging\chrome.json'),
    (Join-Path $packageRoot 'native-messaging\edge.json'),
    (Join-Path $packageRoot 'native-messaging\focusguard.native.json'),
    (Join-Path $packageRoot 'browser-extension\distribution.json'),
    (Join-Path $packageRoot 'manifest.json')
)
$required += @($browserPackageEntries | ForEach-Object { $_.Source })
$policyHelper = Join-Path $packageRoot 'New-FocusGuardIpcPolicy.ps1'
$ownerHelper = Join-Path $packageRoot 'Get-FocusGuardInteractiveOwnerSid.ps1'
if (-not (Test-Path -LiteralPath $policyHelper) -or -not (Test-Path -LiteralPath $ownerHelper)) { throw 'IPC provisioning helpers missing.' }
if ([string]::IsNullOrWhiteSpace($OwnerSid)) { $OwnerSid = (& $ownerHelper).Trim() }
$ownerIdentity = [Security.Principal.SecurityIdentifier]::new($OwnerSid)
if (-not $ClientMetadataQueryConsent) { throw 'Explicit readonly client metadata consent is required for authenticated IPC installation.' }
if (-not (Test-Path -LiteralPath ('Registry::HKEY_USERS\' + $ownerIdentity.Value))) { throw 'The explicit interactive owner hive is not loaded.' }
$ownerHive = 'Registry::HKEY_USERS\' + $ownerIdentity.Value
$chromeHostRegistry = $ownerHive + '\Software\Google\Chrome\NativeMessagingHosts\com.focusguard.native'
$edgeHostRegistry = $ownerHive + '\Software\Microsoft\Edge\NativeMessagingHosts\com.focusguard.native'
$runKey = $ownerHive + '\Software\Microsoft\Windows\CurrentVersion\Run'
# The sign-in value keeps its historical name so an existing on/off choice carries over; it now starts the React host.
$desktopRunName = 'FocusGuard.Desktop'
$uiExecutable = 'FocusGuard.UI.exe'
$agentRunName = 'FocusGuard.ProcessAgent'
$startMenuDirectory = Join-Path ${env:ProgramData} 'Microsoft\Windows\Start Menu\Programs'
$startMenuShortcut = Join-Path $startMenuDirectory 'FocusGuard.lnk'
$interactiveShellTypeDefinition = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public sealed class FocusGuardProcessTokenFacts
{
    public bool IsElevated;
    public int IntegrityLevelRid;
    public int SessionId;
}

public static class FocusGuardInteractiveShell
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenSessionId = 12;
    private const int TokenElevation = 20;
    private const int TokenIntegrityLevel = 25;

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    public static bool HasInteractiveDesktop()
    {
        return GetShellWindow() != IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr token, int informationClass, IntPtr information, int informationLength, out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

    public static int GetDesktopProcessId()
    {
        IntPtr desktop = GetShellWindow();
        uint processId;
        if (desktop == IntPtr.Zero || GetWindowThreadProcessId(desktop, out processId) == 0 || processId == 0)
            throw new InvalidOperationException("The interactive desktop process could not be identified.");
        return checked((int)processId);
    }

    private static void GetDesktopShellObjects(out object shellWindowsObject, out object desktopDispatch)
    {
        shellWindowsObject = null;
        desktopDispatch = null;
        try
        {
            Type shellWindowsType = Type.GetTypeFromCLSID(
                new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true);
            shellWindowsObject = Activator.CreateInstance(shellWindowsType);
            dynamic shellWindows = shellWindowsObject;
            object location = Type.Missing;
            object locationRoot = Type.Missing;
            int desktopWindow;
            desktopDispatch = shellWindows.FindWindowSW(location, locationRoot, 8, out desktopWindow, 1);

            IntPtr expectedDesktopWindow = GetShellWindow();
            if (desktopDispatch == null || expectedDesktopWindow == IntPtr.Zero ||
                desktopWindow != expectedDesktopWindow.ToInt32())
                throw new InvalidOperationException("The Shell did not return this session's desktop dispatcher.");
        }
        catch
        {
            if (desktopDispatch != null && Marshal.IsComObject(desktopDispatch))
                Marshal.FinalReleaseComObject(desktopDispatch);
            if (shellWindowsObject != null && Marshal.IsComObject(shellWindowsObject))
                Marshal.FinalReleaseComObject(shellWindowsObject);
            desktopDispatch = null;
            shellWindowsObject = null;
            throw;
        }
    }

    public static bool ValidateDesktopShellDispatcher()
    {
        object shellWindowsObject = null;
        object desktopDispatch = null;
        object documentObject = null;
        object applicationObject = null;
        try
        {
            GetDesktopShellObjects(out shellWindowsObject, out desktopDispatch);
            dynamic desktop = desktopDispatch;
            documentObject = desktop.Document;
            dynamic document = documentObject;
            applicationObject = document.Application;
            return applicationObject != null;
        }
        finally
        {
            if (applicationObject != null && Marshal.IsComObject(applicationObject))
                Marshal.FinalReleaseComObject(applicationObject);
            if (documentObject != null && Marshal.IsComObject(documentObject))
                Marshal.FinalReleaseComObject(documentObject);
            if (desktopDispatch != null && Marshal.IsComObject(desktopDispatch))
                Marshal.FinalReleaseComObject(desktopDispatch);
            if (shellWindowsObject != null && Marshal.IsComObject(shellWindowsObject))
                Marshal.FinalReleaseComObject(shellWindowsObject);
        }
    }

    public static void ExecuteFromDesktop(string executablePath, string arguments, string workingDirectory)
    {
        object shellWindowsObject = null;
        object desktopDispatch = null;
        object documentObject = null;
        object applicationObject = null;
        try
        {
            GetDesktopShellObjects(out shellWindowsObject, out desktopDispatch);
            dynamic desktop = desktopDispatch;
            documentObject = desktop.Document;
            dynamic document = documentObject;
            applicationObject = document.Application;
            dynamic application = applicationObject;
            // --minimized controls the tray-only startup in the app. SW_HIDE would swallow its first requested Show.
            application.ShellExecute(executablePath, arguments, workingDirectory, "open", 1);
        }
        finally
        {
            if (applicationObject != null && Marshal.IsComObject(applicationObject))
                Marshal.FinalReleaseComObject(applicationObject);
            if (documentObject != null && Marshal.IsComObject(documentObject))
                Marshal.FinalReleaseComObject(documentObject);
            if (desktopDispatch != null && Marshal.IsComObject(desktopDispatch))
                Marshal.FinalReleaseComObject(desktopDispatch);
            if (shellWindowsObject != null && Marshal.IsComObject(shellWindowsObject))
                Marshal.FinalReleaseComObject(shellWindowsObject);
        }
    }

    public static FocusGuardProcessTokenFacts GetProcessTokenFacts(int processId)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, checked((uint)processId));
        if (process == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect the interactive process.");

        IntPtr token = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out token))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect the interactive process token.");

            int returned;
            buffer = Marshal.AllocHGlobal(sizeof(int));
            if (!GetTokenInformation(token, TokenElevation, buffer, sizeof(int), out returned))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect process elevation.");
            bool elevated = Marshal.ReadInt32(buffer) != 0;

            if (!GetTokenInformation(token, TokenSessionId, buffer, sizeof(int), out returned))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect process session.");
            int sessionId = Marshal.ReadInt32(buffer);
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;

            int requiredLength;
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out requiredLength);
            if (requiredLength <= 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not size process integrity information.");
            buffer = Marshal.AllocHGlobal(requiredLength);
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, requiredLength, out returned))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect process integrity.");

            IntPtr sid = Marshal.ReadIntPtr(buffer);
            IntPtr subAuthorityCount = GetSidSubAuthorityCount(sid);
            if (subAuthorityCount == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect process integrity SID.");
            byte count = Marshal.ReadByte(subAuthorityCount);
            if (count == 0)
                throw new InvalidOperationException("Process integrity SID is empty.");
            IntPtr integrityRid = GetSidSubAuthority(sid, (uint)(count - 1));
            if (integrityRid == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read process integrity level.");

            return new FocusGuardProcessTokenFacts {
                IsElevated = elevated,
                IntegrityLevelRid = Marshal.ReadInt32(integrityRid),
                SessionId = sessionId
            };
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) CloseHandle(token);
            CloseHandle(process);
        }
    }
}
'@
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    Add-Type -TypeDefinition $interactiveShellTypeDefinition -Language CSharp `
        -ReferencedAssemblies @('Microsoft.CSharp') -ErrorAction Stop
} else {
    Add-Type -TypeDefinition $interactiveShellTypeDefinition -Language CSharp -ErrorAction Stop
}

function Get-FocusGuardProcessOwnerSid([int]$ProcessId) {
    $processRecord = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
    if ($null -eq $processRecord) { throw 'Interactive process disappeared before its owner could be verified.' }
    $owner = Invoke-CimMethod -InputObject $processRecord -MethodName GetOwnerSid -ErrorAction Stop
    if ($owner.ReturnValue -ne 0 -or [string]::IsNullOrWhiteSpace([string]$owner.Sid)) {
        throw 'Interactive process owner could not be verified.'
    }
    return [string]$owner.Sid
}

function Get-FocusGuardInteractiveSessionId {
    $desktopProcessId = [FocusGuardInteractiveShell]::GetDesktopProcessId()
    $desktop = Get-Process -Id $desktopProcessId -ErrorAction Stop
    if ($desktop.SessionId -ne [Diagnostics.Process]::GetCurrentProcess().SessionId -or
        (Get-FocusGuardProcessOwnerSid $desktopProcessId) -cne $ownerIdentity.Value) {
        throw 'The interactive desktop does not belong to the configured owner session.'
    }
    $token = [FocusGuardInteractiveShell]::GetProcessTokenFacts($desktopProcessId)
    if ($token.SessionId -ne $desktop.SessionId -or $token.IsElevated -or $token.IntegrityLevelRid -ne 8192) {
        throw 'The interactive desktop is not running with the configured owner medium-integrity token.'
    }
    return [int]$desktop.SessionId
}

function Assert-FocusGuardInteractiveProcessContext(
    [Diagnostics.Process]$Process,
    [string]$ExpectedPath,
    [int]$ExpectedSessionId,
    [string]$ProcessName) {
    try {
        if ($null -eq $Process.Path -or
            -not [IO.Path]::GetFullPath($Process.Path).Equals(
                [IO.Path]::GetFullPath($ExpectedPath), [StringComparison]::OrdinalIgnoreCase) -or
            $Process.SessionId -ne $ExpectedSessionId -or
            (Get-FocusGuardProcessOwnerSid $Process.Id) -cne $ownerIdentity.Value) {
            throw 'Owner, session, or executable path mismatch.'
        }
        $token = [FocusGuardInteractiveShell]::GetProcessTokenFacts($Process.Id)
        if ($token.SessionId -ne $ExpectedSessionId -or $token.IsElevated -or $token.IntegrityLevelRid -ne 8192) {
            throw 'Token elevation, integrity, or session mismatch.'
        }
    }
    catch {
        throw "$ProcessName did not start as the configured interactive owner with a medium-integrity token. Installation will roll back."
    }
}

function Start-FocusGuardInteractiveProcess(
    [string]$ExecutablePath,
    [string]$ProcessName,
    [string]$Arguments = '') {
    # Resolve IShellWindows' actual desktop dispatcher. Creating Shell.Application
    # in this elevated PowerShell process does not prove ShellExecute runs as Explorer.
    $expectedSessionId = Get-FocusGuardInteractiveSessionId
    $launchStartedAt = [DateTime]::UtcNow
    [FocusGuardInteractiveShell]::ExecuteFromDesktop($ExecutablePath, $Arguments, $installRoot)

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $started = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
            Where-Object {
                try {
                    $null -ne $_.Path -and
                        [IO.Path]::GetFullPath($_.Path).Equals(
                            $ExecutablePath,
                            [StringComparison]::OrdinalIgnoreCase) -and
                        $_.StartTime.ToUniversalTime() -ge $launchStartedAt.AddSeconds(-1)
                } catch { $false }
            })
        foreach ($process in $started) {
            Assert-FocusGuardInteractiveProcessContext `
                -Process $process `
                -ExpectedPath $ExecutablePath `
                -ExpectedSessionId $expectedSessionId `
                -ProcessName $ProcessName
        }
    } while ($started.Count -eq 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($started.Count -ne 1) {
        throw "$ProcessName startup verification expected one new process; found $($started.Count)."
    }
}

function Stop-FocusGuardRuntimeProcesses {
    # Closes every FocusGuard-branded process still holding files under $installRoot
    # open. Returns whether anything was found running, so the caller can tell "clean
    # already" apart from "just cleaned it."
    $foundAny = $false

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
        $foundAny = $true
        Write-Output "Closing FocusGuard Desktop process $($desktopProcess.Id) before update."
        $null = $desktopProcess.CloseMainWindow()
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

    # Earlier versions installed the Avalonia Desktop app, which is closed above for upgrades. The UI host supervises
    # ProcessAgent: finish closing it before enumerating agents so it cannot replace one after this sweep has passed.
    $hostProcesses = @(Get-Process -Name 'FocusGuard.UI' -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $null -ne $_.Path -and [IO.Path]::GetFullPath($_.Path).Equals(
                    (Join-Path $installRoot 'FocusGuard.UI.exe'), [StringComparison]::OrdinalIgnoreCase)
            } catch { $false }
        })
    foreach ($hostProcess in $hostProcesses) {
        $foundAny = $true
        Write-Output "Closing FocusGuard UI process $($hostProcess.Id) before update."
        $null = $hostProcess.CloseMainWindow()
    }
    foreach ($hostProcess in $hostProcesses) {
        try { $hostProcess.WaitForExit(2000) } catch { }
        if (-not $hostProcess.HasExited) {
            # Closing the window only hides it to the notification area; the service keeps any session running.
            Write-Output "FocusGuard UI is in the notification area; stopping its UI process for the update."
            Stop-Process -Id $hostProcess.Id
            try { $hostProcess.WaitForExit(15000) } catch { }
        }
        if (-not $hostProcess.HasExited) {
            throw "FocusGuard UI process $($hostProcess.Id) did not close; no files were replaced."
        }
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
    foreach ($agentProcess in $agentProcesses) {
        $foundAny = $true
        Write-Output "Stopping FocusGuard ProcessAgent process $($agentProcess.Id) before update."
        Stop-Process -Id $agentProcess.Id
        $agentProcess.WaitForExit(15000)
        if (-not $agentProcess.HasExited) {
            throw "FocusGuard ProcessAgent process $($agentProcess.Id) did not stop; no files were replaced."
        }
    }

    # Chrome and Edge keep this process alive across native-messaging connections.
    # The caller suspends its registry registration before this sweep, preventing
    # Chromium from replacing the process while the installed files are changed.
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
        $foundAny = $true
        Write-Output "Stopping FocusGuard NativeMessaging process $($nativeMessagingProcess.Id) before update."
        Stop-Process -Id $nativeMessagingProcess.Id
        $nativeMessagingProcess.WaitForExit(15000)
        if (-not $nativeMessagingProcess.HasExited) {
            throw "FocusGuard NativeMessaging process $($nativeMessagingProcess.Id) did not stop; no files were replaced."
        }
    }
    return $foundAny
}

function Suspend-FocusGuardNativeHostRegistration {
    $removedAny = $false
    foreach ($registryPath in @($chromeHostRegistry, $edgeHostRegistry)) {
        if (Test-Path -LiteralPath $registryPath) {
            Remove-Item -LiteralPath $registryPath -Recurse -Force
            $removedAny = $true
        }
    }
    if ($removedAny) {
        Write-Output 'Suspending FocusGuard browser native-host registration during update.'
    }
}

function Protect-FocusGuardInstallationTree {
    $root = [IO.Path]::GetFullPath($installRoot)
    if (-not $root.Equals((Join-Path $allowedProgramFilesRoot 'FocusGuard'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Protected installation ACL target denied.'
    }
    $entries = @((Get-Item -LiteralPath $root)) + @(Get-ChildItem -LiteralPath $root -Force -Recurse)
    foreach ($entry in $entries) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Installation reparse entry denied.' }
        $acl = if ($entry.PSIsContainer) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
        $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($permission in @(@('S-1-5-18','FullControl'),@('S-1-5-32-544','FullControl'),@('S-1-5-32-545','ReadAndExecute'),@('S-1-5-19','ReadAndExecute'))) {
            $inherit = if ($entry.PSIsContainer) { [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit' } else { [Security.AccessControl.InheritanceFlags]::None }
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                [Security.Principal.SecurityIdentifier]::new($permission[0]), [Security.AccessControl.FileSystemRights]$permission[1],
                $inherit, [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
        }
        Set-Acl -LiteralPath $entry.FullName -AclObject $acl
    }
}

function Copy-FocusGuardComponentsWithRetry {
    # A browser can respawn FocusGuard.NativeMessaging.exe (or the desktop shortcut
    # can relaunch FocusGuard.UI.exe) in the gap between the process sweep above
    # and this copy actually running -- that race is what used to make the first
    # install attempt fail with a locked-file error and force a second, separate
    # "Instalar" click. Retrying the sweep-then-copy pair in one place means that
    # race is absorbed automatically instead of surfacing as a failed install.
    $maxAttempts = 4
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        try {
            foreach ($component in @('FocusGuard.Service', 'FocusGuard.Recovery', 'FocusGuard.ProcessAgent', 'FocusGuard.NativeMessaging', 'FocusGuard.Host.Windows', 'FocusGuard.DrillClient')) {
                Get-ChildItem -LiteralPath (Join-Path $packageRoot "publish\$component") -Force |
                    Copy-Item -Destination $installRoot -Recurse -Force
            }
            return
        }
        catch [IO.IOException] {
            if ($attempt -eq $maxAttempts) { throw }
            Write-Warning "File copy blocked by a running process (attempt $attempt of $maxAttempts); closing it and retrying."
            Start-Sleep -Milliseconds 500
            $null = Stop-FocusGuardRuntimeProcesses
            Start-Sleep -Milliseconds 500
        }
    }
}

function Remove-FocusGuardLegacyDesktopEntry {
    # The Avalonia app is no longer shipped. Remove only its own entry files so nothing can start it again; the backup
    # taken above still holds them for rollback.
    foreach ($legacy in @(Get-ChildItem -LiteralPath $installRoot -File -Force -Filter 'FocusGuard.Desktop.*')) {
        if (($legacy.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Legacy Desktop reparse entry denied.' }
        Remove-Item -LiteralPath $legacy.FullName -Force
    }
}

function Assert-FocusGuardInstalledPayload {
    $comparisons = @()
    $browserPackageEntries = @(Get-FocusGuardBrowserPackageEntries -PackageDirectory $packageRoot)
    foreach ($component in @('FocusGuard.Service', 'FocusGuard.Recovery', 'FocusGuard.ProcessAgent', 'FocusGuard.NativeMessaging', 'FocusGuard.Host.Windows', 'FocusGuard.DrillClient')) {
        $sourceDirectory = Join-Path $packageRoot "publish\$component"
        $comparisons += @(Get-ChildItem -LiteralPath $sourceDirectory -File -Force |
            Where-Object { $_.Name.StartsWith($(if ($component -eq 'FocusGuard.Host.Windows') { 'FocusGuard.UI.' } else { $component + '.' }), [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object {
                [pscustomobject]@{ Source = $_.FullName; Destination = Join-Path $installRoot $_.Name }
            })
    }
    $hostSourcePrefix = [IO.Path]::GetFullPath((Join-Path $packageRoot 'publish\FocusGuard.Host.Windows')).TrimEnd('\') + '\'
    foreach ($hostAsset in @(Get-ChildItem -LiteralPath (Join-Path $hostSourcePrefix 'ui') -File -Recurse)) {
        if (-not $hostAsset.FullName.StartsWith($hostSourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Host asset is outside its package directory.'
        }
        # Setup invokes Windows PowerShell 5.1, whose .NET Framework lacks GetRelativePath.
        $relative = $hostAsset.FullName.Substring($hostSourcePrefix.Length)
        $comparisons += [pscustomobject]@{ Source = $hostAsset.FullName; Destination = Join-Path $installRoot $relative }
    }
    foreach ($relativePath in @(
        'browser-extension\distribution.json',
        'browser-extension\staging\manifest.json',
        'browser-extension\staging\src\native-health.js',
        'browser-extension\staging\src\navigation-enforcement.js',
        'browser-extension\staging\src\policy-integrity.js',
        'native-messaging\chrome.json',
        'native-messaging\edge.json',
        'native-messaging\focusguard.native.json')) {
        $comparisons += [pscustomobject]@{
            Source = Join-Path $packageRoot $relativePath
            Destination = Join-Path $installRoot $relativePath
        }
    }

    foreach ($browserPackage in $browserPackageEntries) {
        $comparisons += [pscustomobject]@{
            Source = $browserPackage.Source
            Destination = Join-Path (Join-Path $installRoot 'browser-extension') $browserPackage.File
        }
    }
    foreach ($comparison in $comparisons) {
        if (-not (Test-Path -LiteralPath $comparison.Destination -PathType Leaf)) {
            throw "Installed payload verification failed; missing $($comparison.Destination)."
        }
        $expected = (Get-FileHash -LiteralPath $comparison.Source -Algorithm SHA256).Hash
        $actual = (Get-FileHash -LiteralPath $comparison.Destination -Algorithm SHA256).Hash
        if (-not $expected.Equals($actual, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Installed payload verification failed; stale file $($comparison.Destination)."
        }
    }
}

function Wait-FocusGuardSingleInteractiveProcess(
    [string]$ExecutablePath,
    [string]$ProcessName) {
    $expectedSessionId = Get-FocusGuardInteractiveSessionId
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $running = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
            Where-Object {
                try {
                    $null -ne $_.Path -and
                        [IO.Path]::GetFullPath($_.Path).Equals(
                            $ExecutablePath,
                            [StringComparison]::OrdinalIgnoreCase)
                } catch { $false }
            })
    } while ($running.Count -ne 1 -and [DateTime]::UtcNow -lt $deadline)

    if ($running.Count -ne 1) {
        throw "$ProcessName startup verification expected exactly one process; found $($running.Count)."
    }
    Assert-FocusGuardInteractiveProcessContext `
        -Process $running[0] `
        -ExpectedPath $ExecutablePath `
        -ExpectedSessionId $expectedSessionId `
        -ProcessName $ProcessName
}

function Invoke-FocusGuardServiceHealthProbe {
    $binary = Join-Path $installRoot $uiExecutable
    $expectedSessionId = Get-FocusGuardInteractiveSessionId
    $startedAt = [DateTime]::UtcNow
    [FocusGuardInteractiveShell]::ExecuteFromDesktop(
        $binary, '--service-health-check --installer-owner-probe', $installRoot)
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $probe = $null
    while ($null -eq $probe -and [DateTime]::UtcNow -lt $deadline) {
        $probe = Get-Process -Name 'FocusGuard.UI' -ErrorAction SilentlyContinue | Where-Object {
            try {
                $_.StartTime.ToUniversalTime() -ge $startedAt.AddSeconds(-1) -and
                    [IO.Path]::GetFullPath($_.Path).Equals($binary, [StringComparison]::OrdinalIgnoreCase)
            } catch { $false }
        } | Select-Object -First 1
        if ($null -eq $probe) { Start-Sleep -Milliseconds 50 }
    }
    if ($null -eq $probe) { throw 'Owner-context health probe was not observed.' }
    Assert-FocusGuardInteractiveProcessContext `
        -Process $probe `
        -ExpectedPath $binary `
        -ExpectedSessionId $expectedSessionId `
        -ProcessName 'FocusGuard health probe'
    # Hold this exact process handle before it exits; no filename-based termination.
    $null = $probe.Handle
    if (-not $probe.WaitForExit(15000)) {
        throw 'FocusGuard authoritative IPC health probe timed out; inspect owner LocalAppData\FocusGuard\diagnostics\health-check.json and ProgramData\FocusGuard\diagnostics\ipc-control-rejection.json.'
    }
    return [int]$probe.ExitCode
}

function Wait-FocusGuardAuthenticatedReadiness(
    [scriptblock]$Probe,
    [scriptblock]$Wait = { Start-Sleep -Milliseconds 1000 }) {
    # Windows SCM Running precedes availability of background pipe listeners.
    # Each attempt must pass the complete owner-context authenticated probe.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $exitCode = & $Probe
        if ($exitCode -isnot [int]) { throw 'Authoritative IPC health probe returned an invalid result.' }
        if ($exitCode -eq 0) { return }
        if ($exitCode -notin @(12, 14) -or $attempt -eq 3) {
            throw "FocusGuard service failed the authoritative IPC health check (exit ${exitCode}: 11=bootstrap, 12=exchange, 14=not ready; attempt $attempt of 3). Inspect owner LocalAppData\FocusGuard\diagnostics\health-check.json and ProgramData\FocusGuard\diagnostics\ipc-control-rejection.json."
        }
        Write-Host "FocusGuard is starting; repeating the complete authenticated health check ($attempt of 3)."
        & $Wait
    }
}

function Assert-FocusGuardServiceResponsive {
    Wait-FocusGuardAuthenticatedReadiness -Probe { Invoke-FocusGuardServiceHealthProbe }
}

function Write-FocusGuardIpcRejectionDiagnostic {
    # Read only the fixed, size-bounded service diagnostic before rollback removes new machine state.
    $path = Join-Path $dataRoot 'diagnostics\ipc-control-rejection.json'
    try {
        $file = Get-Item -LiteralPath $path -ErrorAction Stop
        if ($file.Length -gt 4096 -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return }
        $record = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $allowedCodes = @('Unclassified', 'OwnerDenied', 'ClientDeploymentDenied', 'ConsentMissing',
            'CallerIdentityUnavailable', 'CallerTokenUnavailable', 'LogonSidUnavailable', 'ServerOwnerDenied',
            'RoleOrOperationDenied', 'ClientRootDenied', 'DeploymentOwnerDenied', 'DeploymentWriteGrantDenied',
            'ServiceIdentityDenied', 'ServiceImageDenied', 'ServiceChanged', 'ManifestDenied',
            'PolicyProcessUnavailable', 'PolicyOwnerScopeDenied', 'PolicyUntrustedDeployment',
            'PolicyImageDenied', 'PolicyOperationDenied')
        if ($record.Failure.Code -notin $allowedCodes) { return }
        $timestamp = ([DateTimeOffset]$record.TimestampUtc).ToUniversalTime().ToString('o')
        $nativeError = if ($null -eq $record.Failure.NativeError) { 'none' } else { [int]$record.Failure.NativeError }
        Write-Output "IPC control rejection: time=$timestamp code=$($record.Failure.Code) nativeError=$nativeError"
    }
    catch { # A missing or unreadable diagnostic must never prevent rollback.
    }
}

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
    if ($existing.StartName -ne 'NT AUTHORITY\LocalService') { throw 'Refusing to change the existing service identity.' }
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
    $originalTreeAcls = @{}
    $agentWasRunning = $false
    $previousHostRegistrations = @{}
    $previousRunRegistrations = @{}
    $shortcutCreated = $false
    foreach ($registryPath in @($chromeHostRegistry, $edgeHostRegistry)) {
        $previousHostRegistrations[$registryPath] = if (Test-Path -LiteralPath $registryPath) {
            (Get-Item -LiteralPath $registryPath).GetValue('')
        } else { $null }
    }
    foreach ($valueName in @($agentRunName, $desktopRunName)) {
        $previousRunRegistrations[$valueName] = if (Test-Path -LiteralPath $runKey) {
            (Get-ItemProperty -LiteralPath $runKey -Name $valueName -ErrorAction SilentlyContinue).$valueName
        } else { $null }
    }

    try {
        # Chromium reconnects the persistent native port one second after a host
        # disconnect. Remove the launch registration before stopping the host so
        # it cannot reacquire clrjit.dll between the sweep and Copy-Item.
        Suspend-FocusGuardNativeHostRegistration
        $agentWasRunning = Stop-FocusGuardRuntimeProcesses

        if ($installExisted) {
            foreach ($entry in @((Get-Item -LiteralPath $installRoot)) + @(Get-ChildItem -LiteralPath $installRoot -Force -Recurse)) {
                if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Existing installation reparse entry denied.' }
                $originalTreeAcls[$entry.FullName] = Get-Acl -LiteralPath $entry.FullName
            }
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
        Copy-FocusGuardComponentsWithRetry
        Remove-FocusGuardLegacyDesktopEntry
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
            $existingTarget = [IO.Path]::GetFullPath($existingShortcut.TargetPath)
            if (-not ($existingTarget.Equals((Join-Path $installRoot $uiExecutable), [StringComparison]::OrdinalIgnoreCase) -or
                      $existingTarget.Equals((Join-Path $installRoot 'FocusGuard.Desktop.exe'), [StringComparison]::OrdinalIgnoreCase))) {
                throw "Refusing to overwrite an unrelated Start Menu shortcut: $startMenuShortcut"
            }
        } else {
            $shortcutCreated = $true
        }
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($startMenuShortcut)
        $shortcut.TargetPath = Join-Path $installRoot $uiExecutable
        $shortcut.WorkingDirectory = $installRoot
        $shortcut.IconLocation = Join-Path $installRoot $uiExecutable
        $shortcut.Description = 'Open FocusGuard'
        $shortcut.Save()
        New-Item -Path $runKey -Force | Out-Null
        Set-ItemProperty -Path $runKey -Name $agentRunName -Value ('"' + (Join-Path $installRoot 'FocusGuard.ProcessAgent.exe') + '"')
        if (-not $installExisted -or $null -ne $previousRunRegistrations[$desktopRunName]) {
            Set-ItemProperty -Path $runKey -Name $desktopRunName -Value ('"' + (Join-Path $installRoot $uiExecutable) + '" --minimized')
        }

        if ($InjectFailureAfterCopyForRollbackTest) {
            throw 'Injected rollback verification failure after binary replacement.'
        }

        Assert-FocusGuardInstalledPayload

        & icacls.exe $installRoot /inheritance:r /grant:r `
            '*S-1-5-18:(OI)(CI)(F)' `
            '*S-1-5-32-544:(OI)(CI)(F)' `
            '*S-1-5-32-545:(OI)(CI)(RX)' `
            '*S-1-5-19:(OI)(CI)(RX)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Installation ACL provisioning failed with exit code $LASTEXITCODE." }

        & icacls.exe $dataRoot /inheritance:r /grant:r `
            '*S-1-5-18:(OI)(CI)(F)' `
            '*S-1-5-32-544:(OI)(CI)(F)' `
            '*S-1-5-19:(OI)(CI)(M)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Machine-state ACL provisioning failed with exit code $LASTEXITCODE." }

        Protect-FocusGuardInstallationTree
        # Prevent a filtered administrator user who owns a copied file from changing its DACL.
        & icacls.exe $installRoot /setowner '*S-1-5-32-544' /T /C | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Installation ownership provisioning failed.' }
        $optionalIpcRoles = @{ WebViewHost = $true }
        if (Test-Path -LiteralPath (Join-Path $installRoot 'FocusGuard.DrillClient.exe')) {
            $optionalIpcRoles.Diagnostic = @{ fileName='FocusGuard.DrillClient.exe';managedFileName='FocusGuard.DrillClient.dll' }
        }
        & $policyHelper -OwnerSid $ownerIdentity.Value -InstallRoot $installRoot -OutputPath (Join-Path $installRoot 'ipc-policy.json') `
            -OptionalBinaries $optionalIpcRoles -ClientMetadataQueryConsent | Out-Null
        Protect-FocusGuardInstallationTree
        & icacls.exe (Join-Path $installRoot 'ipc-policy.json') /setowner '*S-1-5-32-544' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'IPC policy ownership provisioning failed.' }

        if ($null -eq $existing) {
            & sc.exe create $serviceName binPath= "`"$serviceBinary`"" start= auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'FocusGuard Service'
            if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE." }
            $serviceCreated = $true
            & sc.exe description $serviceName 'Authoritative FocusGuard policy service.' | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "sc.exe description failed with exit code $LASTEXITCODE." }
        }

        & sc.exe config $serviceName binPath= "`"$serviceBinary`"" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Service configuration verification failed.' }
        # A crashed or killed service restarts after 5 s, then 30 s; a third failure within a day
        # takes no action so a crash loop cannot spin. A graceful stop is not a failure.
        & sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/30000// | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Service recovery configuration failed.' }
        Start-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromSeconds(15))
        Start-Sleep -Seconds 2
        $startedService = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
        if ($startedService.State -ne 'Running' -or $startedService.ProcessId -le 0) {
            throw 'FocusGuard service did not remain running after startup verification.'
        }
        Assert-FocusGuardServiceResponsive
        # Chromium relaunches the native host about one second after the registration reappears. Registering it before
        # the updated service and ipc-policy.json were live let a host start against a stopped service and stay not-ready
        # (observed after 0.1.122), so the registration returns only once the new service answered its health check.
        New-Item -Path $chromeHostRegistry -Force | Out-Null
        New-Item -Path $edgeHostRegistry -Force | Out-Null
        Set-Item -LiteralPath $chromeHostRegistry -Value (Join-Path $installedNativeManifests 'chrome.json')
        Set-Item -LiteralPath $edgeHostRegistry -Value (Join-Path $installedNativeManifests 'edge.json')
        Start-FocusGuardInteractiveProcess `
            -ExecutablePath (Join-Path $installRoot $uiExecutable) `
            -ProcessName 'FocusGuard.UI' `
            -Arguments '--minimized'
        Wait-FocusGuardSingleInteractiveProcess `
            -ExecutablePath (Join-Path $installRoot 'FocusGuard.ProcessAgent.exe') `
            -ProcessName 'FocusGuard.ProcessAgent'
        Write-Output 'FocusGuard service passed authoritative IPC health checks; one supervised ProcessAgent and the FocusGuard UI are running with the verified payload.'
    }
    catch {
        $installationError = $_
        Write-FocusGuardIpcRejectionDiagnostic
        Write-Warning "Installation failed; restoring the previous FocusGuard state."
        try {
            # A failure can occur after the new registration and the UI have
            # started. Quiesce every respawn source before touching the tree.
            Suspend-FocusGuardNativeHostRegistration
            $null = Stop-FocusGuardRuntimeProcesses
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
                foreach ($oldPath in @($originalTreeAcls.Keys | Sort-Object Length -Descending)) {
                    if (Test-Path -LiteralPath $oldPath) { Set-Acl -LiteralPath $oldPath -AclObject $originalTreeAcls[$oldPath] }
                }
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
            New-Item -Path $runKey -Force | Out-Null
            foreach ($valueName in @($agentRunName, $desktopRunName)) {
                $previousValue = $previousRunRegistrations[$valueName]
                if ($null -eq $previousValue) {
                    Remove-ItemProperty -LiteralPath $runKey -Name $valueName -ErrorAction SilentlyContinue
                } else {
                    Set-ItemProperty -LiteralPath $runKey -Name $valueName -Value $previousValue
                }
            }
            if ($shortcutCreated -and (Test-Path -LiteralPath $startMenuShortcut)) {
                Remove-Item -LiteralPath $startMenuShortcut -Force
            }

            if ($null -ne $existing) {
                & sc.exe config $serviceName binPath= $existing.PathName | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'Service configuration rollback failed.' }
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
