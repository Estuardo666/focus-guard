$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Gcp-Staging.ps1')
Import-Module Microsoft.PowerShell.Management -ErrorAction Stop
$apiKey=(Get-Clipboard -Raw -ErrorAction Stop).Trim()
if ($apiKey.Length -lt 32 -or -not $apiKey.StartsWith('re_')) { throw 'Clipboard does not contain a valid Resend API key; nothing was saved.' }
$accessToken=(gcloud auth print-access-token --account=transcitynorwalk@gmail.com).Trim()
$headers=@{Authorization='Bearer '+$accessToken}
$name="$infrastructurePrefix-resend-smtp-api-key"
$payload=@{payload=@{data=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($apiKey))}} | ConvertTo-Json -Depth 3 -Compress
try {
  $null=Invoke-RestMethod -Method Post -Uri "https://secretmanager.googleapis.com/v1/projects/$projectId/secrets?secretId=$name" -Headers $headers -ContentType 'application/json' -Body (@{replication=@{automatic=@{}}}|ConvertTo-Json -Compress) -TimeoutSec 30
} catch { if ([int]$_.Exception.Response.StatusCode -ne 409) { throw 'Unable to create mail-secret resource.' } }
$resource="projects/$projectId/secrets/$name"
$null=Invoke-RestMethod -Method Post -Uri "https://secretmanager.googleapis.com/v1/${resource}:addVersion" -Headers $headers -ContentType 'application/json' -Body $payload -TimeoutSec 30
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outPath=Join-Path $repository 'artifacts/cloud-phase1/staging/resend-api-key.dpapi'
Add-Type -AssemblyName System.Security
$bytes=[Text.Encoding]::UTF8.GetBytes($apiKey)
try { [IO.File]::WriteAllBytes($outPath,[Security.Cryptography.ProtectedData]::Protect($bytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)) }
finally { [Array]::Clear($bytes,0,$bytes.Length);$apiKey=$null;$payload=$null;$headers=$null;$accessToken=$null }
Write-Output 'RESEND_SECRET_STORED_IN_GOOGLE_SECRET_MANAGER_AND_LOCAL_CURRENTUSER_VAULT'
