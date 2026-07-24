<img src="docs/seamline-icon.png" alt="seamline" width="96" align="left" />

# seamline

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
requires reporting a trade as it stood at the moment of reporting, not as it
stands now; a worker is extracted for mark-to-market because revaluing a
whole book on a curve update is a genuinely different load profile from an
HTTP request; a saga has a real compensating transaction because credit
limits are reserved and released, not invented to demonstrate a saga.

## Architecture

```
Seamline.Api  (modular monolith, .NET 10)

Modules/
  Reference/    commodities, counterparties,
                delivery points, calendars
  Trading/      trade capture + lifecycle
  MarketData/   forward curves, fixings
  Risk/         positions, MtM, credit exposure
  Settlement/   invoices, netting, payments
  Identity/     tenants, users, roles
  Audit/        cross-module actor/action/timestamp/context log

PostgreSQL, one schema per module
Multi-tenant: shared schema + tenant_id
MassTransit (in-memory transport in Phase 1)
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
        .Select(name => $"Seamline.Modules.{name}.Internal");

    var result = Types.InAssembly(assembly)
        .Should().NotHaveDependencyOnAny(otherModuleImplNamespaces.ToArray())
        .GetResult();

    Assert.True(result.IsSuccessful, ...);
}
```

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

Two services are extracted from the monolith on purpose, once Phase 2 lands:
`Reporting.Worker` (REMIT/ACER submission) and `Valuation.Worker`
(mark-to-market fan-out). Both share the same PostgreSQL database — this is
service-based architecture, stated as such, not database-per-service.
Full reasoning: `docs/adr/0001-modular-monolith.md`.

## Scope boundaries

- Physical forwards only, power and gas. No options.
- Monthly delivery periods only.
- Mark-to-market: `(forward_price − trade_price) × volume`. Flat monthly
  curve points — no interpolation, shaping, or cascading.
- No VaR. Stress scenarios instead (Phase 2).
- REMIT: simplified XML against a stub regulator endpoint (Phase 2).

## ADRs

| ADR | Topic |
|---|---|
| [0001](docs/adr/0001-modular-monolith.md) | Modular monolith instead of microservices |
| [0005](docs/adr/0005-multi-tenancy.md) | Multi-tenancy: shared schema + `tenant_id`, not database-per-tenant |
| [0006](docs/adr/0006-audit-trail-instead-of-event-sourcing.md) | Versioned append-only history instead of Event Sourcing |
| [0007](docs/adr/0007-decimal-rounding.md) | `decimal` for money and volume, explicit rounding |
| [0008](docs/adr/0008-saga-placement-and-ownership.md) | Credit-limit saga: lives in Trading, only engages on a limit breach |
| [0009](docs/adr/0009-masstransit-version-pin.md) | MassTransit pinned to 8.5.10 — 9.x requires a commercial license |

More ADRs land as decisions are made — see `CLAUDE.md`.

## Running locally

```bash
docker compose up -d          # PostgreSQL
dotnet build SeamlineCtrm.sln
dotnet test SeamlineCtrm.sln
dotnet run --project src/Seamline.Api
```

## Status

Phase 1 in progress. Landed so far:

- Module boundaries + the six architecture tests above, all running in CI.
- A working vertical slice across three modules: book a trade, submit it,
  see the derived position — Reference → Trading → Risk, talking only
  through Contracts and MassTransit, never a direct reference.
- The credit-limit saga ([ADR-0008](docs/adr/0008-saga-placement-and-ownership.md)):
  within-limit trades activate immediately; a breach parks the trade pending
  a `risk`-role approval, with a timeout and a compensating release if
  nobody responds.
- Append-only trade history ([ADR-0006](docs/adr/0006-audit-trail-instead-of-event-sourcing.md))
  with the immutability guarantee enforced by PostgreSQL itself: the app
  connects as a restricted role that has `SELECT`/`INSERT` on history tables
  and nothing else — migrations run as a separate, more privileged role.

Still open for Phase 1: the remaining trade states (`Cancelled`, `Amended`,
`Delivered`, `Settled`), PostgreSQL Row-Level Security as a second
multi-tenancy layer, and the `MarketData`/`Settlement`/`Identity` modules,
which are still empty scaffolding.
