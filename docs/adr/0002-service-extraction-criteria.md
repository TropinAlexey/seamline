# ADR-0002: Extracting a Process Without Extracting a Database

**Status:** Accepted
**Date:** 2026-07

## Context

ADR-0001 already states, in passing, that two components are extracted into
separate processes on purpose — `Reporting.Worker` and `Valuation.Worker` —
each because it has a genuinely different load, failure, or SLA profile from
the API, and that both share the same PostgreSQL database. That statement
was made before either worker existed. Now that `Valuation.Worker` is being
built (mark-to-market, ADR-0007's formula), the criteria behind "why these
two, and why a process split without a database split" need to be written
down as their own decision rather than live only as a forward reference
inside ADR-0001.

## Decision

**A component earns a separate process when its load or timing profile is
incompatible with living inside the request/response API process** — not
when it belongs to a different bounded context (that's what a module is
for) and not by default.

`Valuation.Worker`'s profile: an end-of-day batch job that revalues every
open position across every tenant in one run. That's a sustained,
predictable burst of CPU/DB work with no caller waiting synchronously — the
opposite shape from `Seamline.Api`'s per-request latency profile. Running it
in-process would mean either the whole API pauses momentarily under EOD
load, or a background job competes for the same thread pool and connection
pool budget the API needs for live requests, with no isolation between them.
`Reporting.Worker` (REMIT/ACER submission, not yet built) will earn the same
argument for the same reason: batch submission runs against an external
regulator endpoint the API's request path has no business waiting on.

**The database is not split.** Both workers connect to the same PostgreSQL
instance as `Seamline.Api`, through the same restricted `seamline_app` role
for runtime queries and the same owner role for their own migrations (each
worker owns its migrations the same way each module does — `Valuation.Worker`
runs `Seamline.Modules.Risk`'s migrations, since `risk.valuation_snapshot`
belongs to `Risk`'s schema, not a new one). Splitting the database would
turn every worker read of `Position`/`PriceCurvePoint` into a network call
across a service boundary, need eventual consistency between the API's
writes and the worker's reads, and buy nothing — there is no requirement
here that the worker's data live anywhere the API's data doesn't.

**A worker is a second composition root, not a new module.** `Valuation.Worker`
is a new top-level project (`src/Seamline.Valuation.Worker`) that references
`Seamline.Modules.Risk` and `Seamline.Modules.MarketData` directly — the same
relationship `Seamline.Api`'s `Program.cs` already has with every module's
implementation project. This is allowed because `Seamline.Api` and
`Valuation.Worker` are composition roots (hosts), not modules; the module
boundary rules in ADR-0001 govern module-to-module references, not
host-to-module ones. `Valuation.Worker` does not become "the Valuation
module" — it hosts `Risk`'s own valuation logic on a different trigger
(Hangfire EOD, not HTTP) and a different schedule from the API.

## Consequences

### Positive

- EOD revaluation load never contends with live API request handling —
  genuinely isolated failure and load domains, the actual justification
  ADR-0001 promised rather than a speculative one.
- No new database, no distributed transaction, no eventual-consistency
  reasoning between the worker and the API — the same dividend ADR-0008
  already named for the saga staying in-process.
- The criterion is reusable: `Reporting.Worker` gets built against the same
  test ("does this have a load/timing profile the API process shouldn't
  absorb?") instead of a fresh argument each time a new worker is proposed.

### Negative

- Two deployables to run and monitor locally (`dotnet run` twice) instead of
  one — a small but real operational cost for a project that otherwise
  stays at "one process, one `docker compose up`."
- `Valuation.Worker` takes a direct dependency on `Seamline.Modules.Risk`
  and `Seamline.Modules.MarketData`'s implementation assemblies, the same
  way `Seamline.Api` does — if `Risk`'s internals ever needed to become
  genuinely private to `Seamline.Api` alone, this dependency would have to
  be re-examined. Not expected to happen; noted as a real coupling, not a
  hypothetical one.

## Alternatives considered

**Run EOD valuation as an in-process Hangfire job inside `Seamline.Api`.**
Rejected. This is the cheapest option and was seriously considered — no new
project, no new deployable. Rejected specifically because ADR-0001 already
committed to a separate process for this, and the reasoning holds: an EOD
batch job over the whole book is exactly the kind of sustained load that
contends with API request latency if it shares a thread pool and connection
pool with live traffic.

**Full service split — separate database for Valuation.** Rejected. Nothing
about `Valuation.Worker`'s job requires data isolation from `Risk` — it
reads `Position` and writes `valuation_snapshot`, both naturally `Risk`'s
data. A separate database would add distributed-transaction and
eventual-consistency complexity to buy an isolation property nothing here
needs, the same argument ADR-0001 already made against microservices
generally.

## Revisit criteria

- **If `Valuation.Worker`'s load ever needs independent scaling from
  `Seamline.Api`'s own database connection budget** (e.g., a much larger
  book makes EOD revaluation contend for connections with live traffic even
  running as a separate process against the same DB): reconsider a
  read-replica or a separate database at that point, not speculatively now.
- **If a third worker is proposed**: apply the same test from this ADR
  ("does this have a load/timing profile the API process shouldn't
  absorb?") rather than defaulting to extraction because two precedents
  already exist.
