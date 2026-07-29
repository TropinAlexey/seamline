# ADR-0014: Valuation.Worker — Real Mark-to-Market

**Status:** Accepted
**Date:** 2026-07

## Context

`GET /positions` has carried a `MarkPrice` field since MarketData's first
entity (ADR-0012), computed as a raw curve lookup — explicitly *not*
ADR-0007's MtM formula, `(forward_price − trade_price) × volume`, because
`Position` only tracked `NetVolume`, with no cost basis to subtract from
the curve price. ADR-0012's revisit criteria named this directly: decide
`MarkPrice`'s fate "when `Valuation.Worker` is built." ADR-0001 and ADR-0002
already committed to `Valuation.Worker` as a separate process sharing the
API's database; this ADR is the domain design underneath that process —
what it computes, what `Position` needs to support it, and what stays a
known simplification.

## Decision

**Cost basis is a volume-weighted average, not FIFO lots.** `Position`
gains `WeightedAvgPrice`. Extending the position in the same direction
blends the incoming price in; reducing it without flipping sign leaves the
average untouched (selling part of a position doesn't change the cost
basis of what's left); crossing zero starts a fresh average at the
incoming trade's price. No per-trade lot tracking — `Position` stays one
row per `(tenant, commodity, delivery period)`, not a ledger of specific
trades. This is adequate for a net MtM view, not a full P&L-realization
system.

**`TradeAmended` gains `OldPrice`** (alongside the existing `OldVolume`/
`NewVolume`, and `Price` renamed to `NewPrice` for symmetry).
`TradeAmendedConsumer` unwinds the old contribution
(`Apply(-oldSignedVolume, OldPrice)`) and applies the new one
(`Apply(newSignedVolume, NewPrice)`) as two calls. **Known limitation**:
this is exact only when the amended trade is the position's sole
contributor — the weighted-average model has no way to know which slice of
a blended average came from which trade, so amending one trade in a
position built from several doesn't perfectly re-derive the average. True
in every scenario this project actually exercises (one trade per
commodity/period in the tests and the demo data); would need per-lot
tracking to fix in general, which is out of scope here.

**Position closes on `Deliver`.** Until now, `TradeDelivered` had no
consumer in `Risk` — delivered trades stayed in `NetVolume` forever. A new
`TradeDeliveredConsumer` in `Risk` (parallel to Settlement's own, an
independent second consumer of the same event) applies
`-signedVolume` at the trade's final price, closing out exactly what was
delivered. `TradeDelivered` gains `CommodityCode`/`DeliveryPeriod`/
`Direction` so `Risk` can locate the position — additive fields, no
breaking change to Settlement's existing consumer.

**Curve reproducibility via a timestamp, not curve history.**
`PriceCurvePoint` gains `PublishedAt`, set on `Create` and bumped on every
`UpdatePrice`. MarketData still doesn't keep curve history (ADR-0012 — a
curve point is a refreshed market input, not an audited transaction);
`PublishedAt` is the only trace of "when was this price live." Each
`valuation_snapshot` row copies the curve price and its `PublishedAt`
directly rather than referencing the (mutable) curve point by id, so a
snapshot stays reproducible after the live curve point is later
overwritten.

**`risk.valuation_snapshot` is append-only**, one row per `(tenant,
commodity, delivery period)` per EOD run — not an upsert of "current
value." Same shape as `trade_history` (ADR-0006): `seamline_app` gets
`SELECT, INSERT` only, RLS baked in at creation. Rounded once, explicitly,
at persistence (ADR-0007): `mtm_amount = Math.Round((curve_price -
weighted_avg_price) * net_volume, 2, MidpointRounding.ToEven)`.

**No tenant registry exists, so discovering "every tenant" needs the
owner connection.** Multi-tenancy here is shared-schema + `tenant_id`, not
a central `Tenant` table (ADR-0005) — nothing tracks which tenant IDs
exist. `EndOfDayValuationRunner` queries `risk.position` via the
`PostgresMigrator` (owner) connection with `IgnoreQueryFilters()`,
bypassing both RLS and the EF query filter, purely to enumerate which
tenants have open positions. Every per-tenant read/write after that
enumeration still goes through the restricted `seamline_app` connection
with RLS and the query filter both active, same as everywhere else.
`Reporting.Worker` will hit the identical problem — if a real registry
(plausibly `identity.user.tenant_id`, distinct) is ever built for that, this
discovery query should switch to it instead of inventing a second one.

**Positions with `NetVolume == 0` are skipped** — MtM on a flat position is
zero by construction, not worth a snapshot row. **Positions with no
published curve point are skipped and logged**, not defaulted to zero —
a missing curve point is a data gap, not a zero valuation.

## Consequences

### Positive

- `MarkPrice`'s open question from ADR-0012 is resolved: it stays as a
  cheap live curve lookup on `GET /positions` (nothing consumes it as MtM),
  while `valuation_snapshot` is the actual MtM record, computed once daily.
- Cost basis and closing-on-delivery are both real gaps this closes, not
  speculative work — `Position` now behaves the way its own `MarkPrice`
  field always implied it should.
- The weighted-average approach keeps `Position` a single-row aggregate,
  consistent with how the rest of this project treats derived data (no new
  per-trade ledger table, no FIFO queue).

### Negative

- Amend on a multi-trade position doesn't perfectly re-derive the average
  (see above) — a real, documented approximation, not a hidden one.
- `EndOfDayValuationRunner`'s tenant-discovery query bypasses both
  isolation layers ADR-0005 built (RLS and the EF filter) for one read.
  Narrow and deliberate, but worth remembering it's there if `Risk`'s
  schema or the owner-role trust boundary ever changes.
- `valuation_snapshot` grows without bound (one row per position per day,
  forever) — no retention/archival policy exists yet. Acceptable at this
  project's scale; would need one before any real usage.

## Alternatives considered

**Running total cost (`TotalCost += signedVolume * price`, `WeightedAvgPrice
= TotalCost / NetVolume`).** Tried first, rejected by its own unit test:
this makes every volume change — including a partial close — blend into
the average using the *closing* trade's price, so selling part of a
position at a very different price than the entry cost swings the
"average" of what's left in a way that doesn't represent a cost basis at
all. The conditional blend/hold/reset model above is more code but is
actually a weighted average of what's still held.

**Per-trade lot tracking (FIFO or specific-lot).** Rejected for now.
Correctly handles Amend on a multi-trade position and gives exact
P&L-on-close, but requires `Position` to become a ledger rather than one
aggregate row — a real redesign, not something this ADR's scope (closing
the `MarkPrice` gap) asked for. Revisit if the Amend approximation above
ever actually matters against real data.

## Revisit criteria

- **If Amend's approximation produces a visibly wrong cost basis against
  real multi-trade positions**: per-trade lot tracking, at that point, not
  speculatively now.
- **If a real tenant registry gets built** (e.g. derived from
  `identity.user`): switch `EndOfDayValuationRunner`'s discovery query to
  it instead of the owner-connection bypass.
- **If `valuation_snapshot` needs retention limits**: design that as its
  own small decision once there's an actual row-count/storage number
  motivating it.
