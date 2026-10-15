Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$docker = Get-Command docker -ErrorAction Stop
$runId = [Guid]::NewGuid().ToString('N')
$label = 'focusguard.scope=phase3-api-container-smoke'
$imageTag = "focusguard-api-container-smoke:$runId"
$databaseName = "focusguard-api-smoke-db-$runId"
$apiName = "focusguard-api-smoke-$runId"
$apiMismatchName = "focusguard-api-smoke-wrong-host-$runId"
$volumeName = "focusguard-api-smoke-pg-$runId"
$certificateDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "FocusGuard-ApiTlsSmoke-$runId"
$password = [Guid]::NewGuid().ToString('N')
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$hostPort = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$http = $null
$rootKey = $null
$rootCertificate = $null
$serverKey = $null
$serverCertificate = $null

try {
    $buildOutput = @(& $docker.Source build --quiet --platform linux/amd64 --file cloud/api/Dockerfile --tag $imageTag . 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Select-Object -Last 30 | Write-Output
        throw 'Building the API Docker image failed.'
    }
    Write-Output "Built API image $($buildOutput | Select-Object -Last 1)."

    New-Item -ItemType Directory -Path $certificateDirectory | Out-Null
    $notBefore = [DateTimeOffset]::UtcNow.AddDays(-1)
    $notAfter = [DateTimeOffset]::UtcNow.AddDays(1)
    $hashAlgorithm = [System.Security.Cryptography.HashAlgorithmName]::SHA256
    $signaturePadding = [System.Security.Cryptography.RSASignaturePadding]::Pkcs1

    $rootKey = [System.Security.Cryptography.RSA]::Create(2048)
    $rootRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=FocusGuard Phase 3 API smoke root', $rootKey, $hashAlgorithm, $signaturePadding)
    $null = $rootRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
    $rootKeyUsage = [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign
    $null = $rootRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($rootKeyUsage, $true))
    $rootCertificate = $rootRequest.CreateSelfSigned($notBefore, $notAfter)

    $serverKey = [System.Security.Cryptography.RSA]::Create(2048)
    $serverRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=127.0.0.1', $serverKey, $hashAlgorithm, $signaturePadding)
    $subjectAlternativeNames = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $subjectAlternativeNames.AddIpAddress([System.Net.IPAddress]::Loopback)
    $null = $serverRequest.CertificateExtensions.Add($subjectAlternativeNames.Build())
    $null = $serverRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $serverKeyUsage = [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment
    $null = $serverRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($serverKeyUsage, $true))
    $serverCertificate = $serverRequest.Create($rootCertificate, $notBefore, $notAfter,
        [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16))

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText((Join-Path $certificateDirectory 'root.crt'),
        $rootCertificate.ExportCertificatePem(), $utf8NoBom)
    [System.IO.File]::WriteAllText((Join-Path $certificateDirectory 'postgres.crt'),
        $serverCertificate.ExportCertificatePem(), $utf8NoBom)
    [System.IO.File]::WriteAllText((Join-Path $certificateDirectory 'postgres.key'),
        $serverKey.ExportPkcs8PrivateKeyPem(), $utf8NoBom)

    $volumeOutput = @(& $docker.Source volume create --label $label $volumeName 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $volumeOutput | Write-Output
        throw 'Creating the isolated PostgreSQL volume failed.'
    }

    $postgresStart = @'
set -eu
mkdir -p /tmp/phase3-pg-tls
cp /certs/postgres.crt /tmp/phase3-pg-tls/postgres.crt
cp /certs/postgres.key /tmp/phase3-pg-tls/postgres.key
chown postgres:postgres /tmp/phase3-pg-tls/postgres.crt /tmp/phase3-pg-tls/postgres.key
chmod 600 /tmp/phase3-pg-tls/postgres.key
exec docker-entrypoint.sh postgres -c ssl=on -c ssl_cert_file=/tmp/phase3-pg-tls/postgres.crt -c ssl_key_file=/tmp/phase3-pg-tls/postgres.key
'@
    $certificateMount = "type=bind,source=$certificateDirectory,target=/certs,readonly"
    $databaseOutput = @(& $docker.Source run --detach --name $databaseName --label $label --memory 256m --cpus 0.5 `
        --publish ('127.0.0.1:{0}:8080' -f $hostPort) --mount ("type=volume,src=$volumeName,dst=/var/lib/postgresql/data") `
        --mount $certificateMount --env 'POSTGRES_USER=focusguard' --env ("POSTGRES_PASSWORD=$password") `
        --env 'POSTGRES_DB=focusguard_product' --entrypoint /bin/sh postgres:17-alpine -c $postgresStart 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $databaseOutput | Write-Output
        throw 'Starting the isolated PostgreSQL container failed.'
    }

    $databaseReady = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        & $docker.Source exec $databaseName pg_isready -U focusguard -d focusguard_product *> $null
        if ($LASTEXITCODE -eq 0) {
            $databaseReady = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $databaseReady) {
        & $docker.Source logs --tail 30 $databaseName
        throw 'The isolated PostgreSQL container did not become ready.'
    }

    $connectionString = "Host=127.0.0.1;Port=5432;Database=focusguard_product;Username=focusguard;Password=$password;SSL Mode=VerifyFull;Root Certificate=/certs/phase3-root.crt"
    $rootCertificateMount = "type=bind,source=$(Join-Path $certificateDirectory 'root.crt'),target=/certs/phase3-root.crt,readonly"
    $apiOutput = @(& $docker.Source run --detach --name $apiName --label $label --network "container:$databaseName" `
        --mount $rootCertificateMount --env 'ASPNETCORE_ENVIRONMENT=Production' `
        --env 'Identity__Authority=https://identity.example.invalid/realms/focusguard' `
        --env ("ConnectionStrings__Product=$connectionString") $imageTag 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $apiOutput | Write-Output
        throw 'Starting the API container failed.'
    }

    $http = [System.Net.Http.HttpClient]::new()
    $http.Timeout = [TimeSpan]::FromSeconds(2)
    $liveResponse = $null
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $containerState = (& $docker.Source inspect --format '{{.State.Status}}' $apiName 2>$null).Trim()
        if ($containerState -ne 'running') {
            & $docker.Source logs --tail 40 $apiName
            throw "The API container exited during startup ($containerState)."
        }
        try {
            $liveResponse = $http.GetAsync("http://127.0.0.1:$hostPort/health/live").GetAwaiter().GetResult()
            if ([int]$liveResponse.StatusCode -eq 200) { break }
            $liveResponse.Dispose()
            $liveResponse = $null
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if ($null -eq $liveResponse) {
        & $docker.Source logs --tail 40 $apiName
        throw 'The API container did not pass its liveness check.'
    }
    Write-Output "GET /health/live => $([int]$liveResponse.StatusCode)."
    $liveResponse.Dispose()

    $readyResponse = $http.GetAsync("http://127.0.0.1:$hostPort/health/ready").GetAwaiter().GetResult()
    if ([int]$readyResponse.StatusCode -ne 200) {
        $statusCode = [int]$readyResponse.StatusCode
        $readyResponse.Dispose()
        throw "API readiness returned HTTP $statusCode with PostgreSQL available."
    }
    Write-Output "GET /health/ready => $([int]$readyResponse.StatusCode)."
    $readyResponse.Dispose()

    $cursorResponse = $http.GetAsync("http://127.0.0.1:$hostPort/v1/sync/cursor").GetAwaiter().GetResult()
    if ([int]$cursorResponse.StatusCode -ne 401) {
        $statusCode = [int]$cursorResponse.StatusCode
        $cursorResponse.Dispose()
        throw "Unauthenticated cursor request returned HTTP $statusCode instead of 401."
    }
    Write-Output "GET /v1/sync/cursor without bearer => $([int]$cursorResponse.StatusCode)."
    $cursorResponse.Dispose()

    & $docker.Source rm --force $apiName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop the successful API smoke container before the TLS-negative case.' }
    $mismatchConnectionString = "Host=localhost;Port=5432;Database=focusguard_product;Username=focusguard;Password=$password;SSL Mode=VerifyFull;Root Certificate=/certs/phase3-root.crt"
    $mismatchOutput = @(& $docker.Source run --detach --name $apiMismatchName --label $label `
        --network "container:$databaseName" --mount $rootCertificateMount `
        --env 'ASPNETCORE_ENVIRONMENT=Production' `
        --env 'Identity__Authority=https://identity.example.invalid/realms/focusguard' `
        --env ("ConnectionStrings__Product=$mismatchConnectionString") $imageTag 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $mismatchOutput | Write-Output
        throw 'Starting the TLS hostname-mismatch negative case failed before API startup.'
    }

    $mismatchState = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $mismatchState = (& $docker.Source inspect --format '{{.State.Status}}' $apiMismatchName 2>$null).Trim()
        if ($mismatchState -eq 'exited') { break }
        Start-Sleep -Milliseconds 500
    }
    if ($mismatchState -ne 'exited') {
        & $docker.Source logs --tail 40 $apiMismatchName
        throw "The API accepted a PostgreSQL certificate for the wrong hostname (state=$mismatchState)."
    }
    $mismatchExitCode = [int](& $docker.Source inspect --format '{{.State.ExitCode}}' $apiMismatchName)
    $mismatchLogs = (@(& $docker.Source logs $apiMismatchName 2>&1) -join "`n")
    if ($mismatchExitCode -eq 0 -or $mismatchLogs -notmatch '(?i)(certificate|SSL|TLS|RemoteCertificate|hostname|principal name)') {
        Write-Output $mismatchLogs
        throw 'The wrong-host negative case did not fail for a PostgreSQL certificate validation error.'
    }
    Write-Output 'PostgreSQL TLS VerifyFull rejected a certificate whose SAN does not match localhost.'
    Write-Output 'API Docker container smoke passed.'
}
finally {
    if ($http) { $http.Dispose() }
    if ($serverCertificate) { $serverCertificate.Dispose() }
    if ($serverKey) { $serverKey.Dispose() }
    if ($rootCertificate) { $rootCertificate.Dispose() }
    if ($rootKey) { $rootKey.Dispose() }
    foreach ($containerName in @($apiName, $apiMismatchName, $databaseName)) {
        $existingContainer = @(& $docker.Source ps --all --quiet --filter ("name=^/{0}$" -f $containerName))
        if ($existingContainer.Count -gt 0) {
            & $docker.Source rm --volumes --force $containerName | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not clean temporary container $containerName." }
        }
    }
    & $docker.Source volume inspect $volumeName *> $null
    if ($LASTEXITCODE -eq 0) {
        & $docker.Source volume rm $volumeName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not remove the temporary PostgreSQL volume.' }
    }
    & $docker.Source image inspect $imageTag *> $null
    if ($LASTEXITCODE -eq 0) {
        & $docker.Source image rm $imageTag | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not remove the temporary API image.' }
    }

    foreach ($containerName in @($apiName, $apiMismatchName, $databaseName)) {
        $remainingContainer = @(& $docker.Source ps --all --quiet --filter ("name=^/{0}$" -f $containerName))
        if ($remainingContainer.Count -gt 0) { throw "Temporary container $containerName remains after cleanup." }
    }
    & $docker.Source volume inspect $volumeName *> $null
    if ($LASTEXITCODE -eq 0) { throw 'The temporary PostgreSQL volume remains after cleanup.' }
    & $docker.Source image inspect $imageTag *> $null
    if ($LASTEXITCODE -eq 0) { throw 'The temporary API image remains after cleanup.' }

    if (Test-Path -LiteralPath $certificateDirectory) {
        $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        $resolvedCertificateDirectory = [System.IO.Path]::GetFullPath($certificateDirectory)
        $expectedDirectoryName = "FocusGuard-ApiTlsSmoke-$runId"
        if (-not $resolvedCertificateDirectory.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -or
            [System.IO.Path]::GetFileName($resolvedCertificateDirectory) -ne $expectedDirectoryName) {
            throw "Refusing to remove unexpected TLS smoke directory: $resolvedCertificateDirectory"
        }
        Remove-Item -LiteralPath $resolvedCertificateDirectory -Recurse -Force
    }
}
