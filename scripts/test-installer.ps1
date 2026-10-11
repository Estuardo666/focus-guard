$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scripts = @('build-installer.ps1', 'install-focusguard.ps1', 'uninstall-focusguard.ps1', 'build-browser-extension.ps1', 'generate-extension-icons.ps1', 'measure-focusguard-resources.ps1', 'invoke-installer-rollback-drill.ps1', 'arm-fg112-production-reboot-drill.ps1', 'complete-fg112-production-reboot-drill.ps1')

foreach ($scriptName in $scripts) {
    $path = Join-Path $PSScriptRoot $scriptName
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -ne 0) { throw "$scriptName has PowerShell parse errors." }
}

$rollbackDrill = Get-Content (Join-Path $PSScriptRoot 'invoke-installer-rollback-drill.ps1') -Raw
foreach ($contract in @('PackageDirectory', 'PreflightOnly', 'OwnerPresent', 'MachineStateChanged = $false',
    'already elevated PowerShell process', '$drillClient status', 'ipc-policy.json', 'ownerUserSid',
    'GetOwnerSid', 'exactly one installed FocusGuard UI process in the configured owner session',
    'ClientMetadataQueryConsent', 'previously recorded client-metadata consent',
    'Injected rollback verification failure after binary replacement.', 'TreeDifferences=$($differences.Count)')) {
    if ($rollbackDrill -notmatch [regex]::Escape($contract)) {
        throw "Rollback drill is missing its explicit preflight/safety contract: $contract"
    }
}
if ($rollbackDrill -match 'FocusGuard-0\.1\.0') {
    throw 'Rollback drill still points to a stale hard-coded candidate package.'
}
if ($rollbackDrill.IndexOf('if ($PreflightOnly)', [StringComparison]::Ordinal) -gt
    $rollbackDrill.IndexOf('Get-CimInstance -ClassName Win32_Process', [StringComparison]::Ordinal)) {
    throw 'Rollback drill inspects owner Desktop processes before its read-only preflight return.'
}
$armRebootDrill = Get-Content (Join-Path $PSScriptRoot 'arm-fg112-production-reboot-drill.ps1') -Raw
$completeRebootDrill = Get-Content (Join-Path $PSScriptRoot 'complete-fg112-production-reboot-drill.ps1') -Raw
$drillClientProgram = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.DrillClient\Program.cs') -Raw
$rolePolicy = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Ipc.Windows\IpcAuthorizationPolicy.cs') -Raw
if ($armRebootDrill -match 'activate-strict|acknowledge' -or
    $completeRebootDrill -match '&\s*\$drillClient\s+acknowledge') {
    throw 'Production reboot drill must use a UI-reviewed strict session and real channel acknowledgements.'
}
foreach ($contract in @('IpcClientRole.Diagnostic => DiagnosticRequests.Contains(requestType)',
    'IpcMessageType.StatusRequest', 'IpcMessageType.ActivityRequest')) {
    if ($rolePolicy -notmatch [regex]::Escape($contract)) {
        throw "Diagnostic authorization is missing its read-only contract: $contract"
    }
}
foreach ($contract in @('Diagnostic cannot activate sessions', 'Diagnostic cannot acknowledge Native Messaging health')) {
    if ($drillClientProgram -notmatch [regex]::Escape($contract)) {
        throw "Production Diagnostic client is missing its mutator denial: $contract"
    }
}
$activationGuard = $drillClientProgram.IndexOf('Diagnostic cannot activate sessions', [StringComparison]::Ordinal)
$authenticatedFactory = $drillClientProgram.IndexOf('new FocusGuard.Ipc.Windows.VerifiedPipeConnectionFactory', [StringComparison]::Ordinal)
if ($activationGuard -lt 0 -or $authenticatedFactory -lt 0 -or $activationGuard -gt $authenticatedFactory) {
    throw 'Diagnostic mutator rejection must happen before authenticated pipe initialization.'
}
foreach ($contract in @('PreflightOnly', 'OwnerPresent', 'enforcementConnected', "NextCommand = 'Restart-Computer'")) {
    if ($armRebootDrill -notmatch [regex]::Escape($contract)) {
        throw "Production reboot arming flow is missing $contract."
    }
}
foreach ($contract in @('RunId', 'enforcementConnected', 'ChannelWaitMinutes', 'StrictMode')) {
    if ($completeRebootDrill -notmatch [regex]::Escape($contract)) {
        throw "Production reboot completion flow is missing $contract."
    }
}

