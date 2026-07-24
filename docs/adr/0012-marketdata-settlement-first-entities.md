# ADR-0012: First Entities for MarketData and Settlement

**Status:** Accepted
**Date:** 2026-07

## Context

`MarketData` and `Settlement` had existed since the initial scaffold as
empty projects — a `.csproj` and a `RootNamespace`, no entity, no
`DbContext`, no migration. The TODO tracker carried this as an open gap.
Filling it meant designing two module boundaries from nothing, unlike the
Trade lifecycle work (ADR-0011), which extended an already-designed
aggregate.

## Decision

**MarketData: `PriceCurvePoint`.**

One row per `(tenant, commodity, delivery period)` — a flat monthly price
point, no interpolation or shaping, per CLAUDE.md's scope boundaries.
Publishing the same `(commodity, period)` again updates the price in place
rather than erroring or growing a history table: a curve point is a market
data input that gets refreshed, not an audited business transaction like a
trade. No history is kept for it — a deliberate simplification, not an
oversight.

Exposed via `ICurvePointDirectory` (`MarketData.Contracts`), the same
synchronous in-process read pattern as `Reference`'s
`ICounterpartyDirectory`. `Risk`'s `GET /positions` now resolves each
position's current curve price through this interface and returns it as
`MarkPrice` (nullable — absent until a curve point is published for that
commodity/period).

**This is explicitly not mark-to-market.** ADR-0007's MtM formula is
`(forward_price - trade_price) * volume`, which needs a trade-weighted
price per position — `Position` only tracks `NetVolume`, not a cost basis.
Computing real unrealized P&L is `Valuation.Worker`'s job (Phase 2, still
unbuilt). `MarkPrice` on `PositionRef` is a stepping stone: it proves the
cross-module read wiring works, nothing more.

**Settlement: `Invoice`.**

Created directly in its final form — no `Draft`/`Issued`/`Paid` lifecycle.
`Trade.Deliver()` (ADR-0011) now publishes `TradeDelivered`
(`TradeId, TenantId, CounterpartyId, Volume, Price, Actor, Reason`), which
`Settlement`'s `TradeDeliveredConsumer` consumes to create one `Invoice` per
delivered trade. `Amount = Math.Round(Volume * Price, 2,
MidpointRounding.ToEven)` — rounded once, explicitly, at creation, per
ADR-0007.

Introducing invoice states (payment, netting) is itself an architectural
decision — CLAUDE.md requires a new ADR for one — deliberately deferred
until Settlement has more than a single consumer to design a lifecycle
around. Shipping a `Draft`/`Issued`/`Paid` enum today with no code path
that ever transitions it would be exactly the "no half-finished
implementations" case to avoid.

**Both get the same infrastructure baseline every module gets**: a
dedicated schema (`marketdata`, `settlement`), `seamline_app` grants scoped
to what the code actually does (`price_curve_point` gets `UPDATE` because
`UpdatePrice` exists; `invoice` doesn't, because nothing ever updates one),
and RLS (`ADR-0005` layer 2) baked into `InitialCreate` directly rather
than retrofitted — these are new tables, so there's no window where they
existed without the second enforcement layer.

**`Settlement` never publishes.** Same shape as `Audit` (ADR-0010): a pure
sink for `TradeDelivered`, no event of its own yet. `GET /invoices` exists
for visibility/testing, not because another module reads it.

## Consequences

### Positive

- Both modules now have a real presence in the system — a schema, a table,
  a migration, actual reads and writes — closing the gap the TODO tracker
  had carried since the initial scaffold.
- The MarketData→Risk wiring demonstrates the query-interface pattern a
  third time (after Reference→Risk's `ICounterpartyDirectory`), reinforcing
  it as the standard shape for synchronous cross-module reads rather than a
  one-off.
- Settlement's consumer is a template for whatever eventually reacts to
  `TradeDelivered` next (Valuation, a real payment workflow) — the wiring
  pattern (one integration event, one pure-sink consumer) already exists.

### Negative

- `MarkPrice` on `PositionRef` is easy to mistake for real MtM if read out
  of context — it is a raw curve lookup, not `(forward_price - trade_price)
  * volume`. Documented in the contract and here; still a real risk of
  confusion until `Valuation.Worker` exists and the distinction is obvious
  from the API surface itself.
- `Invoice` has no lifecycle. A trade delivered twice (which nothing
  currently prevents beyond `Trade`'s own `RequireState` guard restricting
  `Deliver` to `Active`) would violate the unique index on `trade_id` and
  throw a `DbUpdateException` rather than failing gracefully — acceptable
  for now since `Deliver` is a single manual endpoint call, not a path with
  retries.

## Revisit criteria

- **When `Valuation.Worker` is built** (Phase 2): decide whether `MarkPrice`
  on `PositionRef` is superseded by a proper MtM snapshot or kept alongside
  it as "current market price" distinct from "unrealized P&L."
- **When Settlement needs to represent payment or netting**: design the
  `Invoice` lifecycle as its own ADR, informed by whatever the second real
  consumer of `TradeDelivered` (or a new payment-initiated event) actually
  needs — not speculatively now.
