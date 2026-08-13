[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('append', 'replace')]
    [string] $Mode,

    [Parameter(Mandatory)]
    [object[]] $Tabs,

    [string] $CollectionName,

    [bool] $SaveState = $true
)

$package = [ordered]@{
    version   = 1
    source    = 'PowerShell'
    mode      = $Mode
    saveState = $SaveState
    tabs      = $Tabs
}

if (-not [string]::IsNullOrWhiteSpace($CollectionName)) {
    $package.collectionName = $CollectionName.Trim()
}

$json = $package | ConvertTo-Json -Depth 8 -Compress
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
$payload = [System.Convert]::ToBase64String($bytes).
    TrimEnd('=').
    Replace('+', '-').
    Replace('/', '_')
$uri = "link2scape://tabs/active?payload=$payload"

Start-Process -FilePath $uri

