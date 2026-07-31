# ADR-0018: Curve Import

**Status:** Accepted
**Date:** 2026-07

## Context

The TODO tracker carried "Hangfire: curve import, deadline sweep, dashboard
behind auth" since Phase 2 began; `Valuation.Worker`/`Reporting.Worker`
closed the EOD/REMIT halves of it. Curve import — refreshing
`PriceCurvePoint` automatically instead of only via the manual
`POST /curve-points` endpoint (ADR-0012) — is the remaining piece.

**No genuinely free source of real forward curves exists.** A forward curve
(a price per future delivery month, many months out) is commercial,
licensed data everywhere checked (EEX, ICE). What's actually free and open
is *spot/day-ahead* price data — one number for one day, not a curve. Two
sources were found and verified to be real, free, and open, not trial/
freemium:

- **ENTSO-E Transparency Platform** — day-ahead electricity prices for the
  EU, published under a regulatory transparency mandate. Free, requires a
  self-service registered API token.
- **EIA Open Data API** (`api.eia.gov/v2`) — the US Energy Information
  Administration's Henry Hub natural gas spot price series (`RNGWHHD`).
  Free, requires a self-service registered API key. Geographically this is
  US data in a project otherwise framed around European REMIT/ACER
  concepts — a real mismatch, not hidden here or in the README, because no
  free/open European gas spot source was found.

Both are genuinely daily spot data, not a monthly forward point — the gap
between what CLAUDE.md's `PriceCurvePoint` model needs (one flat price per
`(commodity, month)`) and what these APIs return (one price per day) had to
be closed by *some* aggregation.

## Decision

**`ICurveSource.GetMonthlyAveragePriceAsync(commodityCode, month, ct)`** —
internal to `Seamline.Modules.MarketData`, returns the average of the
commodity's daily spot price across every elapsed day of the given month
(not a single latest-day price). This is a deliberate scope clarification:
CLAUDE.md rules out "curve interpolation, shaping, or cascading" *between*
delivery periods — a flat monthly point stays flat, no curve is being
fitted across months. Averaging several real daily observations into the
one number *for that single period* is not the shape CLAUDE.md's rule is
about; it is simply how "this month's price" gets derived from data that
only exists at daily granularity. Stated explicitly here so a future reader
doesn't read the averaging as a quiet erosion of the no-shaping rule.

**Three implementations, selected per commodity via config**
(`MarketData:CurveImport:Sources:POWER` / `:GAS`, values `"Synthetic"`,
`"EntsoE"`, `"Eia"`; default `Synthetic`):

- `SyntheticCurveSource` — no network call, no key. Deterministic per
  `(commodity, year, month)` (seeded, not `Random.Shared`), so a demo's
  numbers don't jitter every run, only drift month to month like a real
  curve would. The zero-configuration default for both commodities, so
  `docker compose up` plus `dotnet run` works with no API keys anywhere.
- `EntsoECurveSource` — real ENTSO-E day-ahead prices for POWER, one
  bidding zone hardcoded (DE-LU, `10Y1001A1001A82H`) for this demo. Fetches
  the whole elapsed-month range in one request and averages every hourly
  point in the response flat, not a two-step per-day-then-per-month
  average — negligible difference except across a DST transition, not
  worth the extra bucketing code here.
- `EiaCurveSource` — real EIA Henry Hub daily spot price for GAS, averaged
  across the elapsed days of the month the same way. EIA's series usually
  lags a day, so the source asks through yesterday, not today.

Both real sources use `IHttpClientFactory` + `AddStandardResilienceHandler`
(`Microsoft.Extensions.Http.Resilience`, Polly v8) for retry/timeout/
circuit-breaker — the same pattern and package version
`AddTradingReportingClient` already uses for `acer-stub` (ADR-0015). On
final failure (missing key, or the retried request still fails), the
source logs and returns `null` — `ICurveSource`'s contract is "never
throws." `CurveImportRunner` treats `null` as "skip this commodity this
run, the existing point stays as-is" — one source's outage never fails the
whole import.

**Selection uses .NET's keyed DI**, not a hand-rolled factory:
`AddKeyedScoped<ICurveSource, T>("Synthetic" | "EntsoE" | "Eia")`, resolved
via `GetRequiredKeyedService<ICurveSource>(configuredKey)`. The two real
sources are also registered as ordinary typed `HttpClient`s (so
`AddStandardResilienceHandler()` attaches normally) and forwarded into the
keyed slot with a factory delegate — no new abstraction invented just to
make keying and typed clients coexist.

