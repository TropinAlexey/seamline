param location string
param projectName string
param acrId string

// Workload identity for Container Apps — pulls images from ACR,
// reads secrets from Key Vault (role assigned in keyvault module).
resource workloadIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' = {
  name: '${projectName}-workload'
  location: location
}

// AcrPull so Container Apps can pull images without admin credentials.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acrId, workloadIdentity.id, 'acrpull')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// GitHub Actions OIDC — federated credential (ADR-0024).
param githubRepo string = 'TropinAlexey/seamline'

resource githubActionsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' = {
  name: '${projectName}-github-actions'
  location: location
}

resource federatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-07-31-preview' = {
  parent: githubActionsIdentity
  name: 'github-main'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepo}:ref:refs/heads/main'
    audiences: ['api://AzureADTokenExchange']
  }
}

output workloadIdentityId string = workloadIdentity.id
output workloadPrincipalId string = workloadIdentity.properties.principalId
output workloadClientId string = workloadIdentity.properties.clientId
output githubActionsClientId string = githubActionsIdentity.properties.clientId
output githubActionsPrincipalId string = githubActionsIdentity.properties.principalId
