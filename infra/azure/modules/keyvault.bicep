param location string
param projectName string
param workloadPrincipalId string
param postgresFqdn string

@secure()
param dbOwnerPassword string
@secure()
param dbAppPassword string

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${projectName}-kv'
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
  }
}

// Key Vault Secrets User role for the workload identity.
resource kvSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, workloadPrincipalId, 'kvsecretsuser')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: workloadPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource appConnString 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'app-connection-string'
  properties: {
    value: 'Host=${postgresFqdn};Database=seamline;Username=seamline_app;Password=${dbAppPassword};Ssl Mode=Require'
  }
}

resource migratorConnString 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'migrator-connection-string'
  properties: {
    value: 'Host=${postgresFqdn};Database=seamline;Username=seamline;Password=${dbOwnerPassword};Ssl Mode=Require'
  }
}

output vaultUri string = vault.properties.vaultUri
output vaultName string = vault.name
