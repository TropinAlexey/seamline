# ADR-0030: Thin web UI — scope, stack, same-origin deployment

## Status

Accepted

## Context

The backend is the product (ADR-0001), but a reviewer cannot see a saga,
four-eyes approval, role separation or RLS without cloning the repo and
driving it with curl. A UI exists to make that machinery visible in a few
minutes. The same goal also bounds it: it must not pull the project's
centre of gravity away from the backend.

Building the UI against the current API surfaced backend gaps that a UI
would expose on screen. Invalid transitions returned 500. An unknown
counterparty returned 500. The approver was recorded as a hard-coded
`"risk"`. Login had one global rate-limit window that any client could
exhaust for everyone.

## Decision

- **Thin client, no domain logic.** React 19 + TypeScript (strict), Vite,
  TanStack Query, React Router, Mantine. Blazor WebAssembly would keep the
  stack all-.NET but is less legible to the wider reviewer audience. The
  browser never adds, multiplies or rounds money or volume: every notional,
  MTM and total is computed server-side (ADR-0007), and `web/src/format/`
  only formats.
- **Same-origin, `/api` prefix at the proxy only.** nginx (compose), the
  Vite dev server and the k8s Ingress serve the SPA at `/` and forward
  `/api/*` to the API with the prefix stripped. A prefix is required because
  the SPA routes (`/trades/:id`, `/invoices`, …) would otherwise collide
  with the API routes, and a refresh on a deep link would hit the API. The
  API's own routes do not change. No CORS policy is needed.
- **The OpenAPI document is the contract.** It is generated at build time
  (`Microsoft.Extensions.ApiDescription.Server`) into
  `web/openapi/seamline.json` and committed. The TypeScript client is
  generated from that file, and CI fails if it is stale. Build-time
  generation runs `Program.cs` up to `app.Run()`, so DB initialisation is
  skipped when the entry assembly is `GetDocument.Insider`. Anonymous
  response objects are replaced by named records with `Produces<T>`
  metadata so the schema is useful. Wire format is unchanged.
- **Token in memory, mirrored to `sessionStorage`.** A tab-scoped demo
  session; `localStorage` would outlive the tab for no benefit. Both are
  readable by an XSS payload. An httpOnly cookie would close that hole but
  means CSRF handling and a second auth path next to ADR-0013's bearer
  token, which is too much for a demo UI. Roles are read from the decoded,
  unverified JWT for **display only**. The server stays authoritative
  (403).
- **Polling now, push later.** TanStack Query `refetchInterval`, plus a 1s
  targeted poll after a `202 Accepted`. SignalR is a separate decision if it
  ever happens.
- **`last_changed_at` column on `trading.trade`** for blotter ordering,
  set wherever `Version` is bumped and backfilled from `trade_history`.
  It replaces a correlated subquery against an append-only table on every
  page.
- **Backend fixes shipped with the UI:**
  `DomainRuleViolationException` (SharedKernel) maps to 409. An unknown
  counterparty on create maps to 400. The approver's login travels through
  the saga messages into `changedBy` and the audit actor. Login rate limiting
  is partitioned by client IP. `X-Forwarded-For` is honoured only when
  `ReverseProxy:KnownNetworks` is set, and then only for exactly
  `ReverseProxy:ForwardLimit` hops: one in compose (nginx, pinned to a
  single address), two in k8s (Traefik and nginx). With no trusted proxies
  configured the middleware is not registered at all. ASP.NET Core treats
  empty known-proxy lists as "trust everyone", which would let any client
  forge a fresh rate-limit window.
  `POST /curve-points` is MO-only.

## Consequences

- Decimals cross the wire as JSON numbers and land in float64. At this
  project's magnitudes that is exact for display. If it stops being so, the
  fix is serialising decimals as strings server-side, a separate ADR.
- One more container (`web`, nginx) in compose and Helm. The demo
  environment settings (Development, relaxed login limit, worker ports for
  the Hangfire dashboard) live in `docker-compose.override.yml`, so base
  compose keeps production defaults.
- Every new endpoint must keep the OpenAPI document in sync. CI enforces it.
- The UI hides actions a role cannot perform, but every such action is also
  covered by a 403 integration test. Hiding is UX, not security.
