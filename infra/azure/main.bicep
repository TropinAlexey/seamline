// Seamline mini-CTRM — Azure deployment (ADR-0023)
// Resource parity with infra/aws/ (Terraform):
//   ECS Fargate       → Container Apps
//   ALB               → Container Apps ingress
//   RDS PostgreSQL    → PostgreSQL Flexible Server
//   ECR               → ACR
//   Secrets Manager   → Key Vault + Managed Identity
//   RabbitMQ          → Service Bus
//   OTLP collector    → Log Analytics
//   GitHub OIDC       → Entra federated credential

targetScope = 'resourceGroup'

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Project name prefix for all resources')
param projectName string = 'seamline'

@secure()
@description('PostgreSQL owner (seamline) password')
param dbOwnerPassword string

@secure()
@description('PostgreSQL app (seamline_app) password')
param dbAppPassword string

@description('GitHub repository for OIDC federation (ADR-0024)')
param githubRepo string = 'TropinAlexey/seamline'

@description('API container image tag')
param apiImageTag string = 'latest'

@description('Valuation worker container image tag')
param valuationWorkerImageTag string = 'latest'

@description('Reporting worker container image tag')
param reportingWorkerImageTag string = 'latest'

// --- Monitoring ---
module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: { location: location, projectName: projectName }
}

// --- Container Registry ---
module acr 'modules/acr.bicep' = {
  name: 'acr'
  params: { location: location, projectName: projectName }
}

// --- PostgreSQL ---
module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    location: location
    projectName: projectName
    adminPassword: dbOwnerPassword
  }
}

// --- Service Bus ---
module servicebus 'modules/servicebus.bicep' = {
  name: 'servicebus'
  params: { location: location, projectName: projectName }
}

// --- Identity (workload + GitHub Actions OIDC) ---
module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    location: location
    projectName: projectName
    acrId: acr.outputs.acrId
    githubRepo: githubRepo
  }
}

// --- Key Vault ---
module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    projectName: projectName
    workloadPrincipalId: identity.outputs.workloadPrincipalId
    postgresFqdn: postgres.outputs.fqdn
    dbOwnerPassword: dbOwnerPassword
    dbAppPassword: dbAppPassword
  }
}

// --- Container Apps ---
module apps 'modules/container-apps.bicep' = {
  name: 'container-apps'
  params: {
    location: location
    projectName: projectName
    logAnalyticsWorkspaceId: monitoring.outputs.workspaceId
    workloadIdentityId: identity.outputs.workloadIdentityId
    workloadClientId: identity.outputs.workloadClientId
    acrLoginServer: acr.outputs.loginServer
    apiImageTag: apiImageTag
    valuationWorkerImageTag: valuationWorkerImageTag
    reportingWorkerImageTag: reportingWorkerImageTag
  }
}

// --- Outputs ---
output apiFqdn string = apps.outputs.apiFqdn
output acrLoginServer string = acr.outputs.loginServer
output postgresFqdn string = postgres.outputs.fqdn
output keyVaultUri string = keyvault.outputs.vaultUri
output serviceBusNamespace string = servicebus.outputs.namespaceName
output githubActionsClientId string = identity.outputs.githubActionsClientId