$install = Get-Content (Join-Path $PSScriptRoot 'install-focusguard.ps1') -Raw
foreach ($contract in @('Get-FocusGuardInteractiveOwnerSid.ps1', 'New-FocusGuardIpcPolicy.ps1',
    'ClientMetadataQueryConsent', 'Registry::HKEY_USERS', 'ipc-policy.json', '/setowner',
    '--installer-owner-probe', 'Service configuration rollback failed.')) {
    if ($install -notmatch [regex]::Escape($contract)) { throw "Authenticated installer contract missing: $contract" }
}
foreach ($contract in @('9BA05972-F6A8-11CF-A442-00A0C90A8F39', 'FindWindowSW(location, locationRoot, 8, out desktopWindow, 1)',
    'GetShellWindow()', 'TokenIntegrityLevel', 'GetOwnerSid', 'Get-FocusGuardInteractiveSessionId',
    'Assert-FocusGuardInteractiveProcessContext', 'IntegrityLevelRid -ne 8192',
    'ValidateDesktopShellDispatcher', 'ExecuteFromDesktop')) {
    if ($install -notmatch [regex]::Escape($contract)) {
        throw "Interactive launch owner/integrity contract missing: $contract"
    }
}
if ($install -match 'New-Object -ComObject Shell\.Application') {
    throw 'Installer creates Shell.Application in its elevated caller instead of using the Explorer desktop dispatcher.'
}
$uninstall = Get-Content (Join-Path $PSScriptRoot 'uninstall-focusguard.ps1') -Raw
$build = Get-Content (Join-Path $PSScriptRoot 'build-installer.ps1') -Raw
$hostProject = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Host.Windows\FocusGuard.Host.Windows.csproj') -Raw
$hostProgram = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Host.Windows\Program.cs') -Raw
$hostMainForm = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Host.Windows\MainForm.cs') -Raw
foreach ($contract in @('ApplicationHighDpiMode>PerMonitorV2', 'AutoScaleMode = AutoScaleMode.Dpi')) {
    $source = if ($contract.StartsWith('ApplicationHighDpiMode')) { $hostProject } else { $hostMainForm }
    if ($source -notmatch [regex]::Escape($contract)) { throw "WebView2 host DPI contract missing: $contract" }
}
foreach ($contract in @('--smoke-window-dpi', 'WindowDpiContract', 'GetWindowDpiAwarenessContext',
    'AreDpiAwarenessContextsEqual', 'GetDpiForWindow', 'MinimumSize.Width * 96d / dpi')) {
    if ($hostProgram -notmatch [regex]::Escape($contract)) { throw "Host DPI smoke contract missing: $contract" }
}
$windowDpiSmokeIndex = $hostProgram.IndexOf('if (smokeScenario == HostSmokeScenario.WindowDpiContract)', [StringComparison]::Ordinal)
$applicationRunIndex = $hostProgram.IndexOf('Application.Run(smokeForm)', [StringComparison]::Ordinal)
$shellRunIndex = $hostProgram.IndexOf('Application.Run(shell)', [StringComparison]::Ordinal)
if ($windowDpiSmokeIndex -lt 0 -or $applicationRunIndex -lt 0 -or $shellRunIndex -lt 0 -or
    $windowDpiSmokeIndex -gt $applicationRunIndex -or $windowDpiSmokeIndex -gt $shellRunIndex) {
    throw 'Window DPI smoke must validate before running the interactive window or starting its services.'
}
foreach ($required in @(
    'FocusGuard.Recovery.exe',
    'LocalService',
    '*S-1-5-18:(OI)(CI)(F)',
    '*S-1-5-32-544:(OI)(CI)(F)',
    '*S-1-5-32-545:(OI)(CI)(RX)',
    '*S-1-5-19:(OI)(CI)(RX)',
    'icacls.exe',
    'SupportsShouldProcess',
    'FocusGuard-install-backup-',
    'Installation failed; restoring the previous FocusGuard state.',
    'Set-Acl',
    'Rollback also failed',
    'Get-Process -Name ''FocusGuard.Desktop''',
    'Get-Process -Name ''FocusGuard.UI''',
    'Stop-Process -Id $hostProcess.Id',
    'Remove-FocusGuardLegacyDesktopEntry',
    'Get-Process -Name ''FocusGuard.ProcessAgent''',
    'CloseMainWindow',
    'minimized to the notification area',
    'Stop-Process -Id $desktopProcess.Id',
    'did not close; no files were replaced.',
    'Get-FocusGuardTreeFingerprint',
    'Rollback verification failed'
    'InjectFailureAfterCopyForRollbackTest'
    'startup verification expected exactly one process; found'
    'startup verification expected one new process; found'
    'Start-FocusGuardInteractiveProcess'
    'ValidateDesktopShellDispatcher'
    'ExecuteFromDesktop($ExecutablePath, $Arguments, $installRoot)'
    'Assert-FocusGuardInstalledPayload'
    'Installed payload verification failed; stale file'
    'Wait-FocusGuardSingleInteractiveProcess'
    'Assert-FocusGuardServiceResponsive'
    '--service-health-check'
    'failed the authoritative IPC health check'
    'expected exactly one process'
    'FocusGuard service passed authoritative IPC health checks; one supervised ProcessAgent and the FocusGuard UI are running with the verified payload.'
    'FocusGuard.ProcessAgent'
    'FocusGuard.Desktop'
    '--minimized'
    'previousRunRegistrations'
    'Start-Process -FilePath'
    'FocusGuard.NativeMessaging'
    'NativeMessagingHosts\com.focusguard.native'
    'native-messaging\focusguard.native.json'
    'focusguard.native.json'
    'browser-extension\distribution.json'
    'startMenuShortcut'
    'FocusGuard.lnk'
    'WScript.Shell'
    '$shortcut.IconLocation = Join-Path $installRoot $uiExecutable'
    '$uiExecutable = ''FocusGuard.UI.exe'''
)) {
    if ($install -notmatch [regex]::Escape($required)) { throw "Installer is missing required contract: $required" }
}
if ($install.Contains('publish\FocusGuard.Desktop') -or $install.Contains("-ExecutablePath (Join-Path `$installRoot 'FocusGuard.Desktop.exe')")) {
    throw 'Installer still ships or starts the retired Avalonia Desktop app.'
}
if ($build -match "'FocusGuard\.Desktop'") {
    throw 'Build script still publishes the retired Avalonia Desktop app.'
}
if ($install -match 'Start-Process -FilePath \$explorer') {
    throw 'Installer still uses explorer.exe command-line launching, which opens Documents instead of FocusGuard.'
}
if ($install -match "Start-FocusGuardInteractiveProcess\s+[\s\S]{0,200}-ExecutablePath \(Join-Path \$installRoot 'FocusGuard\.ProcessAgent\.exe'\)") {
    throw 'Installer still starts ProcessAgent directly instead of letting the UI host supervise one instance.'
}
if ($install -notmatch [regex]::Escape('sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/30000//')) {
    throw 'Installer does not configure bounded service restart after a crash.'
}
if ($build -notmatch 'FocusGuard.Setup.exe' -or $build -notmatch 'PublishSingleFile=true') {
    throw 'Build script does not publish the graphical FocusGuard.Setup executable.'
}
foreach ($required in @('FocusGuard.Payload.zip', 'FocusGuardPayloadArchive', 'Compress-Archive', 'FocusGuard-$Version-Setup.exe', '--package-file-hash', 'stale UI payload')) {
    if ($build -notmatch [regex]::Escape($required)) {
        throw "Build script does not create the standalone Setup payload: $required"
    }
}
if ($build -notmatch [regex]::Escape("'focusguard.native.json'")) {
    throw 'Build script does not package the native host local validation manifest.'
}
if ($build -notmatch 'OutputDirectory') {
    throw 'Build script does not support a recoverable alternate output directory.'
}
if ($build -notmatch 'PayloadSuffix' -or $build -notmatch 'PayloadSuffix must contain only letters, digits, and hyphens' -or
    $build -notmatch 'SetupPublishSuffix' -or $build -notmatch 'SetupPublishSuffix must contain only letters, digits, and hyphens') {
    throw 'Build script does not support isolated, validated installer payload and Setup publish directories.'
}
$setupManifest = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Setup\app.manifest') -Raw
if ($setupManifest -notmatch 'requireAdministrator') {
    throw 'Graphical setup executable is not configured for UAC elevation.'
}
# The installer operations (PowerShell launch, process closing) are shared by the React and classic windows.
$setupSource = (Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Setup\Program.cs') -Raw) + (Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Setup\SetupOperations.cs') -Raw)
$embeddedPackageSource = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Setup\EmbeddedPackage.cs') -Raw
$setupProjectSource = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Setup\FocusGuard.Setup.csproj') -Raw
if ($setupSource -notmatch 'TrimEnd\(Path\.DirectorySeparatorChar, Path\.AltDirectorySeparatorChar\)') {
    throw 'Graphical setup does not normalize a trailing package-directory separator before PowerShell invocation.'
}
if ($setupSource -notmatch [regex]::Escape('Environment["PSModulePath"]') -or
    $setupSource -notmatch [regex]::Escape('Path.Combine(windowsPowerShellRoot, "powershell.exe")')) {
    throw 'Graphical setup does not isolate Windows PowerShell from an inherited PowerShell 7 module path.'
}
if ($setupSource -notmatch [regex]::Escape('startInfo.Environment["PSModulePath"] = Path.Combine(windowsPowerShellRoot, "Modules");')) {
    throw 'Elevated Setup must use only Windows PowerShell system modules.'
}
foreach ($required in @('EmbeddedPackage.Open', '--package-self-test', '--package-file-hash', 'FocusGuard.Payload.zip', 'MaximumUncompressedBytes', 'unsafe path')) {
    $source = if ($required -in @('EmbeddedPackage.Open', '--package-self-test', '--package-file-hash')) { $setupSource } elseif ($required -eq 'FocusGuard.Payload.zip') { $setupProjectSource } else { $embeddedPackageSource }
    if ($source -notmatch [regex]::Escape($required)) {
        throw "Standalone Setup implementation is missing required contract: $required"
    }
}

