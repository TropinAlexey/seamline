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

// GitHub Actions needs AcrPush to push images and Contributor to deploy.
resource ghAcrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acrId, githubActionsIdentity.id, 'acrpush')
  scope: resourceGroup()
  properties: {
    // AcrPush role
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')
    principalId: githubActionsIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource ghContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, githubActionsIdentity.id, 'contributor')
  scope: resourceGroup()
  properties: {
    // Contributor role
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
    principalId: githubActionsIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output workloadIdentityId string = workloadIdentity.id
output workloadPrincipalId string = workloadIdentity.properties.principalId
output workloadClientId string = workloadIdentity.properties.clientId
output githubActionsClientId string = githubActionsIdentity.properties.clientId
output githubActionsPrincipalId string = githubActionsIdentity.properties.principalId
