[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$drillClient = Join-Path $repositoryRoot 'src\FocusGuard.DrillClient\bin\Release\net10.0\FocusGuard.DrillClient.exe'
$stateDirectory = Join-Path $repositoryRoot 'artifacts\fg112-reboot'
$statePath = Join-Path $stateDirectory 'state.json'
$resultPath = Join-Path $stateDirectory 'result.json'
$activePolicyPath = 'C:\ProgramData\FocusGuard\StrictPersistence\active-policy.json'

if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    throw 'FG-112 reboot state is not armed.'
}
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.schemaVersion -ne 1) { throw 'Unsupported FG-112 reboot state.' }

$service = Get-CimInstance Win32_Service -Filter "Name='FocusGuard'"
if ($null -eq $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto') {
    throw 'Production service did not recover running and automatic.'
}
$bootAfter = ([DateTimeOffset](Get-CimInstance Win32_OperatingSystem).LastBootUpTime).ToUniversalTime()
$restartObserved = $bootAfter -gt [DateTimeOffset]$state.bootUtcBeforeRestart
if (-not $restartObserved) { throw 'Windows restart was not observed.' }

$statusBeforeAck = ((& $drillClient status | Out-String) | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or $statusBeforeAck.sessionId -ne $state.sessionId) {
    throw 'The persisted session was not recovered by the production service.'
}
if ([DateTimeOffset]::UtcNow -lt [DateTimeOffset]$state.endUtc) {
    if (-not (Test-Path -LiteralPath $activePolicyPath -PathType Leaf)) {
        throw 'The unexpired strict policy was not retained after reboot.'
    }
    if ($statusBeforeAck.enforcementConnected) {
        throw 'Enforcement must remain released until fresh post-reboot acknowledgements arrive.'
    }
    $acknowledgement = ((& $drillClient acknowledge | Out-String) | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or -not $acknowledgement.extensionReady) {
        throw 'Post-reboot acknowledgement failed.'
    }
    $statusAfterAck = $null
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        $statusAfterAck = ((& $drillClient status | Out-String) | ConvertFrom-Json)
        if ($LASTEXITCODE -eq 0 -and $statusAfterAck.enforcementConnected) { break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $statusAfterAck.enforcementConnected) {
        throw 'Enforcement did not reconnect after fresh post-reboot acknowledgements.'
    }
} else {
    $statusAfterAck = $statusBeforeAck
    if ($statusAfterAck.sessionState -ne 'Expired' -or (Test-Path -LiteralPath $activePolicyPath)) {
        throw 'Expired post-reboot policy was not released.'
    }
}

$result = [ordered]@{
    schemaVersion = 1
    sessionId = $state.sessionId
    bootUtcBeforeRestart = $state.bootUtcBeforeRestart
    bootUtcAfterRestart = $bootAfter.ToString('O')
    restartObserved = $restartObserved
    stateBeforeAcknowledgement = $statusBeforeAck.sessionState
    enforcementBeforeAcknowledgement = $statusBeforeAck.enforcementConnected
    stateAfterAcknowledgement = $statusAfterAck.sessionState
    enforcementAfterAcknowledgement = $statusAfterAck.enforcementConnected
    verifiedUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
[IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
[pscustomobject]$result
