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

// Topology pre-provisioned (ADR-0020 §4): runtime identity holds send/listen
// only, no Manage claim. MassTransit's DeployTopologyOnly = false in Program.cs.
// Topic names follow MassTransit's default kebab-case convention.

var eventTypes = [
  'trade-activated'
  'trade-amended'
  'trade-delivered'
  'trade-rejected'
  'trade-approval-requested'
  'trade-approval-granted'
  'trade-approval-denied'
  'trade-approval-completed'
  'trade-approval-cancelled'
  'trade-cancel-requested'
]

resource topics 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = [
  for name in eventTypes: {
    parent: namespace
    name: name
    properties: {
      maxSizeInMegabytes: 1024
    }
  }
]

// Each consumer gets its own subscription. MassTransit names subscriptions
// after the consumer type; we match that convention here.

resource riskTradeActivated 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topics[0] // trade-activated
  name: 'trade-activated-consumer'
}

resource auditTradeActivated 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topics[0] // trade-activated
  name: 'trade-audit-consumer-activated'
}

resource riskTradeAmended 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topics[1] // trade-amended
  name: 'trade-amended-consumer'
}

resource settlementTradeDelivered 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topics[2] // trade-delivered
  name: 'settlement-trade-delivered-consumer'
}

resource riskTradeDelivered 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topics[2] // trade-delivered
  name: 'risk-trade-delivered-consumer'
}

resource auditTradeRejected 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topics[3] // trade-rejected
  name: 'trade-audit-consumer-rejected'
}

resource approvalCompleted 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topics[7] // trade-approval-completed
  name: 'trade-approval-completed-consumer'
}

resource approvalCancelled 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topics[8] // trade-approval-cancelled
  name: 'trade-approval-cancelled-consumer'
}

output namespaceName string = namespace.name
output namespaceId string = namespace.id
