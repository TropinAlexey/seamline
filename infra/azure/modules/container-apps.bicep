param location string
param projectName string
param logAnalyticsWorkspaceId string
param workloadIdentityId string
param workloadClientId string
param acrLoginServer string
param apiImageTag string = 'latest'
param valuationWorkerImageTag string = 'latest'
param reportingWorkerImageTag string = 'latest'

// Shared Container Apps environment — one per deployment,
// analogous to the single ECS cluster on the AWS side.
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${projectName}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2023-09-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2023-09-01').primarySharedKey
      }
    }
  }
}

// --- API (external ingress, port 8080) ---
resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${projectName}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${workloadIdentityId}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: acrLoginServer
          identity: workloadIdentityId
        }
      ]
      secrets: []
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acrLoginServer}/seamline-api:${apiImageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'AZURE_CLIENT_ID', value: workloadClientId }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

// --- Valuation Worker (no ingress, internal only) ---
resource valuationWorker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${projectName}-valuation'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${workloadIdentityId}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: acrLoginServer
          identity: workloadIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'valuation'
          image: '${acrLoginServer}/seamline-valuation-worker:${valuationWorkerImageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'AZURE_CLIENT_ID', value: workloadClientId }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// --- Reporting Worker (no ingress, internal only) ---
resource reportingWorker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${projectName}-reporting'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${workloadIdentityId}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: acrLoginServer
          identity: workloadIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'reporting'
          image: '${acrLoginServer}/seamline-reporting-worker:${reportingWorkerImageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'AZURE_CLIENT_ID', value: workloadClientId }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// --- Acer Stub (internal ingress, port 8080) ---
resource acerStub 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${projectName}-acer-stub'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${workloadIdentityId}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: acrLoginServer
          identity: workloadIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'acer-stub'
          image: '${acrLoginServer}/seamline-acer-stub:latest'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

output apiFqdn string = api.properties.configuration.ingress.fqdn
output environmentId string = environment.id
