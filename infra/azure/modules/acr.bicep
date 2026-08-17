param location string
param projectName string

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: replace('${projectName}acr', '-', '')
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

output loginServer string = acr.properties.loginServer
output acrId string = acr.id
output acrName string = acr.name