**Scope: `CurveImportRunner` only refreshes a `(tenant, commodity)` pair
that already has at least one existing `PriceCurvePoint` row for that
commodity** — any period, not necessarily the current month. It does not
invent a brand-new tenant/commodity relationship out of nothing; a tenant
that has never published POWER or GAS manually is left alone by curve
import. This sidesteps a real design tension: the "which tenants need a
price" question is naturally Risk's data (tenants with open positions), but
`MarketData`'s implementation can't reference `Risk`'s implementation
(module boundary rule) — this scoping keeps `CurveImportRunner` entirely
within `MarketData`'s own bounded context, doing its own tenant discovery
from its own table, the same shape `EndOfDayValuationRunner` and
`RemitReportingRunner` already use (bypassing RLS via the owner connection,
one raw query, no central tenant registry — ADR-0005).

**The external price is fetched once per commodity per run, not once per
tenant** — the market price is the same for everyone, so
`CurveImportRunner` calls `ICurveSource` exactly twice per run (POWER, GAS)
regardless of how many tenants get updated, then loops tenants only for
the `PriceCurvePoint` write.

**Lives in `Valuation.Worker`, not a new worker, not `Seamline.Api`.**
Tested against ADR-0002's own criterion — "does this have a load/timing
profile the API process shouldn't absorb?" — curve import is a couple of
lightweight external HTTP calls, nowhere near the CPU/DB burst that
justified extracting `Valuation.Worker`/`Reporting.Worker` in the first
place. A third worker for this would fail the criterion the ADR itself set
up to prevent exactly that kind of default extraction. Two separate
Hangfire recurring jobs — `curve-import` (`Cron.Daily(5)`) and
`eod-valuation` (`Cron.Daily(6)`, moved off the unqualified midnight
default it used before) — rather than one combined job, since two
same-time `Cron.Daily()` entries have no guaranteed relative order and the
valuation run needs the day's import to have already landed.

## Consequences

### Positive

- Closes the last open Phase 2 TODO item; `PriceCurvePoint` can now stay
  current without a human remembering to call `POST /curve-points` daily.
- Real, verified free/open data sources named and used, not assumed —
  ENTSO-E and EIA were both checked to actually be free before being wired
  in, not chosen off a guess.
- `docker compose up` still needs zero API keys — `SyntheticCurveSource` is
  the default, real sources are opt-in per commodity.
- One external call per commodity per run, not per tenant — bounded,
  predictable load on ENTSO-E/EIA regardless of tenant count.

### Negative

- `EiaCurveSource` is genuinely US data standing in for a European-framed
  project's gas commodity — a real, documented mismatch, not a defect to
  silently work around later.
- `CurveImportRunner`'s "only refreshes what already exists" scoping means
  a brand-new tenant that has never manually published POWER/GAS gets no
  automatic price until they publish once themselves — a deliberate
  restraint, but a real onboarding step this doesn't automate away.
- `EndOfDayValuationRunner`'s recurring-job time moved from the unqualified
  midnight default to `Cron.Daily(6)` — a small operational behavior change
  for anyone who had the old schedule memorized.

## Alternatives considered

**A single global (not per-commodity) source selector.** Rejected — POWER
and GAS have genuinely different real sources with different key
requirements; a tenant might reasonably want ENTSO-E for POWER while GAS
stays synthetic (no EIA key registered yet). One switch for both would
force an all-or-nothing choice for no benefit.

**Discover "which tenants need a price" from `Risk`'s open positions
instead of `MarketData`'s own existing curve points.** Rejected — would
require either a new cross-module `Risk.Contracts` interface built for this
one narrow purpose (speculative generality for a demo project) or having
`CurveImportRunner` live inside `Risk` instead of `MarketData` (wrong
module — `PriceCurvePoint` is `MarketData`'s aggregate). Scoping to
tenants with an existing curve point keeps the module boundary clean at the
cost of not auto-onboarding brand-new tenants.

**Fetch the external price once per tenant instead of once per commodity.**
Rejected — the market price doesn't vary by tenant; fetching it once and
reusing it for every tenant's write is both simpler and far friendlier to
ENTSO-E/EIA's real rate limits than N calls for N tenants.

**Testcontainers or a fake HTTP server to test `EntsoECurveSource`/
`EiaCurveSource` end-to-end.** Rejected for now — the averaging/parsing
logic (`AveragePrice`) is exposed as an internal static method and unit
tested directly against canned XML/JSON, which covers the actual risk
(wrong math, wrong field parsed) without the complexity of standing up a
fake server for a demo project's opt-in, best-effort external sources.

## Revisit criteria

- **If a genuinely free European gas spot/forward source is found later**:
  replace `EiaCurveSource` as GAS's real option, resolving the US/Europe
  geographic mismatch this ADR accepts for now.
- **If curve import ever needs to auto-onboard brand-new tenants** (not
  just refresh existing ones): revisit the "only refreshes what already
  exists" scoping — likely needs the `Risk.Contracts` interface rejected
  above, once there's a real second consumer to justify it.
- **If ENTSO-E's bidding-zone-per-trader mapping becomes real product
  scope** (not just a hardcoded DE-LU zone): extend `EntsoECurveSource` to
  resolve a zone from the trade/position instead of one constant.
