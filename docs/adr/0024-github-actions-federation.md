# ADR-0024: GitHub Actions with Workload Identity Federation, not Azure DevOps

**Status:** Accepted
**Date:** 2026-08

## Context

The Azure lane needs CI/CD. seamline already deploys via GitHub Actions to AWS
using OIDC — no long-lived keys, a GitHub OIDC provider trusted by an AWS role.
Azure supports the same pattern (workload identity federation): an Entra app with
a federated credential trusts GitHub's OIDC token, so `azure/login` needs no
secret.

Some Azure job postings (Danfoss among them) ask specifically for Azure DevOps.
That pulls the other way.

## Decision

**Stay on GitHub Actions; authenticate to Azure via workload identity
federation.** One pipeline, OIDC into both clouds, no stored cloud credentials.
A new Azure job builds images, pushes to ACR (beside the existing ECR push), and
runs `bicep build` + `what-if` as a CI gate. The federated credential (subject
`repo:TropinAlexey/seamline:ref:refs/heads/main`) is provisioned in Bicep
(ADR-0023). The deploy step is defined and disabled, mirroring AWS.

**We consciously do not close the "Azure DevOps" keyword.** Maintaining a second
CI system in a demo repo is not worth it; Azure DevOps YAML is a couple of days
to pick up before a specific offer. The coherent "one pipeline, federated OIDC
into both clouds" story is the better artifact.

## Consequences

### Positive
- No stored secrets for either cloud; identical security posture (OIDC) across
  AWS and Azure — a strong, single narrative.
- One pipeline to reason about.

### Negative
- The literal "Azure DevOps" screening keyword is unmet. Explicit, deliberate
  trade — flagged, not silent. Mitigation: learn Azure DevOps YAML on demand.

### Neutral
- If a serious opportunity hinges on Azure DevOps, porting the pipeline is a
  known, bounded task; the build/push/what-if steps carry over conceptually.
