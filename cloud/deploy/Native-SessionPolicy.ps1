function Set-FocusGuardNativeSessionPolicy {
    param([Parameter(Mandatory)][string]$Base,
        [Parameter(Mandatory)][string]$Realm,
        [Parameter(Mandatory)][hashtable]$Headers,
        [switch]$VerifyOnly)
    $prefix=$Base.TrimEnd('/')+'/admin/realms/'+[Uri]::EscapeDataString($Realm)
    $clients=Invoke-RestMethod ($prefix+'/clients?clientId=focusguard-native') -Headers $Headers -TimeoutSec 30
    $client=$clients | Select-Object -First 1
    if (-not $client -or -not $client.publicClient -or $client.directAccessGrantsEnabled -or $client.implicitFlowEnabled) {
        throw 'Native public-client policy is unavailable or unsafe.'
    }
    $scopes=Invoke-RestMethod ($prefix+'/client-scopes') -Headers $Headers -TimeoutSec 30
    $scope=$scopes | Where-Object name -eq 'offline_access' | Select-Object -First 1
    if (-not $scope -and -not $VerifyOnly) {
        $scopeBody=@{name='offline_access';protocol='openid-connect';attributes=@{'include.in.token.scope'='true';'display.on.consent.screen'='true'}}
        Invoke-RestMethod -Method Post -Uri ($prefix+'/client-scopes') -Headers $Headers -ContentType application/json -Body ($scopeBody|ConvertTo-Json -Depth 4 -Compress) -TimeoutSec 30 | Out-Null
        $scopes=Invoke-RestMethod ($prefix+'/client-scopes') -Headers $Headers -TimeoutSec 30
        $scope=$scopes | Where-Object name -eq 'offline_access' | Select-Object -First 1
    }
    if (-not $scope) { throw 'Keycloak offline_access scope is unavailable.' }
    if (-not $VerifyOnly) {
        # A finite offline lifetime; browser session limits and access-token lifetime stay unchanged.
        $policy=@{offlineSessionIdleTimeout=2592000;offlineSessionMaxLifespanEnabled=$true;offlineSessionMaxLifespan=2592000}
        Invoke-RestMethod -Method Put -Uri $prefix -Headers $Headers -ContentType application/json -Body ($policy|ConvertTo-Json -Compress) -TimeoutSec 30 | Out-Null
        Invoke-RestMethod -Method Put -Uri ($prefix+'/clients/'+$client.id+'/optional-client-scopes/'+$scope.id) -Headers $Headers -TimeoutSec 30 | Out-Null
        $role=Invoke-RestMethod ($prefix+'/roles/offline_access') -Headers $Headers -TimeoutSec 30
        Invoke-RestMethod -Method Post -Uri ($prefix+'/client-scopes/'+$scope.id+'/scope-mappings/realm') -Headers $Headers -ContentType application/json -Body (ConvertTo-Json -InputObject @($role) -Depth 5 -Compress) -TimeoutSec 30 | Out-Null
        $realmState=Invoke-RestMethod $prefix -Headers $Headers -TimeoutSec 30
        if (-not $realmState.defaultRole.id) { throw 'Realm default role is unavailable.' }
        # This built-in role permits offline sessions only for clients explicitly allowed the scope.
        # It grants no product/API or administrator permissions.
        Invoke-RestMethod -Method Post -Uri ($prefix+'/roles-by-id/'+$realmState.defaultRole.id+'/composites') -Headers $Headers -ContentType application/json -Body (ConvertTo-Json -InputObject @($role) -Depth 5 -Compress) -TimeoutSec 30 | Out-Null
        # Imported/previously provisioned accounts may have no default-role mapping.
        # Migrate only the built-in offline_access capability; keep all other roles unchanged.
        $first=0
        do {
            $users=Invoke-RestMethod ($prefix+'/users?first='+$first+'&max=100') -Headers $Headers -TimeoutSec 30
            foreach ($user in $users) {
                $effective=Invoke-RestMethod ($prefix+'/users/'+$user.id+'/role-mappings/realm/composite') -Headers $Headers -TimeoutSec 30
                if (-not ($effective|Where-Object name -eq 'offline_access')) {
                    Invoke-RestMethod -Method Post -Uri ($prefix+'/users/'+$user.id+'/role-mappings/realm') -Headers $Headers -ContentType application/json -Body (ConvertTo-Json -InputObject @($role) -Depth 5 -Compress) -TimeoutSec 30 | Out-Null
                }
            }
            $first+=100
        } while (@($users).Count -eq 100)
    }
    $actual=Invoke-RestMethod $prefix -Headers $Headers -TimeoutSec 30
    $optional=Invoke-RestMethod ($prefix+'/clients/'+$client.id+'/optional-client-scopes') -Headers $Headers -TimeoutSec 30
    $defaultRoles=Invoke-RestMethod ($prefix+'/roles-by-id/'+$actual.defaultRole.id+'/composites') -Headers $Headers -TimeoutSec 30
    if (-not $actual.offlineSessionMaxLifespanEnabled -or $actual.offlineSessionIdleTimeout -ne 2592000 -or
        $actual.offlineSessionMaxLifespan -ne 2592000 -or -not ($optional|Where-Object name -eq 'offline_access') -or
        -not ($defaultRoles|Where-Object name -eq 'offline_access') -or
        -not $actual.revokeRefreshToken -or $actual.refreshTokenMaxReuse -ne 0) {
        throw 'Bounded persistent native session policy verification failed.'
    }
    Write-Output 'PASS native persistent session: 30-day limit, optional scope, one-use rotation.'
}
