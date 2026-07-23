# seamline

Multi-tenant commodity trading & risk platform (mini-CTRM) for power and gas
forwards, built as a modular monolith with two services extracted on purpose.

## Architectural thesis

Microservices solve an organizational scaling problem, not a technical one.
Default is a modular monolith with boundaries enforced by architecture tests
in CI, not by code review discipline. Services are extracted only when a
component has a genuinely different load, failure, or SLA profile — see
`docs/adr/0001-modular-monolith.md` and (when written) the ADR on service
extraction criteria.

## Module boundary rules (hard constraints)

- Modules live under `src/Modules/<Name>/` as two projects: `<Name>` (impl)
  and `<Name>.Contracts` (public DTOs, query interfaces, integration events).
- Implementation projects use `RootNamespace = Seamline.Modules.<Name>.Internal`,
  not `Seamline.Modules.<Name>` — this keeps the impl namespace from being a
  textual prefix of `Seamline.Modules.<Name>.Contracts`, which would otherwise
  produce false positives in the NetArchTest namespace checks below.
- A module's impl project may only reference: its own Contracts, SharedKernel,
  and the Contracts of modules it legitimately depends on. Never another
  module's impl project.
- A Contracts project may only reference SharedKernel. Never any impl project,
  including its own.
- Cross-module communication is either a read-only query interface exposed in
  Contracts (synchronous, in-process, resolved via DI), or an integration
  event carried over MassTransit (asynchronous — in-memory transport in
  Phase 1, RabbitMQ from Phase 2). No direct calls into another module's
  internals.
- Both directions are enforced by `Seamline.ArchTests` (NetArchTest) in CI:
  a module's implementation must not depend on another module's
  implementation, and a Contracts assembly must not depend on any
  implementation assembly.
- No foreign keys across module schemas in PostgreSQL. Referential integrity
  between modules is eventual, via events, not enforced at the database level.

## No MediatR

Minimal API endpoints call application services directly through DI — a
command/query mediator adds no value on top of that. Cross-module
notifications go through MassTransit (`IPublishEndpoint` / `IConsumer<T>`),
which is a real message bus, not a MediatR replacement wearing a different
name. MassTransit is wired from Phase 1 with the in-memory transport;
switching to RabbitMQ in Phase 2 is a transport configuration change, not a
rewrite of publishers or consumers. MassTransit is pinned to `8.5.10` — 9.x
requires a commercial license (see `docs/adr/0008-masstransit-version-pin.md`);
do not float this dependency without reading that ADR first.

## Any architectural decision → a new ADR

`docs/adr/`, one page, Context / Decision / Consequences. Written when the
decision is made, not retroactively.

## Clean room

No code, configuration, schema, or business rule from any employer or
commercial CTRM/ETRM product. Only public domain knowledge and public
REMIT/ACER specifications. Simplifications are deliberate and stated in
README.

## Scope boundaries

- Physical forwards only, power and gas. No options.
- Monthly delivery periods only.
- Mark-to-market: `(forward_price - trade_price) * volume`. No curve
  interpolation, shaping, or cascading — flat monthly points.
- No VaR. Stress scenarios instead.
- REMIT reporting is simplified XML + acer-stub; not a compliant
  implementation. State this in README.

## Money and volumes

Always `decimal`. Rounding is explicit at the point it happens, never
implicit via type conversion. See `docs/adr/0007-decimal-rounding.md`.

## Multi-tenancy

Shared schema, `tenant_id` on every table, EF Core global query filter as the
primary enforcement mechanism. See `docs/adr/0005-multi-tenancy.md`.

## Commands

- Build: `dotnet build SeamlineCtrm.sln`
- Test: `dotnet test SeamlineCtrm.sln`
- Run API: `dotnet run --project src/Seamline.Api`
- Local Postgres: `docker compose up -d`
- EF Core migrations: `dotnet ef migrations add <Name> --project src/Modules/<Module>/Seamline.Modules.<Module> --startup-project src/Seamline.Api`
