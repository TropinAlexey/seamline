<img src="docs/seamline-icon.png" alt="seamline" width="96" align="left" />

# seamline
mini SaaS CTRM demo project

[![CI](https://github.com/TropinAlexey/seamline/actions/workflows/ci.yml/badge.svg)](https://github.com/TropinAlexey/seamline/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-336791?logo=postgresql&logoColor=white)

Multi-tenant commodity trading & risk platform (mini-CTRM) for power and gas
forwards, in .NET 10 — a modular monolith with boundaries enforced in CI, and
two services extracted on purpose.

<br clear="left" />

> Simplified for demonstration; not a compliant REMIT implementation.
> Clean-room implementation. No code, schemas, or business rules from any
> employer or commercial CTRM product.

## Why this domain

Commodity trading gives every architectural decision here a real reason to
exist instead of a contrived one: trades are versioned because a regulator
(a certain group of people with a keen interest) requires reporting a trade
as it stood at the moment of reporting, not as it stands now; a worker is
extracted for mark-to-market because revaluing a whole book on a curve
update is a genuinely different load profile from an HTTP request; a saga
has a real compensating transaction because credit limits are reserved and
released, not invented to demonstrate a saga.

## Architecture

```
Seamline.Api              (modular monolith)
Seamline.Valuation.Worker (separate process, same database — EOD MtM)
Seamline.Reporting.Worker (separate process, same database — EOD REMIT batch)
Seamline.AcerStub         (stub regulator endpoint, local dev only)

Modules/
  Reference/    commodities, counterparties,
                delivery points, calendars
  Trading/      trade capture + lifecycle
  MarketData/   forward curves, fixings
  Risk/         positions, MtM, credit exposure
  Settlement/   invoices, netting, payments
  Identity/     tenants, users, roles, JWT auth
  Audit/        cross-module actor/action/timestamp/context log

PostgreSQL, one schema per module
Multi-tenant: shared schema + tenant_id global filter, JWT-carried tenant claim
MassTransit (in-memory transport in Phase 1)
Hangfire (Valuation.Worker's and Reporting.Worker's EOD schedulers, Phase 2)
```

Each module is two projects — `Seamline.Modules.<Name>` (implementation) and
`Seamline.Modules.<Name>.Contracts` (public surface: DTOs, query interfaces,
integration events). A module's implementation can never reference another
module's implementation — not by convention, but because implementation
types are `internal` and the dependency itself is forbidden by an
architecture test that runs in CI:

```csharp
[Theory]
[MemberData(nameof(Modules))]
public void Module_implementation_must_not_depend_on_another_modules_implementation(string moduleName)
{
    var assembly = Assembly.Load($"Seamline.Modules.{moduleName}");

    var otherModuleImplNamespaces = ModuleNames
        .Where(name => name != moduleName)
        .Select(name => $"Seamline.Modules.{name}.Internal")
        .ToArray();

    var result = Types.InAssembly(assembly)
        .Should()
        .NotHaveDependencyOnAny(otherModuleImplNamespaces)
        .GetResult();

    Assert.True(result.IsSuccessful,
        $"{moduleName} depends directly on another module's implementation: " +
        string.Join(", ", result.FailingTypeNames ?? []));
}
```
_(verbatim from `tests/Seamline.ArchTests/ModuleBoundaryTests.cs`)_

That's one of six rules `Seamline.ArchTests` runs on every build, each
stated as prose somewhere in `CLAUDE.md` or an ADR and enforced here instead
of trusted to hold:

| Rule | Where it's stated |
|---|---|
| A module's implementation never depends on another module's implementation | `CLAUDE.md` |
| A `.Contracts` assembly never depends on any implementation | `CLAUDE.md` |
| A `.Contracts` assembly depends on nothing but `SharedKernel` | `CLAUDE.md` |
| Money and volume fields are never `double`/`float` | `CLAUDE.md`, [ADR-0007](docs/adr/0007-decimal-rounding.md) |
| An implementation assembly exposes nothing public beyond its DI/endpoint composition root | `CLAUDE.md` ("internal by default") |
| No migration adds a foreign key across module schemas | `CLAUDE.md` |

The last one runs each migration's `Up()` against a real `MigrationBuilder`
and inspects the resulting operations — including foreign keys declared
inline inside `CreateTable`, which don't show up as a top-level operation
and would otherwise make the check pass vacuously.

Two components are extracted from the monolith on purpose, and both have
now landed: `Valuation.Worker` (end-of-day mark-to-market) and
`Reporting.Worker` (end-of-day simplified REMIT submission). Both share the
same PostgreSQL database — this is service-based architecture, stated as
such, not database-per-service. Neither is a new module: each is a second
(third) composition root referencing a module's implementation project
directly, the same relationship `Seamline.Api` already has with every
module — `Valuation.Worker` hosts `Risk`'s valuation logic,
`Reporting.Worker` hosts `Trading`'s REMIT batch, both reached through one
public extension method each rather than a curated subset of the module's
internals. See `docs/adr/0001`/`docs/adr/0002` for the extraction criteria,
`docs/adr/0014`/`docs/adr/0015` for what each worker actually computes.
`Reporting.Worker` submits against `Seamline.AcerStub`, a minimal stand-in
regulator endpoint (random 500s/timeouts/duplicates) that only exists to
give the retry/idempotency logic something genuinely flaky to run against.

## Scope boundaries

- Physical forwards only, power and gas. No options.
- Monthly delivery periods only.
- Mark-to-market: `(forward_price − trade_price) × volume`. Flat monthly
  curve points — no interpolation, shaping, or cascading.
- No VaR. Stress scenarios instead (Phase 2).
- REMIT: simplified XML against `Seamline.AcerStub`, a stub regulator
  endpoint — not a compliant REMIT/ACER implementation. See
  [ADR-0015](docs/adr/0015-reporting-worker.md).
- Auth ([ADR-0013](docs/adr/0013-identity-custom-jwt-auth.md)): JWT signing
  key lives in `appsettings.json` as a static dev-only secret, not a real
  secrets store — fine for a local/demo project, explicitly not a
  production posture. Three demo users (one per FO/MO/BO role) are seeded
  by the `Identity` module's `InitialCreate` migration with a documented
  password (`Demo-Password-123!`) for a fixed demo tenant
  (`11111111-1111-1111-1111-111111111111`).

## ADRs

| ADR | Topic |
|---|---|
| [0001](docs/adr/0001-modular-monolith.md) | Modular monolith instead of microservices |
| [0002](docs/adr/0002-service-extraction-criteria.md) | Extracting a process without extracting a database |
| [0003](docs/adr/0003-hangfire-vs-masstransit-scheduling.md) | Hangfire vs MassTransit `Schedule<>`: instance timeout vs recurring work |
| [0004](docs/adr/0004-transactional-outbox.md) | Transactional outbox for published events |
| [0005](docs/adr/0005-multi-tenancy.md) | Multi-tenancy: shared schema + `tenant_id`, not database-per-tenant |
| [0006](docs/adr/0006-audit-trail-instead-of-event-sourcing.md) | Versioned append-only history instead of Event Sourcing |
| [0007](docs/adr/0007-decimal-rounding.md) | `decimal` for money and volume, explicit rounding |
| [0008](docs/adr/0008-saga-placement-and-ownership.md) | Credit-limit saga: lives in Trading, only engages on a limit breach |
| [0009](docs/adr/0009-masstransit-version-pin.md) | MassTransit pinned to 8.5.10 — 9.x requires a commercial license |
| [0010](docs/adr/0010-audit-module-placement.md) | Audit module placement: a pure sink, never publishes |
| [0011](docs/adr/0011-trade-lifecycle-extension.md) | Trade lifecycle: `Cancelled`/`Amended`/`Delivered` |
| [0012](docs/adr/0012-marketdata-settlement-first-entities.md) | MarketData and Settlement's first entities |
| [0013](docs/adr/0013-identity-custom-jwt-auth.md) | Identity: custom JWT auth, FO/MO/BO roles |
| [0014](docs/adr/0014-valuation-worker.md) | Valuation.Worker: real mark-to-market |
| [0015](docs/adr/0015-reporting-worker.md) | Reporting.Worker: simplified REMIT submission |

More ADRs land as decisions are made — see `CLAUDE.md`.

## Running locally

```bash
docker compose up -d          # PostgreSQL + acer-stub
dotnet build SeamlineCtrm.sln
dotnet test SeamlineCtrm.sln
dotnet run --project src/Seamline.Api                # API — migrates every module's schema on startup
dotnet run --project src/Seamline.Valuation.Worker    # optional — EOD MtM, same database
dotnet run --project src/Seamline.Reporting.Worker    # optional — EOD REMIT batch, same database
```

`POST /auth/login` with `{"tenantId": "11111111-1111-1111-1111-111111111111",
"login": "trader", "password": "Demo-Password-123!"}` (or `risk`/`backoffice`
for the MO/BO demo users) returns a JWT — every other endpoint requires
`Authorization: Bearer <token>`. See [ADR-0013](docs/adr/0013-identity-custom-jwt-auth.md).

## Status

**Phase 1 complete.** Landed:

- Module boundaries + the six architecture rules above, enforced by 42 tests
  running in CI, plus a coverage gate on `Trade`/`TradeHistory`.
- A working vertical slice across every module: book a trade, submit it,
  see the derived position — Reference → Trading → Risk, talking only
  through Contracts and MassTransit, never a direct reference.
- The credit-limit saga ([ADR-0008](docs/adr/0008-saga-placement-and-ownership.md)):
  within-limit trades activate immediately; a breach parks the trade pending
  an MO-role approval, with a timeout and a compensating release if nobody
  responds.
- The full trade lifecycle ([ADR-0011](docs/adr/0011-trade-lifecycle-extension.md)):
  `Draft → Submitted → Active | CreditPending → Active | Rejected`, plus
  `Cancelled`, `Amended`, `Delivered`.
- Append-only trade history ([ADR-0006](docs/adr/0006-audit-trail-instead-of-event-sourcing.md))
  and a cross-module audit log ([ADR-0010](docs/adr/0010-audit-module-placement.md)),
  both immutability-guaranteed by PostgreSQL itself: the app connects as a
  restricted role with `SELECT`/`INSERT` only on these tables — migrations
  run as a separate, more privileged role.
- Multi-tenancy, two layers ([ADR-0005](docs/adr/0005-multi-tenancy.md)): an
  EF Core query filter, plus PostgreSQL Row-Level Security keyed on a
  session variable a connection interceptor sets on every connection open.
- Real JWT authentication ([ADR-0013](docs/adr/0013-identity-custom-jwt-auth.md)):
  three roles (FO/MO/BO) gate trade booking, credit approval, and invoice
  reads respectively — no more unverified `X-Tenant-Id`/`X-Actor-Role`
  headers.
- MarketData and Settlement's first entities ([ADR-0012](docs/adr/0012-marketdata-settlement-first-entities.md)):
  published curve points and delivery-triggered invoices.
- 139 tests: unit (Trading, Risk, Identity), architecture, and integration
  (Testcontainers + `WebApplicationFactory`).

**Phase 2 well underway.** Both processes ADR-0001 promised are now built:

- `Valuation.Worker` ([ADR-0002](docs/adr/0002-service-extraction-criteria.md),
  [ADR-0014](docs/adr/0014-valuation-worker.md)) — end-of-day mark-to-market
  `(forward_price − trade_price) × volume` on a Hangfire recurring job,
  against a volume-weighted cost basis `Position` now tracks.
- `Reporting.Worker` ([ADR-0015](docs/adr/0015-reporting-worker.md)) —
  end-of-day simplified REMIT submission, scanning `trade_history` (never
  live `trade` state, per [ADR-0006](docs/adr/0006-audit-trail-instead-of-event-sourcing.md)'s
  point-in-time requirement) for `New`/`Modify`/`Terminate` reports, sent
  to `Seamline.AcerStub` through an `IHttpClientFactory` client with
  `Microsoft.Extensions.Http.Resilience`'s standard retry/timeout/
  circuit-breaker handler. Idempotency is presence-of-row, not a status
  column — an unreported `trade_history` version is retried automatically
  by the next run's `LEFT JOIN`.
- Also written up: [ADR-0003](docs/adr/0003-hangfire-vs-masstransit-scheduling.md)
  (Hangfire vs MassTransit `Schedule<>`) and
  [ADR-0004](docs/adr/0004-transactional-outbox.md) (transactional
  outbox) — both document decisions already implemented in earlier
  sessions, closed retroactively per CLAUDE.md's own exception for exactly
  this kind of debt.

Still open for Phase 2: RabbitMQ in place of the in-memory MassTransit
transport, the rest of the Hangfire jobs (curve import, deadline sweeps),
stress scenarios in place of VaR, and a MassTransit test harness.
