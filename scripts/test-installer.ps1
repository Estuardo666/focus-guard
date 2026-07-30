$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scripts = @('build-installer.ps1', 'install-focusguard.ps1', 'uninstall-focusguard.ps1', 'build-browser-extension.ps1', 'generate-extension-icons.ps1')

foreach ($scriptName in $scripts) {
    $path = Join-Path $PSScriptRoot $scriptName
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -ne 0) { throw "$scriptName has PowerShell parse errors." }
}

$install = Get-Content (Join-Path $PSScriptRoot 'install-focusguard.ps1') -Raw
$uninstall = Get-Content (Join-Path $PSScriptRoot 'uninstall-focusguard.ps1') -Raw
$build = Get-Content (Join-Path $PSScriptRoot 'build-installer.ps1') -Raw
foreach ($required in @(
    'FocusGuard.Recovery.exe',
    'LocalService',
    '*S-1-5-18:(OI)(CI)(F)',
    '*S-1-5-32-544:(OI)(CI)(F)',
    '*S-1-5-19:(OI)(CI)(RX)',
    'icacls.exe',
    'SupportsShouldProcess',
    'FocusGuard-install-backup-',
    'Installation failed; restoring the previous FocusGuard state.',
    'Set-Acl',
    'Rollback also failed',
    'Get-Process -Name ''FocusGuard.Desktop''',
    'Get-Process -Name ''FocusGuard.ProcessAgent''',
    'CloseMainWindow',
    'minimized to the notification area',
    'Stop-Process -Id $desktopProcess.Id',
    'did not close; no files were replaced.',
    'Get-FocusGuardTreeFingerprint',
    'Rollback verification failed'
    'InjectFailureAfterCopyForRollbackTest'
    'did not remain running after startup verification.'
    'FocusGuard.ProcessAgent'
    'FocusGuard.Desktop'
    'Start-Process -FilePath'
    'FocusGuard.NativeMessaging'
    'NativeMessagingHosts\com.focusguard.native'
    'native-messaging\focusguard.native.json'
    'focusguard.native.json'
    'browser-extension\distribution.json'
    'startMenuShortcut'
    'FocusGuard.lnk'
    'WScript.Shell'
)) {
    if ($install -notmatch [regex]::Escape($required)) { throw "Installer is missing required contract: $required" }
}
if ($build -notmatch 'FocusGuard.Setup.exe' -or $build -notmatch 'PublishSingleFile=true') {
    throw 'Build script does not publish the graphical FocusGuard.Setup executable.'
}
if ($build -notmatch [regex]::Escape("'focusguard.native.json'")) {
    throw 'Build script does not package the native host local validation manifest.'
}
if ($build -notmatch 'OutputDirectory') {
    throw 'Build script does not support a recoverable alternate output directory.'
}
$setupManifest = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Setup\app.manifest') -Raw
if ($setupManifest -notmatch 'requireAdministrator') {
    throw 'Graphical setup executable is not configured for UAC elevation.'
}
$setupSource = Get-Content (Join-Path $repositoryRoot 'src\FocusGuard.Setup\Program.cs') -Raw
if ($setupSource -notmatch 'TrimEnd\(Path\.DirectorySeparatorChar, Path\.AltDirectorySeparatorChar\)') {
    throw 'Graphical setup does not normalize a trailing package-directory separator before PowerShell invocation.'
}
$installSource = Get-Content (Join-Path $repositoryRoot 'scripts\install-focusguard.ps1') -Raw
if ($installSource -notmatch 'Copy-Item -Path' -or $installSource -notmatch 'native-messaging' -or $installSource -notmatch 'browser-extension') {
    throw 'Installer does not merge existing native-messaging and browser-extension directories safely.'
}
if ($installSource -notmatch "installedNativeManifests 'focusguard\.native\.json'" -or
    $installSource -notmatch "installRoot 'focusguard\.native\.json'") {
    throw 'Installer does not place the native host local validation manifest beside the executable.'
}
foreach ($required in @('active-policy.json', 'recovery-request.json', 'PurgeData', 'No files were removed', 'NativeMessagingHosts\com.focusguard.native')) {
    if ($uninstall -notmatch [regex]::Escape($required)) { throw "Uninstaller is missing recovery contract: $required" }
}
if ($build -notmatch 'FocusGuardProduction=true' -or $build -notmatch 'Compress-Archive') {
    throw 'Build script does not produce a production bundle.'
}
if ($build -match 'utf8NoBOM' -or $build -notmatch '\[Text\.UTF8Encoding\]::new\(\$false\)') {
    throw 'Build script manifest encoding is not compatible with Windows PowerShell 5.1.'
}
Write-Output "Installer script tests passed for $repositoryRoot."
