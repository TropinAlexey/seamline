# ADR-0022: A Serverless Trigger for End-of-Day Valuation

**Status:** Accepted
**Date:** 2026-08

## Context

The Azure lane targets roles that value serverless/cloud-native experience. A
narrow, honest serverless surface is worth adding — but only where the model
fits, and without duplicating domain logic or making the messaging story
incoherent.

End-of-day mark-to-market is a scheduled batch. On AWS it runs as a Hangfire
recurring job inside `Valuation.Worker`. Crucially, the valuation logic is
already reached through one public extension method on `Risk`, and
`Valuation.Worker` is already a thin composition root over it — so the domain is
already host-agnostic.

## Decision

**1. Add `Seamline.Valuation.Function`** — Azure Functions, isolated worker
model, .NET 10, a Timer trigger — as a *third* composition root calling the
*same* `Risk` extension method that `Valuation.Worker` calls. The trigger is
swappable (Hangfire recurring ↔ Function timer); the valuation logic is not
touched and not duplicated.

**2. Serverless is used only for scheduling.** Event-driven consumers stay on
MassTransit (over Service Bus on Azure, ADR-0020). We do **not** rewrite them as
Service Bus-triggered Functions: two hosting models for one messaging concern
would make the architecture incoherent for no gain. Functions appear exactly
where the serverless story is clean — a scheduled trigger.

**3. Event Grid and API Management are out of scope.** They add infrastructure
weight without proportional signal for the target roles — the same scope
discipline applied to CTRM features (options, VaR).

## Consequences

### Positive
- A genuine, honestly-scoped serverless artifact, obtained by adding a trigger,
  not by restructuring the domain.
- Reinforces ADR-0003: the trigger mechanism is a host concern; the work is not.

### Negative
- A second scheduling host to keep in sync conceptually. Bounded: both call one
  method, and only one runs per deployment target.

### Neutral
- pet-project serverless depth, not commercial. Stated plainly; it demonstrates
  the model and the hosting seam, nothing more.

## Validation
`func start` locally; one EOD valuation pass produces the same MtM as the
Hangfire path against the same local Postgres.
