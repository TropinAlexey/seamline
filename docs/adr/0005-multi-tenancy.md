# ADR-0005: Shared Schema with `tenant_id`, Not Database-per-Tenant

**Status:** Accepted
**Date:** 2026-07

## Context

Seamline is multi-tenant: multiple trading companies use the same deployment,
and one tenant must never see another tenant's trades, positions, or
counterparties. Tenant isolation is a hard security requirement, not a nice
to have.

Production experience available to this project is **tenant-per-database**
(PostgreSQL, 175 tenants, a separate database per tenant), which is a proven
pattern for that specific profile: large tenant count, need for per-tenant
operational isolation (backup, restore, scaling a single noisy tenant), and
an existing operational toolchain to manage dynamic connection strings and
per-tenant migrations.

Seamline's profile is different: a low, fixed number of demo tenants, no
requirement to scale or operate tenants independently, and a project goal
that is explicitly about demonstrating module boundaries and messaging
architecture, not multi-tenancy operations. Repeating tenant-per-database
here would not add a new data point to the portfolio — it is already
demonstrated — and would add real operational cost (dynamic connection
strings, per-tenant migration orchestration, more complex integration
tests) that competes with the project's actual goals for a limited time
budget.

## Decision

**Shared schema, single database, `tenant_id` column on every tenant-owned
table**, enforced primarily by an **EF Core global query filter**.

Concretely:

- Every entity that is tenant-owned has a non-nullable `tenant_id` column.
- Each module's `DbContext` applies `HasQueryFilter(e => e.TenantId ==
  _tenantContext.TenantId)` to every tenant-owned entity type.
- `ITenantContext` (in `Seamline.SharedKernel`) is resolved per-request from
  a claim/header by API middleware and injected wherever a `DbContext` is
  constructed.
- PostgreSQL Row-Level Security is the planned second enforcement layer —
  a `CREATE POLICY` per tenant-owned table keyed on a session variable the
  middleware sets — so that a forgotten query filter or a raw SQL statement
  still cannot cross tenants. Not yet implemented in Phase 1; tracked as
  follow-up, not silently dropped.

## Consequences

### Positive

- No dynamic connection strings, no per-tenant migration orchestration, no
  per-tenant provisioning workflow to build or test.
- Testcontainers-based integration tests run against one PostgreSQL
  container, not N.
- Adding a tenant is inserting a row, not provisioning infrastructure.
- The pattern is intentionally different from the production experience
  already on record, which is a stronger portfolio signal than repeating it:
  it shows the judgment to pick shared-schema when the profile calls for it,
  not just familiarity with one pattern.

### Negative

- **Enforcement lives in application code by default.** An EF Core query
  filter is bypassed by `.IgnoreQueryFilters()`, raw SQL, or a new query path
  that forgets to apply it. This is the reason RLS is the planned second
  layer rather than an optional nice-to-have — until it lands, a single
  missed filter is a real cross-tenant leak, not a theoretical one.
- **No per-tenant operational isolation.** A single tenant's data cannot be
  backed up, restored, or scaled independently of the others. Accepted:
  Seamline's tenant count and profile do not require this.
- **Noisy-neighbor risk.** A large tenant's queries share the database's
  resources with every other tenant. Accepted at this scale.

## Alternatives considered

**Database-per-tenant.** Rejected for this project — see Context. Already
demonstrated in production (Lakmus); would not add signal here and adds
operational cost that competes with the project's actual architectural
goals.

**Schema-per-tenant (one PostgreSQL schema per tenant, same database).**
A middle ground between the two above. Rejected: still requires dynamic
schema resolution and per-tenant migration application, without the full
operational isolation that would justify that cost. Shared schema with RLS
gives most of the same safety guarantee at a fraction of the operational
complexity.

## Revisit criteria

Move toward database-per-tenant or schema-per-tenant if any of the
following becomes true:

- A tenant requires independent backup/restore, scaling, or a data
  residency guarantee that shared infrastructure cannot provide.
- Tenant count or per-tenant data volume grows to where noisy-neighbor
  effects are measurable and matter.

Until then, shared schema plus `tenant_id` (and RLS once implemented) is
the right trade-off.
