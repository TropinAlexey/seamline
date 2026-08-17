# ADR-0023: Azure Compute (Container Apps) and IaC (Bicep)

**Status:** Accepted
**Date:** 2026-08

## Context

The Azure lane needs a compute target and an IaC tool. The AWS side uses ECS
Fargate provisioned by Terraform. The choices should preserve the project's
narrative — managed compute, no orchestration weight the domain doesn't need —
and keep the Azure story native.

## Decision

**1. Compute: Azure Container Apps, not AKS.** Container Apps runs the existing
Docker images unchanged, provides ingress and scale-to-N out of the box, and
keeps the "managed compute on both clouds" line intact (Fargate ↔ Container
Apps). AKS would buy a Kubernetes keyword at the cost of contradicting the
project's own thesis (ADR-0001) — added complexity without a driving need.

**2. IaC: Bicep, alongside Terraform.** `infra/aws/` (Terraform, existing) and
`infra/azure/` (Bicep, new). Bicep is Azure-native, closes the keyword, and has
first-class `what-if`. The existing `infra/` → `infra/aws/` move is done in a
separate early PR (before any Azure work) to keep the Bicep phase purely
additive.

Resource parity:

| AWS (Terraform)      | Azure (Bicep)                          |
|----------------------|----------------------------------------|
| ECS Fargate          | Container Apps + environment           |
| ALB                  | Container Apps ingress                 |
| RDS PostgreSQL       | Azure PostgreSQL Flexible Server       |
| ECR                  | Azure Container Registry (ACR)         |
| Secrets Manager      | Key Vault + Managed Identity           |
| (RabbitMQ)           | Service Bus namespace + topics/subs    |
| OTLP collector       | Log Analytics + Azure Monitor          |
| GitHub OIDC provider | Entra app + federated credential       |

**3. No provisioning as Definition of Done.** Validation is `az bicep build`
(offline) and `az deployment group what-if` (dry-run). This mirrors the AWS
side, where infrastructure is written but the deploy step is disabled until
provisioned.

## Consequences

### Positive
- Same images, both clouds; consistent "managed compute" narrative.
- Bicep gives native `what-if` — cleaner dry-run validation than Terraform plan
  against an unprovisioned account.
- KEDA queue-length scaling is available in Container Apps as a future, honest
  bonus to the serverless story — deferred as optional "Level 1.5", out of scope.

### Negative
- Two IaC languages to maintain. Accepted: the parity table is the shared spec,
  and the divergence is the point being demonstrated.

### Neutral
- `what-if` requires an authenticated subscription (a free account suffices; no
  resources are created). Pure-offline floor is `bicep build` + `validate`.
