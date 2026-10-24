# ADR-0031: Risk and audit read models for the web UI

## Status

Accepted

Amended in part by [ADR-0041](0041-eod-run-marker-per-tenant.md): the latest run is the tenant's newest run marker, not the newest row of each table.

## Context

The web UI (ADR-0030) shows positions with MtM, the latest EOD valuation
and stress run, the counterparty exposure behind a four-eyes decision, and
the audit trail. Exposing that data turned up four problems that would
have put wrong numbers on screen:

- **No EOD run identity.** `ValuationSnapshot.Create` and
  `StressScenarioResult.Create` each stamped `ValuedAt = UtcNow`, so the
  rows of one run differed by milliseconds. "The latest run" could not be
  selected, and a stress total could not be matched to its base valuation.
- **Leaking credit reservations.** Reservations were released only on
  reject, timeout or cancel. Delivery closed the position but kept the
  reservation. Amendment kept the old volume and price. A counterparty's
  exposure only ever grew.
- **Two exposure formulas waiting to happen.** The credit check computed
  `max(0, MtM)` per reservation against the live curve. A new endpoint that
  summed the stored `credit_reservation.amount`, a submit-time snapshot,
  would disagree with the decision it is meant to explain.
- **A thin audit trail.** Only activation, rejection and amendment were
  audited. Creation, submit, cancel and delivery left no row.

## Decision

- **`run_id` on `valuation_snapshot` and `stress_scenario_result`.**
  `EndOfDayValuationRunner` creates one `EodRun(RunId, ValuedAt)` per
  invocation and passes it to every row. The migration backfills legacy
  rows per (tenant, minute) across both tables. This is best effort and
  documented in the migration.
- **Reservations follow the trade.** Risk's `TradeDeliveredConsumer`
  releases the reservation. `TradeAmendedConsumer` moves it to the new
  volume and price.
- **One exposure implementation.** `CreditExposureCalculator` is used by
  both the credit check and `GET /counterparties/{id}/exposure`. The
  endpoint lives in Risk, which owns the reservations. It reads the credit
  limit through `ICounterpartyDirectory` (Reference Contracts). Risk owning
  a route under `/counterparties/…` is fine, because route prefixes are not
  module boundaries.
- **Server-side totals.** `/positions` becomes `{ items, totalMtm }`, with
  per-row MtM from the same `MtmCalculator` the EOD run uses. Flat positions
  are omitted. `/valuations/latest` and `/stress-results/latest` return one
  run each. The stress response carries the base total from the same run.
  `/positions` changes shape instead of getting a parallel
  `/positions/summary`: its only client is the UI, and two endpoints over
  one table drift apart.
- **Complete audit trail.** `TradeCreated`, `TradeSubmitted` and
  `TradeCancelled` integration events in Trading Contracts, published
  through the outbox like the others. Audit consumes them plus
  `TradeDelivered`. `GET /audit-events` is for MO and BO.
- **EOD on demand for the demo:** the Hangfire dashboard, mapped only in
  Development, with an allow-all authorization filter. The default filter
  rejects requests from the Docker host. The API gets no endpoint that
  reaches into a worker (ADR-0002).

## Consequences

- Stress and valuation figures on screen are always from one run and one
  curve. The legacy backfill can merge or split runs that straddled a
  minute boundary. Only pre-migration data is affected.
- Exposure on the approvals screen is the number the credit check used. It
  moves with the live curve, which is intended (replacement cost).
- Three more event types on the bus. Nothing but Audit consumes them today.
- In compose, Development mode exposes the worker dashboards on 8082/8083
  with no authentication. That is acceptable for a local demo, and the
  dashboard never exists outside Development.
