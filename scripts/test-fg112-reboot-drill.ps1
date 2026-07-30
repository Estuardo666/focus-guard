$ErrorActionPreference = 'Stop'
foreach ($name in @(
    'arm-fg112-production-reboot-drill.ps1',
    'complete-fg112-production-reboot-drill.ps1')) {
    $path = Join-Path $PSScriptRoot $name
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -ne 0) {
        throw "$name has PowerShell parse errors: $($errors.Message -join '; ')"
    }
}
Write-Output 'FG-112 production reboot drill scripts parsed successfully.'
exit 0
