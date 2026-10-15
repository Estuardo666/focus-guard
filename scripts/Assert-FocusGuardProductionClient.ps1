[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$publishRoot = Join-Path ([IO.Path]::GetFullPath($PackageDirectory)) 'publish'
$clients = @(Get-ChildItem -LiteralPath $publishRoot -Filter 'FocusGuard.Client.dll' -File -Recurse)
if ($clients.Count -lt 1) { throw 'Production Client verification found no Client assemblies.' }
foreach ($client in $clients) {
    $bytes = [IO.File]::ReadAllBytes($client.FullName)
    # CLR user strings are UTF-16, and their heap position is not guaranteed to be even.
    $strings = [Text.Encoding]::Unicode.GetString($bytes)
    $unaligned = if ($bytes.Length -gt 1) { [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1) } else { '' }
    if ($strings.Contains('FocusGuard.Development.') -or $unaligned.Contains('FocusGuard.Development.')) {
        throw "Development IPC endpoint found in production Client: $($client.FullName)"
    }
    if (-not ($strings.Contains('FocusGuard.Control.v2') -or $unaligned.Contains('FocusGuard.Control.v2'))) {
        throw "Production control endpoint is missing from Client: $($client.FullName)"
    }
}
Write-Output "Production IPC endpoint verified in $($clients.Count) Client assemblies."
