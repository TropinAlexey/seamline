# ADR-0008: Pin MassTransit to 8.5.10 (Last Apache-2.0 Release)

**Status:** Accepted
**Date:** 2026-07

## Context

Starting with MassTransit 9.0, the project requires a commercial license
for non-evaluation use — `dotnet run` fails at startup with
`MassTransit.ConfigurationException: License must be specified with
SetLicense/SetLicenseLocation...` unless a license is configured. This is
the same shape of change MediatR went through under the same author
(Chris Patterson), which is exactly why "no MediatR" was already a decision
for this project — see `CLAUDE.md`, "No MediatR". Adding MassTransit 9.x
as MediatR's replacement while ignoring that it hit the identical trap would
have been a contradiction discovered by an interviewer, not by us.

## Decision

Pin `MassTransit` and `MassTransit.EntityFrameworkCore` to **8.5.10**, the
last version published under the Apache 2.0 license before the licensing
change. Do not float to `9.x` or later without a deliberate, separate
decision (and a license, if that ever makes sense for this project).

## Consequences

### Positive

- No license cost, no license configuration, no risk of a showcase project
  failing to start for a reviewer who clones it and runs `docker compose up`.
- 8.5.10 has the full feature set this project needs: `IPublishEndpoint`,
  `IConsumer<T>`, the in-memory transport, `AddEntityFrameworkOutbox`, and
  the RabbitMQ transport for Phase 2. Nothing planned requires 9.x.

### Negative

- No security or bug fixes land on 8.x going forward; MassTransit's own
  maintenance attention is on 9.x. Accepted: re-evaluate if a fix the
  project actually needs never gets backported.
- Any future contributor or reviewer running `dotnet add package
  MassTransit` without checking this ADR will silently pull 9.x and break
  the build with a license exception at startup — not a compile error, a
  runtime one. Mitigated by this ADR and by the explicit version in every
  `.csproj`; NuGet will not auto-upgrade a pinned version.

## Alternatives considered

**Pay for a MassTransit license.** Rejected. This is an unpaid portfolio
project; a licensing cost is disproportionate to what it demonstrates, and
the free 8.x line already covers every feature used here.

**Hand-roll the bus (in-memory dispatcher now, raw RabbitMQ.Client later).**
Rejected. It would reintroduce the exact problem MassTransit exists to
solve — retry policies, delayed redelivery, the transactional outbox,
consumer idempotency — and turn a one-line dependency choice into weeks of
infrastructure code that adds no interview signal beyond "can implement a
worse MassTransit."

## Revisit criteria

Move off 8.x if either becomes true:

- A specific bug or CVE in 8.5.10 has no backport and blocks the project.
- The project's scope changes such that paying for a MassTransit license
  becomes justified (e.g., it stops being an unpaid portfolio project).
