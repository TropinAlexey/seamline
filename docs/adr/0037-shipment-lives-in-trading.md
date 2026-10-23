# ADR-0037: The shipment lives in Trading, not in a Logistics module

## Status

Accepted. Implemented ([spec 01](../specs/01-coal-cargo.md)).

## Context

[ADR-0034](0034-coal-cargo-logistics.md) put the coal shipment into a new
Logistics module. The argument was that it has its own actors, data and
failure modes. Implementing it showed two problems the ADR had not weighed:

1. **The outbox is bound to one DbContext.** Integration events are
   published through MassTransit's bus outbox
   ([ADR-0004](0004-transactional-outbox.md)). The bus outbox is
   registered once per bus, for `TradingDbContext`: the scoped
   `IPublishEndpoint` writes outbox rows into that context. A Logistics
   endpoint saving `LogisticsDbContext` would either lose its events or
   need one of these:
   - a second bus with its own outbox, which an in-memory transport cannot
     connect to the first one, so the tests would break;
   - a publish outside a transaction, which is exactly what ADR-0004 rules
     out.
2. **Loading *is* the trade's delivery.** Under FOB, title passes at
   loading, so recording the B/L and moving the trade `Active → Delivered`
   are one business fact. In two modules they would be two transactions
   joined by an event. There would be a window where the shipment says
   "loaded" and the trade still says "Active". And an out-of-tolerance
   B/L could be accepted by Logistics before Trading rejected it.

## Decision

- **`Shipment` and `ShipmentHistory` are aggregates in Trading** (tables
  `trading.shipment` and `trading.shipment_history`). This is the same
  placement rule ADR-0008 applies to the saga: a lifecycle that drives the
  trade's lifecycle belongs to the module that owns the trade. Trading
  already hosts REMIT reporting (ADR-0015) on the same grounds.
- **Recording the B/L is one transaction**: the shipment moves to
  `Loaded`, the trade moves to `Delivered` with its actual quantity, both
  history rows are written, and `TradeDelivered` goes into the outbox. If
  the tolerance check fails, nothing is written.
- **There is no separate `CargoLoaded` event.** `TradeDelivered`, which now
  carries `ContractVolume` and `CargoTerms`, announces the loading. The
  other shipment events (`CargoNominated`, `CargoQualityCertified`,
  `CargoQualityRejected`) live in `Trading.Contracts`.
- **"Awaiting nomination" is not a state with a row.** It is an Active
  cargo trade with no shipment yet. The shipment row is created at
  nomination. ADR-0034's `Open` state had no data of its own, so it is
  dropped.
- Operations endpoints are `/shipments/{tradeId}/…`, restricted to back
  office, the same as in ADR-0034.

## Relationship to existing ADRs

| ADR | Effect |
|---|---|
| [0034](0034-coal-cargo-logistics.md) | **Amended in part.** Module placement, the `Open` state and the `CargoLoaded` event are replaced as above. Everything else stands: actualization is not an amendment, the tolerance band, FOB, invoice kinds, close-out on the contract quantity, and no REMIT for coal. Why: ADR-0034's reasons for a separate module are real but secondary. The outbox constraint and the atomicity of loading-as-delivery decide it. |
| [0004](0004-transactional-outbox.md) | Upheld. Every event is still written in the same transaction as the state change that produced it. This ADR exists to keep that true. |
| [0008](0008-saga-placement-and-ownership.md) | Upheld and reused. "A lifecycle belongs to the module that owns the aggregate it drives" decides this placement too. |
| [0001](0001-modular-monolith.md) | Upheld. No module boundary is crossed or weakened. A module grows, which ADR-0001 allows; boundaries are about ownership, not size. |
| [0006](0006-audit-trail-instead-of-event-sourcing.md) | Upheld. `shipment_history` is append-only (`SELECT, INSERT` only, tested). |

## Consequences

- Loading and delivery cannot disagree: they commit together or not at
  all.
- Trading grows by two tables and one endpoint group. If shipping ever
  needs its own team or process (voyages, freight, demurrage), that is the
  trigger to extract it, together with a real multi-outbox design.
- **A lesson kept on record:** ADR-0034 was written before the
  implementation. The constraint that overturned part of it came from the
  infrastructure, not the domain. Specifications should check where events
  are published from, not only who owns what.

## Revisit criteria

- Logistics grows beyond one shipment per trade (part-cargoes, voyages,
  freight contracts), or gains its own team. Then extract it, with an
  outbox per module.
