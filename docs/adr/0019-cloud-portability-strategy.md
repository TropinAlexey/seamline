# ADR-0019: Cloud Portability Strategy

**Status:** Accepted
**Date:** 2026-08

## Context

seamline runs on AWS (ECS Fargate, RDS, Secrets Manager, ECR) with RabbitMQ as
the MassTransit transport. Adding an Azure deployment lane raises the real
question: *what should differ between the two clouds, and where should that
difference live?*

The tempting answer — a cloud-provider abstraction in application code, swapping
implementations per cloud — is wrong here. It would pull cloud identity into the
domain, the exact coupling the module boundaries (ADR-0001, ADR-0021) exist to
prevent.

## Decision

**Cloud differences live in infrastructure (IaC) and composition/config, never
in domain or module code.**

Three mechanisms, each at its correct layer:

1. **Secrets — abstracted at the platform, not in code.** The app reads plain
   `ConnectionStrings:*` from environment variables. AWS injects them via ECS
   `valueFrom` → Secrets Manager; Azure via Container Apps `secretRef` → Key
   Vault + Managed Identity. No C# ever references a secrets SDK. Introducing a
   Key Vault configuration provider would be a *regression* — it would add an
   `Azure.*` dependency to the composition root for zero benefit.

2. **Transport — abstracted in config/DI.** MassTransit already selects its
   transport from configuration (ADR-0017). Azure Service Bus is one more branch
   (ADR-0020). Consumers, publishers, and the saga are unchanged.

3. **Compute, database, telemetry — identical.** The same Docker images run on
   Fargate and Container Apps. Npgsql talks to RDS and Azure PostgreSQL Flexible
   Server unchanged. OTLP export is endpoint-only.

The net code delta between clouds is **one thing**: the transport branch. Every
other difference is IaC or platform-injected config.

## Consequences

### Positive
- The portability claim becomes literal and testable (ADR-0021): domain and
  module assemblies carry no cloud SDK dependency at all.
- Adding a third cloud later is an IaC exercise, not a code change.
- The strongest signal is not "I used Azure service X" but "the system is
  cloud-portable *by construction*, enforced in CI".

### Negative
- Discipline required: every cloud fix must be resisted from leaking into code.
  The architecture test in ADR-0021 is what makes the discipline mechanical
  rather than aspirational.

### Neutral
- Not every abstraction lives in code. Secrets are abstracted at the platform,
  transport in DI. Different seams, different mechanisms — both correct.

## Alternatives considered

**A code-level `ICloudProvider` master switch.** Rejected. It would abstract a
single differing seam (transport) behind a grand-sounding interface, while
tempting secrets/telemetry code into the app. A leaky abstraction pretending to
be a large one.
