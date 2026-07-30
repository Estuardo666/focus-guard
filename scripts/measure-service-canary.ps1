[CmdletBinding()]
param(
    [string]$ServiceName = 'FocusGuard',
    [ValidateRange(5, 60)]
    [int]$SampleSeconds = 10
)

$ErrorActionPreference = 'Stop'
$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
if ($null -eq $service -or $service.State -ne 'Running' -or $service.ProcessId -le 0) {
    throw "Service $ServiceName must be running for the resource canary."
}

$process = Get-Process -Id $service.ProcessId
$cpuBefore = $process.TotalProcessorTime.TotalSeconds
$started = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $SampleSeconds
$process.Refresh()
$started.Stop()
$cpuSeconds = $process.TotalProcessorTime.TotalSeconds - $cpuBefore
$cpuPercent = 100 * $cpuSeconds / $started.Elapsed.TotalSeconds / [Environment]::ProcessorCount
$workingSetMb = $process.WorkingSet64 / 1MB
$result = [ordered]@{
    serviceName = $ServiceName
    processId = $process.Id
    sampleSeconds = $started.Elapsed.TotalSeconds
    idleCpuPercent = $cpuPercent
    workingSetMb = $workingSetMb
    cpuBudgetPercent = 1.0
    memoryBudgetMb = 100.0
    passed = $cpuPercent -lt 1.0 -and $workingSetMb -lt 100.0
    measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}

$artifactDirectory = Join-Path $PSScriptRoot '..\artifacts\performance'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$artifactPath = Join-Path $artifactDirectory 'service-canary.json'
$result | ConvertTo-Json | Set-Content -LiteralPath $artifactPath -Encoding UTF8
$result | Format-List
if (-not $result.passed) {
    throw "Service resource canary exceeded its CPU or memory budget. See $artifactPath"
}
