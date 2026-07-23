# ADR-0001: Modular Monolith Instead of Microservices

**Status:** Accepted
**Date:** 2026-07

## Context

Seamline is a multi-tenant commodity trading & risk platform covering trade
capture, market data, risk/position, settlement, reference data, and
identity. These are six clearly separable bounded contexts, and "trading
platform" is exactly the kind of domain where a microservices write-up is
expected. Choosing not to split into services therefore needs an explicit
justification, not silence.

The system is built and operated by a single engineer. There is no
organizational scaling problem to solve — no multiple teams stepping on each
other's release trains, no need for independent deploy cadences across teams.
The technical benefits usually cited for microservices (independent scaling,
failure isolation, polyglot runtimes) are either not needed at this scale or
are achievable more cheaply inside a single process.

## Decision

Default to a **modular monolith**: one deployable ASP.NET Core process
(`Seamline.Api`), one PostgreSQL database with one schema per module, and
six modules (`Reference`, `Trading`, `MarketData`, `Risk`, `Settlement`,
`Identity`) that communicate only through explicit contracts.

Concretely:

- Each module is two projects: `Seamline.Modules.<Name>` (implementation,
  internal types) and `Seamline.Modules.<Name>.Contracts` (public DTOs,
  query interfaces, integration events).
- A module's implementation may depend only on its own Contracts, on
  `Seamline.SharedKernel`, and on the Contracts of modules it legitimately
  needs. It may never depend on another module's implementation.
- Cross-module communication is either a synchronous read-only query
  interface (for slow-changing reference data) or an asynchronous
  integration event over MassTransit (for state changes).
- `Seamline.ArchTests` enforces both the "no impl-to-impl dependency" rule
  and the "Contracts never depends on any impl" rule via NetArchTest, in CI,
  on every build. The boundary is a compiler/CI fact, not a code-review
  convention — see the namespace mechanics documented in `CLAUDE.md`.
- No PostgreSQL foreign keys cross module schemas. Referential integrity
  between modules is eventual, via events, not enforced at the database
  level, so the schema itself never leaks the module boundary.

Two components are extracted into separate processes on purpose —
`Reporting.Worker` (REMIT/ACER submission) and `Valuation.Worker`
(mark-to-market) — each because it has a genuinely different load, failure,
or SLA profile from the API. Both share the same PostgreSQL database; this is
service-based architecture, not a database-per-service split, and that is a
deliberate, stated choice, not an oversight.

## Consequences

### Positive

- No network calls, no distributed transactions, no eventual-consistency
  reasoning for anything that stays inside the monolith — most of the system.
- A wrongly drawn boundary is a same-day refactor: move a class, fix the
  `ProjectReference`, done. Between real services it would mean a data
  migration and API versioning.
- Single deployable, single connection pool, trivial local development
  (`docker compose up`, no service mesh, no per-service configuration).
- The boundary discipline (arch tests, Contracts-only communication, no
  cross-schema FKs) is exactly the discipline that would be needed to split
  a module into a real service later — if that ever becomes necessary, the
  module is already decoupled enough that the split is a deployment change,
  not a redesign.

### Negative

- A single process means a single failure domain for everything that stays
  in the monolith: an unhandled exception or resource exhaustion in one
  module can affect the others. Accepted at this scale and mitigated by
  extracting the two components whose failure/load profile genuinely differs.
- Independent scaling is not available for anything inside the monolith —
  the whole API scales as one unit. Accepted: nothing inside the monolith
  currently has a load profile that would benefit from scaling
  independently; the two extracted workers are exactly the exception.

## Alternatives considered

**Microservices per module (six services).** Rejected. None of the
triggers that justify microservices apply: no multi-team coordination
problem, no radically different load/SLA profile for most modules, no
regulatory isolation requirement for reference data, trading, or
settlement. The cost — distributed transactions, saga complexity, N
deployment pipelines, a service mesh or equivalent — would be paid without
the benefit that justifies it.

**Single project, no module boundaries.** Rejected. Without separate
assemblies and arch tests, module boundaries erode within weeks — every
class becomes reachable from every other class, and "modular" becomes an
aspiration in a README rather than a fact the compiler and CI enforce.

## Revisit criteria

Reconsider extracting a module into a real service if any of the following
becomes true for it specifically:

- It needs to scale independently at a magnitude the rest of the system
  does not (e.g., an order-of-magnitude higher request volume).
- It needs a release cadence decoupled from the rest of the system.
- A regulatory or contractual requirement demands an isolated deployment
  boundary for that module specifically.

Until then, the cost of a full service split is not justified.
