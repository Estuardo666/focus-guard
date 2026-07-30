[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-p]{32}$')]
    [string]$ExtensionId,

    [ValidateSet('heartbeat', 'status')]
    [string]$Type = 'heartbeat'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$hostDirectory = Join-Path $repositoryRoot 'artifacts\FocusGuard.NativeMessaging\host'
$hostPath = Join-Path $hostDirectory 'FocusGuard.NativeMessaging.exe'
$manifestPath = Join-Path $hostDirectory 'focusguard.native.json'

if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'Native messaging staging is missing. Run prepare-native-messaging-staging.ps1 first.'
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$origin = "chrome-extension://$ExtensionId/"
if ($manifest.allowed_origins -cnotcontains $origin) {
    throw "The staged manifest does not allow the exact requested origin: $origin"
}

$request = [ordered]@{
    protocolVersion = 1
    type = $Type
    correlationId = [Guid]::NewGuid()
    sentAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    acknowledgedPolicyId = $null
    acknowledgedPolicyHash = $null
    blockedUrl = $null
} | ConvertTo-Json -Compress
$payload = [Text.Encoding]::UTF8.GetBytes($request)
$frame = [byte[]]::new(4 + $payload.Length)
[BitConverter]::GetBytes([uint32]$payload.Length).CopyTo($frame, 0)
$payload.CopyTo($frame, 4)

$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\native-health-drill'))
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null
$inputPath = Join-Path $temporaryRoot 'request.bin'
$outputPath = Join-Path $temporaryRoot 'response.bin'
$errorPath = Join-Path $temporaryRoot 'stderr.txt'
[IO.File]::WriteAllBytes($inputPath, $frame)

$process = Start-Process `
    -FilePath $hostPath `
    -ArgumentList $origin `
    -RedirectStandardInput $inputPath `
    -RedirectStandardOutput $outputPath `
    -RedirectStandardError $errorPath `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
if ($process.ExitCode -ne 0) {
    throw "Native host exited with code $($process.ExitCode)."
}

$responseFrame = [IO.File]::ReadAllBytes($outputPath)
if ($responseFrame.Length -lt 5) {
    throw 'Native host response frame is truncated.'
}

$responseLength = [BitConverter]::ToUInt32($responseFrame, 0)
if ($responseLength -ne $responseFrame.Length - 4) {
    throw 'Native host response length does not match its frame.'
}

$responseJson = [Text.Encoding]::UTF8.GetString($responseFrame, 4, $responseLength)
$response = $responseJson | ConvertFrom-Json
if ($response.correlationId -ne ($request | ConvertFrom-Json).correlationId) {
    throw 'Native host response correlation ID does not match the request.'
}

$response
