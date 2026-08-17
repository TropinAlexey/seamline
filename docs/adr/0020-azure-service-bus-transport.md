# ADR-0020: Azure Service Bus as a MassTransit Transport

**Status:** Accepted
**Date:** 2026-08

## Context

The Azure lane (ADR-0019) needs a message transport. On AWS the MassTransit
transport is RabbitMQ, selected from configuration (ADR-0017). MassTransit has a
first-class Azure Service Bus transport, so the swap is a configuration branch,
not a rewrite.

Two decisions need recording: how the transport is selected, and who creates the
Service Bus topology (topics/subscriptions).

## Decision

**1. Config-selected transport.** Configuration key `Transport` accepts
`InMemory` | `RabbitMq` | `AzureServiceBus`. The `AzureServiceBus` branch lives
in the same composition/infrastructure project as the existing RabbitMQ branch.
Consumers, publishers, and the credit-limit saga are untouched — they bind to
MassTransit abstractions, not to a transport.

**2. Message scheduler is transport-specific.** `UseDelayedMessageScheduler()`
(RabbitMQ's delayed-message-exchange plugin) and
`UseServiceBusMessageScheduler()` (Service Bus native delayed delivery) are
configured inside their respective `UsingRabbitMq` / `UsingAzureServiceBus`
branches, not in the shared pipeline. The shared `ConfigurePipeline` method
retains only transport-agnostic concerns: `UseMessageRetry` and
`ConfigureEndpoints`.

**3. Connection via environment, not code.** The Service Bus connection string
(or, preferably, a fully-qualified namespace used with Managed Identity) arrives
as an environment variable, the same pattern as the RabbitMQ password today
(ADR-0019). No `Azure.*` reference enters domain or module assemblies.

**4. Topology pre-provisioned in IaC, deploy-time creation disabled.** By
default MassTransit creates topics and subscriptions at startup, which requires
the `Manage` claim on the namespace. Under least privilege the runtime identity
should hold only send/listen. Therefore the namespace, topics, and subscriptions
are declared in Bicep (ADR-0023) and MassTransit's deploy-time topology is turned
off (`DeployTopologyOnly = false`, no `Manage` grant at runtime).

## Consequences

### Positive
- One more transport, zero domain change — the payoff of ADR-0017's abstraction.
- Runtime identity is least-privilege (send/listen only).

### Negative
- Topology now lives in two mental models: MassTransit's naming conventions and
  the Bicep declarations must agree. Documented in ADR-0023's resource table.

### Neutral
- Tests are unaffected: the integration suite runs on the in-memory transport
  (ADR-0017), so it depends on neither RabbitMQ nor Service Bus.

## Validation

Local smoke against the Azure Service Bus **emulator** (container). The emulator
is young (2024) and feature-incomplete, so it is used for manual smoke only —
never as a CI dependency.
