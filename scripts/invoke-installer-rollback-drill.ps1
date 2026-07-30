[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installRoot = [IO.Path]::GetFullPath((Join-Path ${env:ProgramFiles} 'FocusGuard'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\installer'))
$packageRoot = Join-Path $artifactRoot 'FocusGuard-0.1.0'
foreach ($required in @($installRoot, $packageRoot)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required drill path is missing: $required" }
}

function Get-TreeFingerprint([string]$Root) {
    $prefixLength = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($prefixLength)
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            "$relativePath|$hash"
        } | Sort-Object)
}

try {
    $before = Get-TreeFingerprint $installRoot
    $desktop = Start-Process `
        -FilePath (Join-Path $installRoot 'FocusGuard.Desktop.exe') `
        -WindowStyle Hidden `
        -PassThru
    $windowReady = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 100
        $desktop.Refresh()
    } while (-not $desktop.HasExited -and
        $desktop.MainWindowHandle -eq 0 -and
        $windowReady.Elapsed -lt [TimeSpan]::FromSeconds(10))
    if ($desktop.HasExited) { throw 'Installed Desktop exited before installer drill.' }

    $expectedFailure = $null
    try {
        & (Join-Path $PSScriptRoot 'install-focusguard.ps1') `
            -PackageDirectory $packageRoot `
            -InjectFailureAfterCopyForRollbackTest
    }
    catch {
        $expectedFailure = $_.Exception.Message
    }
    if ($null -eq $expectedFailure) { throw 'Failure-injection update unexpectedly succeeded.' }

    $after = Get-TreeFingerprint $installRoot
    $differences = @(Compare-Object $before $after)
    $service = Get-CimInstance Win32_Service -Filter "Name='FocusGuard'"
    $desktopStillRunning = @(Get-Process -Name 'FocusGuard.Desktop' -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Path -eq (Join-Path $installRoot 'FocusGuard.Desktop.exe')
        }).Count -gt 0
    $agentRunning = @(Get-Process -Name 'FocusGuard.ProcessAgent' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Join-Path $installRoot 'FocusGuard.ProcessAgent.exe') }).Count -gt 0
    $result = [pscustomobject]@{
        ExpectedFailure = $expectedFailure
        TreeDifferences = $differences.Count
        ServiceState = $service.State
        DesktopStillRunning = $desktopStillRunning
        AgentRunning = $agentRunning
    }
    $result
    if ($differences.Count -ne 0 -or $service.State -ne 'Running' -or $desktopStillRunning -or -not $agentRunning) {
        throw 'Rollback drill verification failed.'
    }
}
finally {
    # The drill uses only the immutable package and the installer's own backup.
}
