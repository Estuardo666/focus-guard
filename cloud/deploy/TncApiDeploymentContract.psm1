$script:SupportedApiImageTagPattern = '^(?:phase2|phase3)-[a-z0-9-]{1,48}$'

function Test-TncApiImageTag {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$ImageTag)

    [regex]::IsMatch($ImageTag, $script:SupportedApiImageTagPattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Test-TncApiServiceStable {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Service)

    $status = $Service.status
    if ($null -eq $status) { return $false }

    $latestCreated = [string]$status.latestCreatedRevisionName
    $latestReady = [string]$status.latestReadyRevisionName
    if ([string]::IsNullOrWhiteSpace($latestCreated) -or
        [string]::IsNullOrWhiteSpace($latestReady) -or
        $latestCreated -cne $latestReady) {
        return $false
    }

    $readyCondition = @($status.conditions | Where-Object { $_.type -ceq 'Ready' } | Select-Object -First 1)
    return $readyCondition.Count -eq 1 -and $readyCondition[0].status -ceq 'True'
}

Export-ModuleMember -Function Test-TncApiImageTag, Test-TncApiServiceStable
