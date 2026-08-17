param location string
param projectName string

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: '${projectName}-sb'
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}

output namespaceName string = namespace.name
output namespaceId string = namespace.id
// MassTransit auto-creates topics/subscriptions at startup (ADR-0020),
// so we only provision the namespace here.
