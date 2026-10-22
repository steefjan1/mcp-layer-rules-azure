# Assigns app roles on the Stay API app registration to the MCP server's managed identity.
# This is the single place where "what may the agent change" is decided. Leave a role out
# and the matching tool returns "forbidden" prose while the REST door keeps working for
# callers that do hold it.
#
#   .\scripts\grant-roles.ps1                              # Stays.Read + Bookings.Create (default)
#   .\scripts\grant-roles.ps1 -Roles Stays.Read            # read-only agent: book_stay gets 403
#   .\scripts\grant-roles.ps1 -Roles Stays.Read,Bookings.Create,Bookings.Cancel
#
# Run AFTER `azd up` (needs the identity's principal id from the azd outputs).
# Windows PowerShell 5.1 safe. Uses Microsoft Graph via `az rest`.

[CmdletBinding()]
param(
    [string[]] $Roles = @('Stays.Read', 'Bookings.Create')
)
$ErrorActionPreference = 'Stop'

function Invoke-Az {
    # Parameter must not be called $Args: that is a PowerShell automatic variable and the splat would be empty.
    param([string[]] $AzArgs)
    $out = & az @AzArgs
    if ($LASTEXITCODE -ne 0) { throw "az $($AzArgs -join ' ') failed with exit code $LASTEXITCODE" }
    return $out
}

$appId = (azd env get-value STAY_API_APP_CLIENT_ID)
if ($LASTEXITCODE -ne 0 -or -not $appId) { throw 'STAY_API_APP_CLIENT_ID missing; run scripts/setup-entra.ps1 first.' }
$mcpPrincipalId = (azd env get-value STAY_MCP_PRINCIPAL_ID)
if ($LASTEXITCODE -ne 0 -or -not $mcpPrincipalId) { throw 'STAY_MCP_PRINCIPAL_ID missing; run azd up first.' }

$apiSpJson = Invoke-Az @('ad', 'sp', 'list', '--filter', "appId eq '$appId'", '--query', '[0]', '-o', 'json')
$apiSp = $apiSpJson | ConvertFrom-Json
if (-not $apiSp) { throw "No service principal for app $appId" }

# Current assignments, so the script is re-runnable: it adds missing roles and removes extra ones.
$current = (Invoke-Az @('rest', '--method', 'GET', '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$mcpPrincipalId/appRoleAssignments") | ConvertFrom-Json).value |
    Where-Object { $_.resourceId -eq $apiSp.id }

foreach ($roleName in $Roles) {
    $role = $apiSp.appRoles | Where-Object { $_.value -eq $roleName }
    if (-not $role) { throw "Role '$roleName' not defined on the app registration." }
    if ($current | Where-Object { $_.appRoleId -eq $role.id }) {
        Write-Host "already granted: $roleName"
        continue
    }
    # Body goes through a file: Windows PowerShell strips the double quotes when it passes JSON to a native command.
    $bodyFile = Join-Path $env:TEMP "stay-role-$($role.value).json"
    (@{ principalId = $mcpPrincipalId; resourceId = $apiSp.id; appRoleId = $role.id } | ConvertTo-Json -Compress) | Set-Content -Path $bodyFile -Encoding ASCII
    Invoke-Az @('rest', '--method', 'POST', '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$mcpPrincipalId/appRoleAssignments", '--headers', 'Content-Type=application/json', '--body', "@$bodyFile") | Out-Null
    Remove-Item $bodyFile -ErrorAction SilentlyContinue
    Write-Host "granted: $roleName"
}

foreach ($assignment in $current) {
    $role = $apiSp.appRoles | Where-Object { $_.id -eq $assignment.appRoleId }
    if ($role -and ($Roles -notcontains $role.value)) {
        Invoke-Az @('rest', '--method', 'DELETE', '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$mcpPrincipalId/appRoleAssignments/$($assignment.id)") | Out-Null
        Write-Host "removed: $($role.value)"
    }
}

Write-Host ''
Write-Host 'Done. Managed identity tokens are cached up to 24h by the platform; restart the staymcp app to pick up role changes quickly:'
Write-Host "  az functionapp restart -g (azd env get-value AZURE_RESOURCE_GROUP) -n (azd env get-value STAY_MCP_APP_NAME)"
