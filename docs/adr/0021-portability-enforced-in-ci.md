# ADR-0021: Portability Enforced in CI

**Status:** Accepted
**Date:** 2026-08

## Context

ADR-0019 claims domain and module code carries no cloud dependency. A claim in
prose decays. seamline's convention is that boundaries are enforced by
architecture tests, not trusted to hold — six such rules already run in
`Seamline.ArchTests`. Portability earns the same treatment.

## Decision

A seventh architecture test: **implementation and `.Contracts` assemblies of
every module must not depend on `AWSSDK.*` or `Azure.*`.**

Scope is deliberate and mirrors the existing boundary tests (which target impl
vs composition root):

- **In scope (must stay clean):** `Seamline.Modules.*`, `Seamline.Modules.*.Contracts`.
- **Out of scope (may reference cloud/transport SDKs):** composition roots and
  hosts — `Seamline.Api`, `Seamline.Valuation.Worker`, `Seamline.Reporting.Worker`,
  `Seamline.Valuation.Function`, and the messaging/infrastructure project (which
  legitimately references `Azure.Messaging.ServiceBus` transitively via MassTransit).

The test passes on introduction — there is no AWS SDK in code today (all AWS
concerns live in Terraform). Its job is not to fix a violation but to **lock the
achieved state** before the Azure phases (D–F) can introduce a leak.

## Consequences

### Positive
- The portability claim is now mechanical: a leaked `using Azure.…` in a module
  fails the build, the same way a forbidden cross-module dependency does.
- The strongest résumé/interview artifact of the whole lane: portability you can
  *demonstrate*, not assert.

### Negative
- The allow-list of host assemblies must be maintained when a new host is added.
  Cheap and self-evident when it happens.

### Neutral
- Ordering matters: this ADR lands before Service Bus wiring and Bicep, so those
  phases build on a guarded foundation.
