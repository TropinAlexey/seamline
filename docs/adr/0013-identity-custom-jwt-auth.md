# ADR-0013: Identity — Custom JWT Auth for FO/MO/BO

**Status:** Accepted
**Date:** 2026-07

## Context

`Identity` had existed since the initial scaffold as an empty project — a
`.csproj` and a `RootNamespace`, no entity, no `DbContext`, no migration,
deliberately deferred. In its place, two request headers stood in for
authentication: `X-Tenant-Id` (required on every request, read by
middleware into `TenantContext`) and `X-Actor-Role` (checked only on
`POST /trades/{id}/approve` and `/reject`, via a local `IsRiskActor` helper
in `TradingEndpoints`). Neither header is verified against anything —
either one can be set to any value by the caller. The TODO tracker carried
both the empty module and "FO/MO/BO segregation through real roles" as open
gaps.

## Decision

**Own minimal auth, not a library or an external IdP.**

`Identity` owns an `identity.user` table (`Login`, `PasswordHash`, `Role`,
`TenantId`) and issues JWTs from `POST /auth/login`. Rejected alternatives:

- **ASP.NET Core Identity** — brings its own EF Core schema and migration
  set for `IdentityUser`/`IdentityRole` that adds little at this scale; the
  three roles and one credential check this project needs don't justify the
  library's surface.
- **External IdP (Keycloak, Duende IdentityServer)** — the realistic choice
  for production CTRM software, but out of scope for a local pet project:
  another container/service to run, and Duende's commercial license for
  anything beyond a small-business tier mirrors the exact reasoning ADR-0009
  already gave for pinning MassTransit below 9.x. Mirrors the Aspire/Dapr
  rejection in ADR-0001 — not reopened without a new reason.

**Roles are FO / MO / BO**, exposed as string constants in
`Identity.Contracts` (`IdentityRoles.FrontOffice/.MiddleOffice/.BackOffice`)
— the one thing another module legitimately needs from `Identity`: a
compile-time-safe name for `RequireAuthorization(policy =>
policy.RequireRole(...))`, instead of the magic string `"risk"` the header
stub used. No query interface into `Identity` exists or is needed —
authorization reads `ClaimsPrincipal`, not a synchronous call back into the
module.

**Role → endpoint mapping**, replacing the two stand-ins:

- `X-Tenant-Id` → the `tenant_id` claim on the JWT, set once at login. A
  claims-based middleware (replacing the old header middleware) reads it
  into `TenantContext` after authentication runs. `POST /auth/login` itself
  takes `TenantId` in the request body — there is no authenticated context
  yet to read it from — then explicitly calls
  `TenantContext.SetTenant(...)` *before* querying `IdentityDbContext`, the
  same pattern `TenantContext`'s own doc comment already describes for
  message consumers. This keeps `identity.user`'s RLS policy identical in
  shape to every other tenant-owned table — no special-cased exception for
  the one table that has to be queried before authentication.
- `X-Actor-Role: risk` on approve/reject → `RequireAuthorization(policy =>
  policy.RequireRole(IdentityRoles.MiddleOffice))`. This is the one place
  the header stub already encoded a real segregation-of-duties story (the
  approver must not be the trader who booked the trade) — MO is the
  business role that story maps to.
- Trade-booking actions (`POST /trades`, `/submit`, `/amend`, `/cancel`,
  `/deliver`) → FO.
- `GET /invoices` (Settlement) → BO — the only endpoint anywhere in the
  system today that a back-office role reads.
- Everything else (Reference, MarketData, Risk reads, `GET /trades/{id}`)
  → any authenticated user, no specific role. There is no segregation
  story for these yet; gating them by role would be inventing an RBAC
  matrix nothing in the domain currently asks for.

**Passwords hash with stdlib `Rfc2898DeriveBytes.Pbkdf2`** (PBKDF2-HMACSHA256,
100k iterations, random salt) — the same primitive ASP.NET Core Identity's
own `PasswordHasher` uses internally. No new hashing dependency; the BCL
already has a correct one. **JWT creation and validation use
`System.IdentityModel.Tokens.Jwt` / `Microsoft.AspNetCore.Authentication.JwtBearer`**
— unlike hashing, correct JWT signing isn't a few lines of stdlib code
worth reinventing, so this is a real new dependency, added deliberately.

**Demo users are seeded via raw SQL in the `InitialCreate` migration** (one
fixed tenant, one user per role), not a self-service registration endpoint
— nothing else in this project needs account creation, and building a
registration flow for a demo-only need would be exactly the "no
half-finished implementations" case to avoid.

## Consequences

### Positive

- Both open TODO items close together: `Identity` stops being an empty
  scaffold, and FO/MO/BO become real, JWT-verified roles instead of an
  unverified header.
- `X-Tenant-Id` disappears entirely — tenant provenance now traces to a
  signed claim set at login, not a client-supplied header trusted at face
  value on every request.
- The `IdentityRoles` constants in `Contracts` are the first example in this
  codebase of a module's `Contracts` project holding something other than a
  DTO, query interface, or integration event — a deliberate, narrow
  exception (pure string constants, zero behavior, zero dependencies beyond
  what `Contracts` already allows).

### Negative

- The JWT signing key lives in `appsettings.json` as a static dev secret,
  not a real secrets store — acceptable for a local/demo project, explicitly
  not a production posture. Noted in README's simplifications section.
- Login is `(TenantId, Login, Password)`, not `(Login, Password)` with the
  tenant inferred from the account — a caller has to already know which
  tenant they're logging into. Simpler and more consistent with the
  existing RLS pattern than a global-login-namespace design, but not how a
  real product's login page would work.
- Roles are a single string per user, not a set — a user cannot hold both
  FO and BO simultaneously. Fine for three roles with disjoint
  responsibilities; would need revisiting if a real org chart required
  multi-role users.

## Revisit criteria

- **If a second write action needing segregation of duties appears**
  (e.g. a BO-only settlement action): extend the role mapping then, not
  speculatively now.
- **If this ever needs to run outside a local/demo context**: replace the
  static config-file signing key with a real secret store, and reconsider
  the external-IdP alternative rejected above — the reasoning that rejected
  it here is explicitly about local/demo scope, not a permanent judgment.
