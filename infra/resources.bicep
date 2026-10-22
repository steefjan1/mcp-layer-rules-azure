param location string
param tags object
param token string
param apiAppClientId string
param apiTenantId string
@secure()
param quoteSigningKey string

var storageName = 'st${token}'
var apiAppName = 'func-stayapi-${token}'
var mcpAppName = 'func-staymcp-${token}'

// ---------- shared ----------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
  resource blobs 'blobServices' = {
    name: 'default'
    resource apiContainer 'containers' = { name: 'stayapi-deploy' }
    resource mcpContainer 'containers' = { name: 'staymcp-deploy' }
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${token}'
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${token}'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}

// Flex Consumption allows exactly one site per plan, so each app gets its own.
resource planApi 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-stayapi-${token}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { tier: 'FlexConsumption', name: 'FC1' }
  properties: { reserved: true }
}

resource planMcp 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-staymcp-${token}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { tier: 'FlexConsumption', name: 'FC1' }
  properties: { reserved: true }
}

// One identity per app. The MCP identity is the "own token" of rule 1.
resource idApi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-stayapi-${token}'
  location: location
  tags: tags
}

resource idMcp 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-staymcp-${token}'
  location: location
  tags: tags
}

// Role assignments on the identities' principalIds live in their own module.
module storageRoles 'roles.bicep' = {
  name: 'storage-roles'
  params: {
    storageName: storage.name
    principalIds: [idApi.properties.principalId, idMcp.properties.principalId]
  }
}

// ---------- the API (kitchen) ----------

resource apiApp 'Microsoft.Web/sites@2024-04-01' = {
  name: apiAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'stayapi' })
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${idApi.id}': {} }
  }
  properties: {
    serverFarmId: planApi.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}stayapi-deploy'
          authentication: { type: 'UserAssignedIdentity', userAssignedIdentityResourceId: idApi.id }
        }
      }
      scaleAndConcurrency: { maximumInstanceCount: 40, instanceMemoryMB: 2048 }
      runtime: { name: 'dotnet-isolated', version: '8.0' }
    }
    siteConfig: {
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: idApi.properties.clientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'AZURE_CLIENT_ID', value: idApi.properties.clientId }
        { name: 'STORE_TABLE_URI', value: storage.properties.primaryEndpoints.table }
        { name: 'AUTH_MODE', value: empty(apiAppClientId) ? 'local' : 'entra' }
        { name: 'QUOTE_KEY_ID', value: 'stay-${token}-2026-10' }
        { name: 'QUOTE_TTL_MINUTES', value: '15' }
        { name: 'QUOTE_SIGNING_KEY', value: quoteSigningKey }
      ]
    }
  }
  dependsOn: [storageRoles]
}

// Built-in authentication. The platform validates the bearer token (issuer, audience,
// signature, expiry) and passes claims to the app in X-MS-CLIENT-PRINCIPAL. The app
// only reads roles; it never touches the token.
resource apiAuth 'Microsoft.Web/sites/config@2024-04-01' = if (!empty(apiAppClientId)) {
  parent: apiApp
  name: 'authsettingsV2'
  properties: {
    globalValidation: {
      requireAuthentication: true
      unauthenticatedClientAction: 'Return401'
      excludedPaths: ['/api/.well-known/stay-keys']
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          clientId: apiAppClientId
          openIdIssuer: 'https://login.microsoftonline.com/${apiTenantId}/v2.0'
        }
        validation: {
          allowedAudiences: ['api://${apiAppClientId}', apiAppClientId]
        }
      }
    }
    login: { tokenStore: { enabled: false } }
  }
}

// ---------- the MCP server (the door the model reads) ----------

resource mcpApp 'Microsoft.Web/sites@2024-04-01' = {
  name: mcpAppName
  location: location
  tags: union(tags, { 'azd-service-name': 'staymcp' })
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${idMcp.id}': {} }
  }
  properties: {
    serverFarmId: planMcp.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}staymcp-deploy'
          authentication: { type: 'UserAssignedIdentity', userAssignedIdentityResourceId: idMcp.id }
        }
      }
      scaleAndConcurrency: { maximumInstanceCount: 40, instanceMemoryMB: 2048 }
      runtime: { name: 'dotnet-isolated', version: '8.0' }
    }
    siteConfig: {
      appSettings: [
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: idMcp.properties.clientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'AZURE_CLIENT_ID', value: idMcp.properties.clientId }
        { name: 'STAY_API_BASE_URL', value: 'https://${apiApp.properties.defaultHostName}/api' }
        { name: 'AUTH_MODE', value: empty(apiAppClientId) ? 'local' : 'entra' }
        { name: 'LOCAL_ROLES', value: 'Stays.Read,Bookings.Create' }
        { name: 'STAY_API_APP_ID_URI', value: empty(apiAppClientId) ? '' : 'api://${apiAppClientId}' }
      ]
    }
  }
  dependsOn: [storageRoles]
}

output apiUrl string = 'https://${apiApp.properties.defaultHostName}'
output mcpUrl string = 'https://${mcpApp.properties.defaultHostName}/runtime/webhooks/mcp'
output mcpPrincipalId string = idMcp.properties.principalId
output apiAppName string = apiApp.name
output mcpAppName string = mcpApp.name
