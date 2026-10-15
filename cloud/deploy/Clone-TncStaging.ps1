param([switch]$GrantCollaborator)
$ErrorActionPreference = 'Stop'
$sourceProject = 'focusguard-staging-2026'
$targetProject = 'tnc-staging-2026'
$region = 'us-east4'
$account = 'transcitynorwalk@gmail.com'
$gc = 'C:\Program Files (x86)\Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd'
$env:PATH = (Split-Path $gc) + ';' + $env:PATH
$token = (& $gc auth print-access-token --account=$account).Trim()
if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'Google authentication unavailable.' }
$headers = @{ Authorization = 'Bearer ' + $token }
function Api([string]$Method, [string]$Uri, $Body = $null) {
    $args = @{Method=$Method; Uri=$Uri; Headers=$headers; TimeoutSec=120}
    if ($null -ne $Body) { $args.ContentType='application/json'; $args.Body=ConvertTo-Json -InputObject $Body -Depth 60 -Compress }
    try { return Invoke-RestMethod @args }
    catch { throw "Google API $Method failed (HTTP $([int]$_.Exception.Response.StatusCode)) for $Uri. Response omitted to protect secret payloads." }
}
function Grant([string]$Resource, [string]$Role, [string]$Member) {
    $getMethod = if ($Resource -match '^https://(cloudresourcemanager|iam)\.googleapis\.com/') { 'POST' } else { 'GET' }
    $policy = Api $getMethod ($Resource + ':getIamPolicy')
    $bindings = @($policy.bindings | Where-Object { $null -ne $_ })
    $binding = $bindings | Where-Object role -eq $Role | Select-Object -First 1
    if ($binding) {
        if ($Member -in $binding.members) { return }
        $binding.members = @($binding.members) + $Member
    } else { $bindings += @{role=$Role;members=@($Member)} }
    $body = @{policy=@{version=1;bindings=$bindings}}
    if ($policy.etag) { $body.policy.etag=$policy.etag }
    $null = Api 'POST' ($Resource + ':setIamPolicy') $body
}
$project = Api 'GET' "https://cloudresourcemanager.googleapis.com/v1/projects/$targetProject"
$services = @{}
foreach ($name in @('focusguard-identity-staging','focusguard-api-staging')) {
    $services[$name] = Api 'GET' "https://${region}-run.googleapis.com/apis/serving.knative.dev/v1/namespaces/$sourceProject/services/$name"
}
$runtimeNames = @{ 'focusguard-identity-staging'='tnc-identity-runtime'; 'focusguard-api-staging'='tnc-api-runtime' }
$accounts = Api 'GET' "https://iam.googleapis.com/v1/projects/$targetProject/serviceAccounts"
foreach ($runtime in $runtimeNames.Values) {
    $email = "$runtime@$targetProject.iam.gserviceaccount.com"
    if ($email -notin @($accounts.accounts.email)) {
        $null = Api 'POST' "https://iam.googleapis.com/v1/projects/$targetProject/serviceAccounts" @{accountId=$runtime;serviceAccount=@{displayName=$runtime}}
    }
}
$repoResource = "https://artifactregistry.googleapis.com/v1/projects/$targetProject/locations/$region"
$repos = Api 'GET' "$repoResource/repositories"
if (-not ($repos.repositories | Where-Object name -like '*/tnc-staging')) {
    $null = Api 'POST' "$repoResource/repositories?repositoryId=tnc-staging" @{format='DOCKER';description='TNC staging pinned runtime images'}
}
$usedVersions = @{}
foreach ($svc in $services.Values) {
    foreach ($envItem in $svc.spec.template.spec.containers.env) {
        if ($envItem.valueFrom.secretKeyRef) { $usedVersions[$envItem.valueFrom.secretKeyRef.name]=$envItem.valueFrom.secretKeyRef.key }
    }
}
$sourceSecrets = Api 'GET' "https://secretmanager.googleapis.com/v1/projects/$sourceProject/secrets"
$existingSecrets = Api 'GET' "https://secretmanager.googleapis.com/v1/projects/$targetProject/secrets"
foreach ($secret in $sourceSecrets.secrets) {
    $oldName = ($secret.name -split '/')[-1]
    $newName = $oldName -replace '^focusguard-','tnc-'
    $version = if ($usedVersions.ContainsKey($oldName)) { $usedVersions[$oldName] } else { 'latest' }
    $sourceValue = Api 'GET' "https://secretmanager.googleapis.com/v1/$($secret.name)/versions/${version}:access"
    $resource = "projects/$targetProject/secrets/$newName"
    if ($newName -notin @($existingSecrets.secrets | ForEach-Object { ($_.name -split '/')[-1] })) {
        $null = Api 'POST' "https://secretmanager.googleapis.com/v1/projects/$targetProject/secrets?secretId=$newName" @{replication=@{automatic=@{}}}
        $null = Api 'POST' "https://secretmanager.googleapis.com/v1/${resource}:addVersion" @{payload=@{data=$sourceValue.payload.data}}
    }
    $targetValue = Api 'GET' "https://secretmanager.googleapis.com/v1/${resource}/versions/1:access"
    if ($sourceValue.payload.data -cne $targetValue.payload.data) { throw "Secret clone mismatch: $newName" }
    $sourceValue=$null; $targetValue=$null
    Write-Output "Secret verified: $newName (values omitted)"
}
foreach ($oldName in $services.Keys) {
    $runtimeEmail = "$($runtimeNames[$oldName])@$targetProject.iam.gserviceaccount.com"
    foreach ($envItem in $services[$oldName].spec.template.spec.containers.env) {
        if ($envItem.valueFrom.secretKeyRef) {
            $secretName = $envItem.valueFrom.secretKeyRef.name -replace '^focusguard-','tnc-'
            Grant "https://secretmanager.googleapis.com/v1/projects/$targetProject/secrets/$secretName" 'roles/secretmanager.secretAccessor' "serviceAccount:$runtimeEmail"
        }
    }
}
& $gc auth configure-docker "${region}-docker.pkg.dev" --quiet
if ($LASTEXITCODE -ne 0) { throw 'Registry credential helper configuration failed.' }
foreach ($oldName in $services.Keys) {
    $svc = $services[$oldName]
    $image = $svc.spec.template.spec.containers[0].image
    $imageName = (($image -split '/')[-1] -split '@')[0]
    $targetImage = "${region}-docker.pkg.dev/$targetProject/tnc-staging/${imageName}:phase1"
    & docker pull $image
    if ($LASTEXITCODE -ne 0) { throw "Image pull failed: $imageName" }
    & docker tag $image $targetImage
    if ($LASTEXITCODE -ne 0) { throw "Image tag failed: $imageName" }
    & docker push $targetImage
    if ($LASTEXITCODE -ne 0) { throw "Image push failed: $imageName" }
    $digest = (& $gc artifacts docker images describe $targetImage --project=$targetProject --account=$account --format='value(image_summary.digest)').Trim()
    if ($LASTEXITCODE -ne 0 -or $digest -notmatch '^sha256:[0-9a-f]{64}$') { throw 'Target image digest unavailable.' }
    if ($digest -cne ($image -split '@')[1]) { throw "Image digest changed: $imageName. Deployment stopped." }
    $svc.spec.template.spec.containers[0].image = ($targetImage -split ':phase1')[0] + '@' + $digest
    $svc.spec.template.spec.serviceAccountName = "$($runtimeNames[$oldName])@$targetProject.iam.gserviceaccount.com"
    foreach ($envItem in $svc.spec.template.spec.containers.env) {
        if ($envItem.valueFrom.secretKeyRef) {
            $envItem.valueFrom.secretKeyRef.name = $envItem.valueFrom.secretKeyRef.name -replace '^focusguard-','tnc-'
            $envItem.valueFrom.secretKeyRef.key = '1'
        }
    }
    $newName = $oldName -replace '^focusguard-','tnc-'
    $svc.spec.template.metadata = @{annotations=@{'autoscaling.knative.dev/minScale'='0';'autoscaling.knative.dev/maxScale'='1';'run.googleapis.com/cpu-throttling'='true';'run.googleapis.com/startup-cpu-boost'='true'}}
    $body = @{apiVersion='serving.knative.dev/v1';kind='Service';metadata=@{name=$newName;namespace=[string]$project.projectNumber;annotations=@{'run.googleapis.com/ingress'='all';'run.googleapis.com/minScale'='0';'run.googleapis.com/maxScale'='1'}};spec=$svc.spec}
    $serviceResource = "https://${region}-run.googleapis.com/apis/serving.knative.dev/v1/namespaces/$targetProject/services/$newName"
    $list = Api 'GET' "https://${region}-run.googleapis.com/apis/serving.knative.dev/v1/namespaces/$targetProject/services"
    if ($newName -notin @($list.items.metadata.name)) {
        $null = Api 'POST' "https://${region}-run.googleapis.com/apis/serving.knative.dev/v1/namespaces/$targetProject/services" $body
    }
    $ready=$false
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        $state = Api 'GET' $serviceResource
        $condition=$state.status.conditions | Where-Object type -eq 'Ready'
        if ($condition.status -eq 'True') { $ready=$true; break }
        if ($condition.status -eq 'False') { throw "Service not Ready: $newName ($($condition.reason)): $($condition.message)" }
        Start-Sleep -Seconds 5
    }
    if (-not $ready) { throw "Service readiness timed out: $newName" }
    $sourcePolicy=Api 'GET' "https://${region}-run.googleapis.com/v1/projects/$sourceProject/locations/$region/services/${oldName}:getIamPolicy"
    if (($sourcePolicy.bindings | Where-Object role -eq 'roles/run.invoker').members -contains 'allUsers') {
        Grant "https://${region}-run.googleapis.com/v1/projects/$targetProject/locations/$region/services/$newName" 'roles/run.invoker' 'allUsers'
    }
    $paths = if ($imageName -eq 'api') { @('/health/live','/health/ready') } else { @('/realms/focusguard/.well-known/openid-configuration') }
    foreach ($path in $paths) {
        $response=Invoke-WebRequest -Uri ($state.status.url+$path) -TimeoutSec 120 -UseBasicParsing
        if ($response.StatusCode -ne 200) { throw "Smoke check failed: $newName $path" }
        if ($imageName -ne 'api') {
            $discovery=$response.Content | ConvertFrom-Json
            if ($discovery.issuer -cne 'https://auth.focusguard.totemmassmedia.com/realms/focusguard') { throw 'Cloned identity issuer changed.' }
        }
    }
    Write-Output "Ready and HTTP 200 smoke checks: $newName $($state.status.url)"
}
if ($GrantCollaborator) {
    # Deployment can indirectly access runtime secrets; do not grant Owner, Editor,
    # billing administration, project IAM administration, or direct secret access.
    foreach ($role in @('roles/run.developer','roles/logging.viewer','roles/browser')) {
        Grant "https://cloudresourcemanager.googleapis.com/v1/projects/$targetProject" $role 'user:estuarlito@gmail.com'
    }
    Grant "https://artifactregistry.googleapis.com/v1/projects/$targetProject/locations/$region/repositories/tnc-staging" 'roles/artifactregistry.reader' 'user:estuarlito@gmail.com'
    foreach ($runtime in $runtimeNames.Values) {
        Grant "https://iam.googleapis.com/v1/projects/$targetProject/serviceAccounts/$runtime@$targetProject.iam.gserviceaccount.com" 'roles/iam.serviceAccountUser' 'user:estuarlito@gmail.com'
    }
    Write-Output 'Collaborator granted: estuarlito@gmail.com; project roles run.developer/logging.viewer/browser; repository reader; actAs on the two runtime identities.'
}
$headers=$null; $token=$null
