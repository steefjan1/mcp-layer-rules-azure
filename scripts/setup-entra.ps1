# Creates the Entra ID app registration that represents the Stay API, with three app roles,
# and stores its client id in the azd environment as STAY_API_APP_CLIENT_ID.
#
#   .\scripts\setup-entra.ps1
#
# Run BEFORE `azd up` so the bicep can switch on built-in authentication for the API app.
# Requires: az login with rights to create app registrations. Windows PowerShell 5.1 safe.

[CmdletBinding()]
param(
    [string] $DisplayName = 'stay-api-mcp-layer-rules'
)
$ErrorActionPreference = 'Stop'

function Invoke-Az {
    # Parameter must not be called $Args: that is a PowerShell automatic variable and the splat would be empty.
    param([string[]] $AzArgs)
    $out = & az @AzArgs
    if ($LASTEXITCODE -ne 0) { throw "az $($AzArgs -join ' ') failed with exit code $LASTEXITCODE" }
    return $out
}

# App roles. Application-type roles can be assigned to a managed identity's service principal.
$roles = @(
    @{ id = [guid]::NewGuid().ToString(); allowedMemberTypes = @('Application'); displayName = 'Stays.Read';       value = 'Stays.Read';       description = 'Search listings and create quotes';  isEnabled = $true },
    @{ id = [guid]::NewGuid().ToString(); allowedMemberTypes = @('Application'); displayName = 'Bookings.Create';  value = 'Bookings.Create';  description = 'Create bookings from signed quotes'; isEnabled = $true },
    @{ id = [guid]::NewGuid().ToString(); allowedMemberTypes = @('Application'); displayName = 'Bookings.Cancel';  value = 'Bookings.Cancel';  description = 'Cancel bookings';                   isEnabled = $true }
)

$existing = Invoke-Az @('ad', 'app', 'list', '--display-name', $DisplayName, '--query', '[0].appId', '-o', 'tsv')
if ($existing) {
    $appId = $existing.Trim()
    Write-Host "App registration '$DisplayName' already exists: $appId"
} else {
    $rolesFile = Join-Path $env:TEMP 'stay-app-roles.json'
    ($roles | ConvertTo-Json -Depth 5) | Set-Content -Path $rolesFile -Encoding UTF8
    $appId = (Invoke-Az @('ad', 'app', 'create', '--display-name', $DisplayName, '--sign-in-audience', 'AzureADMyOrg', '--app-roles', "@$rolesFile", '--query', 'appId', '-o', 'tsv')).Trim()
    Remove-Item $rolesFile -ErrorAction SilentlyContinue
    Write-Host "Created app registration '$DisplayName': $appId"
}

# Identifier URI = the audience the MCP identity asks a token for (api://<appId>/.default).
Invoke-Az @('ad', 'app', 'update', '--id', $appId, '--identifier-uris', "api://$appId") | Out-Null

# A service principal is required before roles can be assigned to anyone.
$sp = Invoke-Az @('ad', 'sp', 'list', '--filter', "appId eq '$appId'", '--query', '[0].id', '-o', 'tsv')
if (-not $sp) {
    Invoke-Az @('ad', 'sp', 'create', '--id', $appId) | Out-Null
    Write-Host 'Created service principal.'
}

azd env set STAY_API_APP_CLIENT_ID $appId
if ($LASTEXITCODE -ne 0) { throw "azd env set failed with exit code $LASTEXITCODE" }

Write-Host ''
Write-Host "STAY_API_APP_CLIENT_ID=$appId stored. Now run: azd up, then .\scripts\grant-roles.ps1"