$agentSource = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.ProcessAgent\Program.cs') -Raw
foreach ($required in @('ProcessAgentStartupConfiguration.SingleInstanceName', 'isFirstInstance')) {
    if ($agentSource -notmatch [regex]::Escape($required)) {
        throw "ProcessAgent does not enforce its single-instance contract: $required"
    }
}
$agentStartupSource = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.ProcessAgent\ProcessAgentStartupConfiguration.cs') -Raw
if ($agentStartupSource -notmatch [regex]::Escape('"Local\\" + ProcessAgentProtocol.PipeName')) {
    throw 'ProcessAgent single-instance namespace does not follow the production/development endpoint.'
}
$installSource = Get-Content (Join-Path $repositoryRoot 'scripts\install-focusguard.ps1') -Raw
if ($installSource -notmatch 'Copy-Item -Path' -or $installSource -notmatch 'native-messaging' -or $installSource -notmatch 'browser-extension') {
    throw 'Installer does not merge existing native-messaging and browser-extension directories safely.'
}
foreach ($required in @(
    'Suspend-FocusGuardNativeHostRegistration',
    'Suspending FocusGuard browser native-host registration during update.')) {
    if ($installSource -notmatch [regex]::Escape($required)) {
        throw "Installer does not suspend browser native-host respawn during binary replacement: $required"
    }
}
$transactionStart = $installSource.IndexOf(
    'if ($PSCmdlet.ShouldProcess',
    [StringComparison]::Ordinal)
