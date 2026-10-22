param storageName string
param principalIds array

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}

// Blob Data Owner (b7e6dc6d-f1e8-4753-8033-0f276bb0955b), Queue Data Contributor
// (974c5e8b-45b9-4653-ba55-5f855dd0fb88), Table Data Contributor (0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3).
var roles = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
]

resource assignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for pair in flatten(map(principalIds, p => map(roles, r => { principalId: p, role: r }))): {
  name: guid(storage.id, pair.principalId, pair.role)
  scope: storage
  properties: {
    principalId: pair.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', pair.role)
  }
}]
