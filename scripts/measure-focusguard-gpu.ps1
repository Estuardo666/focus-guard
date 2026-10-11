[CmdletBinding()]
param([ValidateRange(3,60)][int]$SampleSeconds = 5, [string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
$processTree = @(Get-CimInstance Win32_Process -Property Name,ProcessId,ParentProcessId)
$owned = [Collections.Generic.HashSet[int]]::new()
foreach ($process in $processTree | Where-Object Name -eq 'FocusGuard.UI.exe') { $null = $owned.Add([int]$process.ProcessId) }
if ($owned.Count -eq 0) { throw 'FocusGuard UI is not running.' }
do {
    $changed = $false
    foreach ($process in $processTree) {
        if ($owned.Contains([int]$process.ParentProcessId) -and $owned.Add([int]$process.ProcessId)) { $changed = $true }
    }
} while ($changed)
$samples = @(Get-Counter '\GPU Engine(*)\Utilization Percentage' -SampleInterval 1 -MaxSamples $SampleSeconds)
$usage = @($samples | ForEach-Object {
    $matching = @($_.CounterSamples | Where-Object { $_.InstanceName -match 'pid_(\d+)_' -and $owned.Contains([int]$Matches[1]) })
    [pscustomobject]@{ timestamp = $_.Timestamp; summedEnginePercent = ($matching.CookedValue | Measure-Object -Sum).Sum; peakEnginePercent = ($matching.CookedValue | Measure-Object -Maximum).Maximum }
})
$memory = @(Get-Counter '\GPU Process Memory(*)\Dedicated Usage','\GPU Process Memory(*)\Shared Usage')
$matchingMemory = @($memory.CounterSamples | Where-Object { $_.InstanceName -match 'pid_(\d+)_' -and $owned.Contains([int]$Matches[1]) })
$report = [ordered]@{
    measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sampleSeconds = $SampleSeconds
    processIds = @($owned)
    meanSummedEnginePercent = ($usage.summedEnginePercent | Measure-Object -Average).Average
    maxEnginePercent = ($usage.peakEnginePercent | Measure-Object -Maximum).Maximum
    dedicatedGpuMiB = (($matchingMemory | Where-Object Path -like '*\Dedicated Usage').CookedValue | Measure-Object -Sum).Sum / 1MB
    sharedGpuMiB = (($matchingMemory | Where-Object Path -like '*\Shared Usage').CookedValue | Measure-Object -Sum).Sum / 1MB
    note = 'Windows GPU engine counters for the FocusGuard UI process tree. Sum of engine percentages is not whole-GPU utilization.'
    samples = $usage
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $PSScriptRoot '..\artifacts\performance\focusguard-gpu.json' }
$artifact = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $artifact -Parent) | Out-Null
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $artifact -Encoding utf8
$report | ConvertTo-Json -Depth 5