if ($transactionStart -lt 0) {
    throw 'Installer transaction entry point was not found.'
}
$transactionSource = $installSource.Substring($transactionStart)
$initialSuspend = $transactionSource.IndexOf(
    'Suspend-FocusGuardNativeHostRegistration',
    [StringComparison]::Ordinal)
$initialStop = $transactionSource.IndexOf(
    '$agentWasRunning = Stop-FocusGuardRuntimeProcesses',
    [StringComparison]::Ordinal)
$componentCopy = $transactionSource.IndexOf(
    'Copy-FocusGuardComponentsWithRetry',
    [StringComparison]::Ordinal)
$hostRegistrationRestore = $transactionSource.IndexOf(
    'New-Item -Path $chromeHostRegistry',
    [StringComparison]::Ordinal)
if ($initialSuspend -lt 0 -or
    $initialStop -le $initialSuspend -or
    $componentCopy -le $initialStop -or
    $hostRegistrationRestore -le $componentCopy) {
    throw 'Installer must suspend native-host registration before stopping runtimes and keep it suspended through binary replacement.'
}
$serviceResponsive = $transactionSource.IndexOf(
    'Assert-FocusGuardServiceResponsive',
    [StringComparison]::Ordinal)
if ($serviceResponsive -lt 0 -or $hostRegistrationRestore -le $serviceResponsive) {
    throw 'Installer must register the native host only after the updated service answered its health check, so a Chromium-relaunched host cannot start against a stopped service or the previous policy.'
}
$rollbackStart = $transactionSource.IndexOf(
    'catch {',
    [StringComparison]::Ordinal)
