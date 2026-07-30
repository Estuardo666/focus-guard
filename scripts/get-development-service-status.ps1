[CmdletBinding()]
param()

$serviceName = 'FocusGuard.Development'
$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Output "$serviceName is not installed."
    return
}

$service | Select-Object Name, DisplayName, State, StartMode, StartName, PathName
