[CmdletBinding()]
param(
    [ValidateRange(5, 60)]
    [int]$SampleSeconds = 10,
    [switch]$Enforce,
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$names = @(
    'FocusGuard.Desktop',
    'FocusGuard.Service',
    'FocusGuard.ProcessAgent',
    'FocusGuard.NativeMessaging',
    'FocusGuard.UI'
)
$processes = @(
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $names -contains $_.ProcessName }
)

$hostRoots = @($processes | Where-Object { $_.ProcessName -eq 'FocusGuard.UI' })
$hostProcessIds = [System.Collections.Generic.HashSet[int]]::new()
try {
    $processTree = @(Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId -ErrorAction Stop)
    foreach ($hostRoot in $hostRoots) { $null = $hostProcessIds.Add([int]$hostRoot.Id) }
    do {
        $addedDescendant = $false
        foreach ($candidate in $processTree) {
            $candidateId = [int]$candidate.ProcessId
            $parentId = [int]$candidate.ParentProcessId
            if ($hostProcessIds.Contains($parentId) -and -not $hostProcessIds.Contains($candidateId)) {
                $null = $hostProcessIds.Add($candidateId)
                $addedDescendant = $true
            }
        }
    } while ($addedDescendant)

    foreach ($hostTreeId in $hostProcessIds) {
        if (-not ($processes | Where-Object { $_.Id -eq $hostTreeId })) {
            try { $processes += Get-Process -Id $hostTreeId -ErrorAction Stop }
            catch { }
        }
    }
}
catch {
    throw "Unable to enumerate the React Host process tree: $($_.Exception.Message)"
}

$processes = @($processes | Sort-Object Id -Unique)
if ($processes.Count -eq 0) {
    throw 'No running FocusGuard processes were found.'
}

$cpuBefore = @{}
foreach ($process in $processes) {
    $cpuBefore[$process.Id] = $process.TotalProcessorTime.TotalSeconds
}

$timer = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $SampleSeconds
$timer.Stop()

$samples = @(
    foreach ($original in $processes) {
        try {
            $process = Get-Process -Id $original.Id -ErrorAction Stop
            [pscustomobject][ordered]@{
                name = $process.ProcessName
                processId = $process.Id
                reactHostTree = $hostProcessIds.Contains([int]$process.Id)
                cpuPercent = 100 *
                    ($process.TotalProcessorTime.TotalSeconds - $cpuBefore[$process.Id]) /
                    $timer.Elapsed.TotalSeconds /
                    [Environment]::ProcessorCount
                workingSetMb = $process.WorkingSet64 / 1MB
                privateMemoryMb = $process.PrivateMemorySize64 / 1MB
                threadCount = $process.Threads.Count
                handleCount = $process.HandleCount
            }
        }
        catch {
            [pscustomobject][ordered]@{
                name = $original.ProcessName
                processId = $original.Id
                reactHostTree = $hostProcessIds.Contains([int]$original.Id)
                exited = $true
            }
        }
    }
)

$running = @($samples | Where-Object { -not $_.exited })
$service = @($running | Where-Object { $_.name -eq 'FocusGuard.Service' })
$desktop = @($running | Where-Object { $_.name -eq 'FocusGuard.Desktop' })
$reactHostProcesses = @($running | Where-Object { $_.name -eq 'FocusGuard.UI' })
$reactHostTree = @($running | Where-Object { $_.reactHostTree })
$result = [ordered]@{
    measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sampleSeconds = $timer.Elapsed.TotalSeconds
    logicalProcessorCount = [Environment]::ProcessorCount
    reactHostProcessCount = $reactHostProcesses.Count
    reactHostTreeProcessCount = $reactHostTree.Count
    reactHostTreeWorkingSetMb = ($reactHostTree | ForEach-Object { $_.workingSetMb } | Measure-Object -Sum).Sum
    reactHostTreePrivateMemoryMb = ($reactHostTree | ForEach-Object { $_.privateMemoryMb } | Measure-Object -Sum).Sum
    totals = [ordered]@{
        processCount = $running.Count
        cpuPercent = ($running | ForEach-Object { $_.cpuPercent } | Measure-Object -Sum).Sum
        workingSetMb = ($running | ForEach-Object { $_.workingSetMb } | Measure-Object -Sum).Sum
        privateMemoryMb = ($running | ForEach-Object { $_.privateMemoryMb } | Measure-Object -Sum).Sum
    }
    gates = [ordered]@{
        serviceWithinBudget = $service.Count -eq 1 -and
            $service[0].cpuPercent -lt 1.0 -and
            $service[0].workingSetMb -lt 100.0
        singleDesktopInstance = $desktop.Count -le 1
        singleReactHostInstance = $reactHostProcesses.Count -le 1
    }
    processes = $samples
}

$artifactDirectory = Join-Path $PSScriptRoot '..\artifacts\performance'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $artifactPath = Join-Path $artifactDirectory 'focusguard-resources.json'
} else {
    $artifactPath = [IO.Path]::GetFullPath($OutputPath)
    $artifactDirectory = Split-Path -Parent $artifactPath
}
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $artifactPath -Encoding UTF8
$result | ConvertTo-Json -Depth 6

if ($Enforce -and (
    -not $result.gates.serviceWithinBudget -or
    -not $result.gates.singleDesktopInstance -or
    -not $result.gates.singleReactHostInstance)) {
    throw "FocusGuard exceeded a resource gate. See $artifactPath"
}