if ($rollbackStart -lt 0) {
    throw 'Installer rollback entry point was not found.'
}
$rollbackSource = $transactionSource.Substring($rollbackStart)
$rollbackSuspend = $rollbackSource.IndexOf(
    'Suspend-FocusGuardNativeHostRegistration',
    [StringComparison]::Ordinal)
$rollbackStop = $rollbackSource.IndexOf(
    '$null = Stop-FocusGuardRuntimeProcesses',
    [StringComparison]::Ordinal)
$rollbackRemove = $rollbackSource.IndexOf(
    'Remove-Item -LiteralPath $installRoot -Recurse -Force',
    [StringComparison]::Ordinal)
if ($rollbackSuspend -lt 0 -or
    $rollbackStop -le $rollbackSuspend -or
    $rollbackRemove -le $rollbackStop) {
    throw 'Installer rollback must suspend native-host registration and stop every runtime before restoring installation files.'
}
if ($installSource -notmatch "installedNativeManifests 'focusguard\.native\.json'" -or
    $installSource -notmatch "installRoot 'focusguard\.native\.json'") {
    throw 'Installer does not place the native host local validation manifest beside the executable.'
}
foreach ($required in @('active-policy.json', 'recovery-request.json', 'PurgeData', 'No files were removed', 'NativeMessagingHosts\com.focusguard.native', 'FocusGuard.ProcessAgent', 'FocusGuard.Desktop', 'Remove-ItemProperty')) {
    if ($uninstall -notmatch [regex]::Escape($required)) { throw "Uninstaller is missing recovery contract: $required" }
}
if ($build -notmatch 'FocusGuardProduction=true' -or $build -notmatch 'Compress-Archive') {
    throw 'Build script does not produce a production bundle.'
}
if ($build -match 'utf8NoBOM' -or $build -notmatch '\[Text\.UTF8Encoding\]::new\(\$false\)') {
    throw 'Build script manifest encoding is not compatible with Windows PowerShell 5.1.'
}
$installScriptPath = Join-Path $PSScriptRoot 'install-focusguard.ps1'
$installTokens = $null
$installErrors = $null
$installAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $installScriptPath, [ref]$installTokens, [ref]$installErrors)
if ($installErrors.Count -ne 0) { throw 'Installer has parse errors before browser-copy fixture tests.' }
$shellTypeAssignment = $installAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'interactiveShellTypeDefinition'
}, $true)
if ($null -eq $shellTypeAssignment -or
    $shellTypeAssignment.Right -isnot [System.Management.Automation.Language.CommandExpressionAst] -or
    $shellTypeAssignment.Right.Expression -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) {
    throw 'Installer interactive shell/token helper definition could not be extracted for runtime testing.'
}
if ($null -eq ('FocusGuardInteractiveShell' -as [type])) {
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        Add-Type -TypeDefinition $shellTypeAssignment.Right.Expression.Value -Language CSharp `
            -ReferencedAssemblies @('Microsoft.CSharp') -ErrorAction Stop
    } else {
        Add-Type -TypeDefinition $shellTypeAssignment.Right.Expression.Value -Language CSharp -ErrorAction Stop
    }
}
$currentToken = [FocusGuardInteractiveShell]::GetProcessTokenFacts($PID)
if ($currentToken.SessionId -ne [Diagnostics.Process]::GetCurrentProcess().SessionId -or
    $currentToken.IntegrityLevelRid -le 0) {
    throw 'Installer token inspector failed its current-process session/integrity smoke.'
}
if ([FocusGuardInteractiveShell]::HasInteractiveDesktop()) {
    $desktopPid = [FocusGuardInteractiveShell]::GetDesktopProcessId()
    $desktop = Get-Process -Id $desktopPid -ErrorAction Stop
    $desktopToken = [FocusGuardInteractiveShell]::GetProcessTokenFacts($desktopPid)
    if ($desktopToken.IsElevated -or $desktopToken.IntegrityLevelRid -ne 8192 -or
        $desktopToken.SessionId -ne $desktop.SessionId) {
        throw 'Interactive desktop shell is not running with a medium-integrity token.'
    }
    if (-not [FocusGuardInteractiveShell]::ValidateDesktopShellDispatcher()) {
        throw 'Interactive desktop dispatcher was not returned.'
    }

    $probeExecutable = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $probeExecutable -PathType Leaf)) {
        throw 'Windows PowerShell is unavailable for the Explorer-launch regression smoke.'
    }
    $launchAt = [DateTime]::UtcNow
    [FocusGuardInteractiveShell]::ExecuteFromDesktop(
        $probeExecutable,
        '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -Command "Start-Sleep -Seconds 3"',
        $env:TEMP)
    $probeDeadline = [DateTime]::UtcNow.AddSeconds(10)
    $probe = $null
    while ($null -eq $probe -and [DateTime]::UtcNow -lt $probeDeadline) {
        $probe = Get-Process -Name 'powershell' -ErrorAction SilentlyContinue | Where-Object {
            try {
                [IO.Path]::GetFullPath($_.Path).Equals($probeExecutable, [StringComparison]::OrdinalIgnoreCase) -and
                    $_.StartTime.ToUniversalTime() -ge $launchAt.AddSeconds(-1)
            } catch { $false }
        } | Select-Object -First 1
        if ($null -eq $probe) { Start-Sleep -Milliseconds 50 }
    }
    if ($null -eq $probe) { throw 'Explorer did not launch the hidden owner-context smoke process.' }
    try {
        $probeToken = [FocusGuardInteractiveShell]::GetProcessTokenFacts($probe.Id)
        $desktopRecord = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $desktopPid" -ErrorAction Stop
        $desktopOwner = Invoke-CimMethod -InputObject $desktopRecord -MethodName GetOwnerSid -ErrorAction Stop
        $probeRecord = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $($probe.Id)" -ErrorAction Stop
        $probeOwner = Invoke-CimMethod -InputObject $probeRecord -MethodName GetOwnerSid -ErrorAction Stop
        if ($probeToken.IsElevated -or $probeToken.IntegrityLevelRid -ne 8192 -or
            $probeToken.SessionId -ne $desktop.SessionId -or
            $desktopOwner.ReturnValue -ne 0 -or $probeOwner.ReturnValue -ne 0 -or
            $probeOwner.Sid -cne $desktopOwner.Sid) {
            throw 'Explorer-launched smoke process did not inherit the owner medium-integrity session.'
        }
        if (-not $probe.WaitForExit(10000)) { throw 'Explorer-launched smoke process did not exit within its test deadline.' }
        Write-Output 'Interactive desktop launch/token smoke passed: owner SID/session match, elevated=false, integrity=medium.'
    }
    finally {
        $probe.Refresh()
        if (-not $probe.HasExited) { Stop-Process -Id $probe.Id -ErrorAction SilentlyContinue }
    }
} else {
    Write-Output 'SKIP: Explorer launch/token integration smoke requires an interactive desktop; token API smoke passed.'
}
foreach ($functionName in @('Get-FocusGuardBrowserPackageEntries', 'Assert-FocusGuardInstalledPayload')) {
    $functionAst = $installAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $functionName
    }, $true)
    if ($null -eq $functionAst) { throw "Installer function missing: $functionName" }
    . ([scriptblock]::Create($functionAst.Extent.Text))
}
$tempRoot = Join-Path $env:TEMP ('FocusGuard.BrowserCopyTest.' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $tempRoot) { throw 'Browser-copy test target unexpectedly exists.' }
$packageRoot = Join-Path $tempRoot 'package'
$installRoot = Join-Path $tempRoot 'installed'
try {
    New-Item -ItemType Directory -Path $packageRoot, $installRoot | Out-Null
    $version = '0.1.86'
    [IO.File]::WriteAllText((Join-Path $packageRoot 'manifest.json'), (@{ Version = $version } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $browserDirectory = Join-Path $packageRoot 'browser-extension'
    New-Item -ItemType Directory -Path (Join-Path $browserDirectory 'staging\src'), (Join-Path $packageRoot 'native-messaging') -Force | Out-Null
    $packages = @()
    foreach ($browser in @('chrome', 'edge')) {
        $archiveName = "FocusGuard-$version-$browser.zip"
        $archivePath = Join-Path $browserDirectory $archiveName
        [IO.File]::WriteAllText($archivePath, "$browser package fixture", [Text.UTF8Encoding]::new($false))
        $packages += [pscustomobject]@{
            browser = $browser
            file = $archiveName
            sha256 = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    [IO.File]::WriteAllText((Join-Path $browserDirectory 'distribution.json'),
        (@{ version = $version; extensionId = 'emhdinkjpinjnmldipnejnbeefgonjnj'; packages = $packages } | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $browserDirectory 'staging\manifest.json'), '{}', [Text.UTF8Encoding]::new($false))
    foreach ($source in @('native-health.js', 'navigation-enforcement.js', 'policy-integrity.js')) {
        [IO.File]::WriteAllText((Join-Path (Join-Path $browserDirectory 'staging\src') $source), $source, [Text.UTF8Encoding]::new($false))
    }
    foreach ($source in @('chrome.json', 'edge.json', 'focusguard.native.json')) {
        [IO.File]::WriteAllText((Join-Path (Join-Path $packageRoot 'native-messaging') $source), '{}', [Text.UTF8Encoding]::new($false))
    }
    foreach ($component in @('FocusGuard.Service', 'FocusGuard.Recovery', 'FocusGuard.ProcessAgent', 'FocusGuard.NativeMessaging', 'FocusGuard.Host.Windows', 'FocusGuard.DrillClient')) {
        $directory = Join-Path $packageRoot "publish\$component"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $executableName = if ($component -eq 'FocusGuard.Host.Windows') { 'FocusGuard.UI.exe' } else { "$component.exe" }
        [IO.File]::WriteAllText((Join-Path $directory $executableName), $component, [Text.UTF8Encoding]::new($false))
        if ($component -eq 'FocusGuard.Host.Windows') {
            [IO.File]::WriteAllText((Join-Path $directory 'FocusGuard.UI.dll'), 'host dll', [Text.UTF8Encoding]::new($false))
            New-Item -ItemType Directory -Path (Join-Path $directory 'ui') -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path (Join-Path $directory 'ui') 'index.html'), 'host html', [Text.UTF8Encoding]::new($false))
        }
    }
    foreach ($component in @('FocusGuard.Service', 'FocusGuard.Recovery', 'FocusGuard.ProcessAgent', 'FocusGuard.NativeMessaging', 'FocusGuard.Host.Windows', 'FocusGuard.DrillClient')) {
        Get-ChildItem -LiteralPath (Join-Path $packageRoot "publish\$component") -File -Force | Copy-Item -Destination $installRoot -Force
    }
    Copy-Item -LiteralPath (Join-Path $packageRoot 'publish\FocusGuard.Host.Windows\ui') -Destination $installRoot -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $packageRoot 'native-messaging') -Destination $installRoot -Recurse -Force
    Copy-Item -LiteralPath $browserDirectory -Destination $installRoot -Recurse -Force
    $installedExtension = Join-Path $installRoot 'browser-extension'
    $packageRoot = [IO.Path]::GetFullPath($packageRoot)
    $installRoot = [IO.Path]::GetFullPath($installRoot)
    Assert-FocusGuardInstalledPayload
    [IO.File]::AppendAllText((Join-Path $installedExtension 'FocusGuard-0.1.86-edge.zip'), 'stale')
    $staleCopyRejected = $false
    try { Assert-FocusGuardInstalledPayload } catch { $staleCopyRejected = $_.Exception.Message -like '*stale file*FocusGuard-0.1.86-edge.zip*' }
    if (-not $staleCopyRejected) { throw 'Installed browser archive drift was not rejected by payload verification.' }
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
        $resolvedTempBase = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
        if (-not $resolvedTempRoot.StartsWith($resolvedTempBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove browser-copy test data outside the temporary directory.'
        }
        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
    }
}

Write-Output 'Installed Chrome/Edge archive parity tests passed.'
Write-Output "Installer script tests passed for $repositoryRoot."
