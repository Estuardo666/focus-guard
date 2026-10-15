param([ValidateSet('Show','Headless','Status','Stop')][string]$Action='Show')
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$vmRoot=Join-Path $repository 'artifacts/cloud-phase0/android-vm'
$adb=Join-Path $vmRoot 'sdk/platform-tools/adb.exe'
$emulator=Join-Path $vmRoot 'sdk/emulator/emulator.exe'
$name='FocusGuard_Phase0_Android15'
$serial='emulator-5570'
$env:ANDROID_HOME=Join-Path $vmRoot 'sdk'
$env:ANDROID_USER_HOME=Join-Path $vmRoot 'user'
$originalAvdHome=Join-Path $vmRoot 'avd'
$localAvdHome=Join-Path $env:LOCALAPPDATA 'FocusGuard\AndroidVM\avd'
$localAvdIni=Join-Path $localAvdHome "$name.ini"
$localAvdDirectory=Join-Path $localAvdHome "$name.avd"
$env:ANDROID_AVD_HOME=if((Test-Path -LiteralPath $localAvdIni -PathType Leaf) -and
    (Test-Path -LiteralPath (Join-Path $localAvdDirectory 'config.ini') -PathType Leaf)) {$localAvdHome} else {$originalAvdHome}
foreach($exe in @($adb,$emulator)){if(-not(Test-Path -LiteralPath $exe)){throw 'The isolated Android SDK is not installed'}}
function Get-OwnedAdbState {
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$adb
    $info.Arguments="-s $serial get-state"
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true
    $info.RedirectStandardError=$true
    $query=New-Object Diagnostics.Process
    $query.StartInfo=$info
    try {
        [void]$query.Start()
        $value=$query.StandardOutput.ReadToEnd().Trim()
        [void]$query.StandardError.ReadToEnd()
        $query.WaitForExit()
        return @{State=$value;ExitCode=$query.ExitCode}
    } finally {$query.Dispose()}
}
$state=Get-OwnedAdbState
$running=$state.ExitCode -eq 0 -and $state.State -eq 'device'
if(-not $running){
    $starting=Get-CimInstance Win32_Process -Filter "Name='emulator.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($emulator) -and $_.CommandLine.Contains($name) }
    if($starting){throw 'The owned emulator is starting or offline; wait and check Status instead of launching a duplicate'}
}
if($running){
    $current=(& $adb -s $serial emu avd name | Out-String).Split([char]10)[0].Trim()
    if($current -ne $name){throw 'Port belongs to another emulator; refusing to control it'}
}
if($Action -eq 'Status'){
    [PSCustomObject]@{Avd=$name;Serial=$serial;Connected=$running;BootCompleted=if($running){(& $adb -s $serial shell getprop sys.boot_completed | Out-String).Trim()}else{'unknown'}} | ConvertTo-Json | Write-Output
    exit
}
if($Action -eq 'Stop' -or ($Action -eq 'Show' -and $running)){
    if($running){
        & $adb -s $serial emu kill
        if($LASTEXITCODE){throw 'Emulator stop failed'}
        $deadline=[DateTime]::UtcNow.AddSeconds(30)
        do{Start-Sleep -Milliseconds 500;$state=Get-OwnedAdbState}while($state.ExitCode -eq 0 -and [DateTime]::UtcNow -lt $deadline)
        if($state.ExitCode -eq 0){throw 'Emulator is still running; refusing a duplicate launch'}
    }
    if($Action -eq 'Stop'){exit}
    $running=$false
}
if($running){Write-Output 'Emulator already running';exit}
& $emulator -accel-check
if($LASTEXITCODE){throw 'Hardware acceleration is unavailable; see Android VM setup notes'}
$arguments=@('-avd',$name,'-port','5570','-no-audio','-no-boot-anim','-no-snapshot','-gpu','software','-accel','on')
if($Action -eq 'Headless'){$arguments+='-no-window'}
# Hidden applies to the helper console; Show starts the interactive emulator UI.
$process=Start-Process -FilePath $emulator -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $vmRoot 'emulator.stdout.log') -RedirectStandardError (Join-Path $vmRoot 'emulator.stderr.log') -PassThru
[PSCustomObject]@{Avd=$name;Serial=$serial;LauncherPid=$process.Id;Mode=$Action}
