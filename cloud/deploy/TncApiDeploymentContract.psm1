$script:SupportedApiImageTagPattern = '^(?:phase2|phase3)-[a-z0-9-]{1,48}$'

function Test-TncApiImageTag {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$ImageTag)

    [regex]::IsMatch($ImageTag, $script:SupportedApiImageTagPattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

Export-ModuleMember -Function Test-TncApiImageTag
