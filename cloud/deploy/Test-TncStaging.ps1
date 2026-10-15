$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Gcp-Staging.ps1')
$gc=(Get-Command gcloud.cmd).Source
$token=(& $gc auth print-access-token --account=$($gcpStaging.account)).Trim()
if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'Owner authentication unavailable.' }
$ownerHeaders=@{Authorization='Bearer '+$token}
$checks=[Collections.Generic.List[string]]::new()
function Assert([bool]$Condition,[string]$Name) {
    if (-not $Condition) { throw "FAILED: $Name" }
    $checks.Add($Name)
    Write-Output "PASS: $Name"
}
function GetApi([string]$Uri,$Headers) { Invoke-RestMethod -Uri $Uri -Headers $Headers -TimeoutSec 120 }
function Permissions([string]$Uri,[string[]]$Names,$Headers) {
    $body=@{permissions=$Names}|ConvertTo-Json -Compress
    (Invoke-RestMethod -Method Post -Uri $Uri -Headers $Headers -Body $body -ContentType application/json -TimeoutSec 30).permissions
}
try {
    $project=GetApi "https://cloudresourcemanager.googleapis.com/v1/projects/$projectId" $ownerHeaders
    Assert ($project.name -ceq $gcpStaging.displayName -and $project.projectId -ceq $projectId) 'Project ID and display name'
    $billing=GetApi "https://cloudbilling.googleapis.com/v1/projects/$projectId/billingInfo" $ownerHeaders
    $sourceBilling=GetApi 'https://cloudbilling.googleapis.com/v1/projects/focusguard-staging-2026/billingInfo' $ownerHeaders
    Assert ($billing.billingEnabled -and $billing.billingAccountName -ceq $sourceBilling.billingAccountName) 'Same enabled Transcity billing account'
    $urls=@{}
    foreach ($kind in @('identity','api')) {
        $name="tnc-$kind-staging"
        $url="https://$($gcpStaging.region)-run.googleapis.com/apis/serving.knative.dev/v1/namespaces/$projectId/services/$name"
        $service=GetApi $url $ownerHeaders
        $source=GetApi ($url.Replace($projectId,'focusguard-staging-2026').Replace("services/$name","services/focusguard-$kind-staging")) $ownerHeaders
        Assert (($service.status.conditions | Where-Object type -eq 'Ready').status -eq 'True') "$name Ready"
        Assert (($source.status.conditions | Where-Object type -eq 'Ready').status -eq 'True') "$kind source preserved Ready"
        Assert ($service.metadata.annotations.'run.googleapis.com/maxScale' -eq '1' -and $service.spec.template.metadata.annotations.'autoscaling.knative.dev/minScale' -eq '0' -and $service.spec.template.metadata.annotations.'autoscaling.knative.dev/maxScale' -eq '1' -and $service.spec.template.metadata.annotations.'run.googleapis.com/cpu-throttling' -eq 'true') "$name request billing and scaling 0/1"
        $image=$service.spec.template.spec.containers[0].image
        if ($kind -eq 'identity') {
            Assert ($image.StartsWith("us-east4-docker.pkg.dev/$projectId/tnc-staging/") -and ($image -split '@')[1] -ceq [string]$gcpStaging.identityImageDigest) "$name target image pinned by digest in TNC registry"
        } else {
            Assert ($image.StartsWith("us-east4-docker.pkg.dev/$projectId/tnc-staging/api@") -and ($image -split '@')[1] -ceq $gcpStaging.apiImageDigest) 'API phase 2 image pinned by digest in target registry'
        }
        $runtime=if ($kind -eq 'identity') {'tnc-identity-runtime'} else {'tnc-api-runtime'}
        Assert ($service.spec.template.spec.serviceAccountName -ceq "$runtime@$projectId.iam.gserviceaccount.com") "$name dedicated target runtime identity"
        $env=$service.spec.template.spec.containers[0].env
        foreach ($entry in $env) {
            $original=$source.spec.template.spec.containers[0].env | Where-Object name -eq $entry.name
            if ($entry.valueFrom.secretKeyRef) {
                Assert ($entry.valueFrom.secretKeyRef.name -ceq ($original.valueFrom.secretKeyRef.name -replace '^focusguard-','tnc-') -and $entry.valueFrom.secretKeyRef.key -ceq '1') "$name secret reference $($entry.name)"
            } elseif ($kind -eq 'api' -and $entry.name -eq 'Identity__Authority') {
                Assert ($entry.value -ceq 'https://auth.focusguard.totemmassmedia.com/realms/focusguard') 'API authority uses exact realm issuer'
            } else { Assert ($entry.value -ceq $original.value) "$name environment preserved $($entry.name)" }
        }
        $urls[$kind]=$service.status.url
    }
    $secrets=GetApi "https://secretmanager.googleapis.com/v1/projects/$projectId/secrets" $ownerHeaders
    Assert (@($secrets.secrets).Count -eq 7 -and @($secrets.secrets | Where-Object {($_.name -split '/')[-1] -notlike 'tnc-*'}).Count -eq 0) 'Seven target secrets use tnc prefix'
    foreach ($path in @('/health/live','/health/ready')) {
        $response=Invoke-WebRequest -Uri ($urls.api+$path) -TimeoutSec 120 -SkipHttpErrorCheck
        Assert ($response.StatusCode -eq 200) "API $path returns 200"
    }
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/account') -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Anonymous account API denied with 401'
    foreach ($path in @('/v1/bootstrap/challenges','/v1/bootstrap/accounts','/v1/bootstrap/sessions/renew')) {
        $response=Invoke-WebRequest -Uri ($urls.api+$path) -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 120 -SkipHttpErrorCheck
        Assert ($response.StatusCode -eq 401) "Unauthenticated bootstrap denied with 401: $path"
    }
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/bootstrap/pairings') -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Unauthenticated pairing request denied with 401'
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/bootstrap/membership') -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Unauthenticated current membership read denied with 401'
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/bootstrap/pairings/pending') -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Unauthenticated pending pairing list denied with 401'
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/bootstrap/pairings/11111111-1111-4111-8111-111111111111') -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Unauthenticated pairing read denied with 401'
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/bootstrap/pairings/11111111-1111-4111-8111-111111111111/confirm') -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Unauthenticated device confirmation denied with 401'
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/pairings/11111111-1111-4111-8111-111111111111/approve') -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Unauthenticated membership approval denied with 401'
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/memberships/devices/11111111-1111-4111-8111-111111111111/revoke') -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Unauthenticated device revocation denied with 401'
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/documents/11111111-1111-4111-8111-111111111111') -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Unauthenticated encrypted-document read denied with 401'
    $response=Invoke-WebRequest -Uri ($urls.api+'/v1/documents/11111111-1111-4111-8111-111111111111') -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 120 -SkipHttpErrorCheck
    Assert ($response.StatusCode -eq 401) 'Unauthenticated encrypted-document write denied with 401'
    $discovery=Invoke-RestMethod ($urls.identity+'/realms/focusguard/.well-known/openid-configuration') -TimeoutSec 120
    Assert ($discovery.issuer -ceq 'https://auth.focusguard.totemmassmedia.com/realms/focusguard') 'OIDC discovery preserves product issuer'
    $collaboratorToken=(& $gc auth print-access-token --account=estuarlito@gmail.com).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $collaboratorToken) { throw 'Collaborator authentication unavailable.' }
    $collaboratorHeaders=@{Authorization='Bearer '+$collaboratorToken}
    $projectPerms=@(Permissions "https://cloudresourcemanager.googleapis.com/v1/projects/${projectId}:testIamPermissions" @('resourcemanager.projects.get','resourcemanager.projects.setIamPolicy','resourcemanager.projects.delete','run.services.update','logging.logEntries.list','resourcemanager.projects.createBillingAssignment') $collaboratorHeaders | Where-Object {$_})
    Assert ('resourcemanager.projects.get' -in $projectPerms -and 'run.services.update' -in $projectPerms -and 'logging.logEntries.list' -in $projectPerms) 'Collaborator can view project, deploy and read logs'
    Assert ('resourcemanager.projects.setIamPolicy' -notin $projectPerms -and 'resourcemanager.projects.delete' -notin $projectPerms -and 'resourcemanager.projects.createBillingAssignment' -notin $projectPerms) 'Collaborator cannot manage project IAM, delete project or link billing'
    $secretPerms=@(Permissions "https://secretmanager.googleapis.com/v1/projects/$projectId/secrets/tnc-keycloak-db-password:testIamPermissions" @('secretmanager.versions.access','secretmanager.secrets.setIamPolicy') $collaboratorHeaders | Where-Object {$_})
    Assert ($secretPerms.Count -eq 0) 'Collaborator direct secret access and secret IAM denied'
    foreach ($runtime in @('tnc-identity-runtime','tnc-api-runtime')) {
        $saPerms=@(Permissions "https://iam.googleapis.com/v1/projects/$projectId/serviceAccounts/$runtime@$projectId.iam.gserviceaccount.com:testIamPermissions" @('iam.serviceAccounts.actAs','iam.serviceAccounts.getAccessToken','iam.serviceAccounts.setIamPolicy') $collaboratorHeaders | Where-Object {$_})
        Assert ('iam.serviceAccounts.actAs' -in $saPerms -and 'iam.serviceAccounts.getAccessToken' -notin $saPerms -and 'iam.serviceAccounts.setIamPolicy' -notin $saPerms) "Collaborator only actAs on $runtime"
    }
    $repoPerms=@(Permissions "https://artifactregistry.googleapis.com/v1/projects/$projectId/locations/$($gcpStaging.region)/repositories/tnc-staging:testIamPermissions" @('artifactregistry.repositories.downloadArtifacts','artifactregistry.repositories.uploadArtifacts','artifactregistry.repositories.setIamPolicy') $collaboratorHeaders | Where-Object {$_})
    Assert ('artifactregistry.repositories.downloadArtifacts' -in $repoPerms -and 'artifactregistry.repositories.uploadArtifacts' -notin $repoPerms -and 'artifactregistry.repositories.setIamPolicy' -notin $repoPerms) 'Collaborator target repository read only'
    $evidence=@{verifiedAtUtc=[DateTime]::UtcNow.ToString('o');projectId=$projectId;displayName=$project.name;projectNumber=$project.projectNumber;urls=$urls;checks=@($checks.ToArray());collaborator='estuarlito@gmail.com';sharedNeon=$true;domainsMoved=$false;sourcePreserved=$true}
    $stage=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts/cloud-phase1/staging'
    $evidence | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $stage 'tnc-clone-verification.json') -Encoding utf8
    Write-Output "VERIFIED: $($checks.Count) checks; no secret values recorded."
} finally {$ownerHeaders=$null;$token=$null;$collaboratorToken=$null;$collaboratorHeaders=$null}
