# ADR-0034: Coal cargo — Logistics module, actualization, provisional and final invoices

## Status

Accepted. Specified, not yet implemented ([spec 01](../specs/01-coal-cargo.md)).

## Context

A seaborne coal cargo has a lifecycle the trade aggregate does not:
1. vessel nomination inside a loading window;
2. loading with a bill of lading whose quantity may differ from the
   contract within a tolerance;
3. a quality certificate days later that adjusts the price;
4. two invoices, provisional at loading and final after the certificate.

Today `Trade.Deliver()` is a manual, all-or-nothing transition
([ADR-0011](0011-trade-lifecycle-extension.md)), and `Invoice` is created
once, final, on `TradeDelivered`
([ADR-0012](0012-marketdata-settlement-first-entities.md)).

## Decision

- **A new Logistics module** (`Seamline.Modules.Logistics` + `.Contracts`)
  owns the shipment:
  - states: `Open → Nominated → Loaded → QualityCertified | QualityRejected`;
  - vessel, B/L and certificate data;
  - an append-only `shipment_history`.

  Not states on `Trade`: a shipment has its own actors (operations), data
  and failure modes. Folding it into `Trade` would make one aggregate own
  both the contract and the physical movement. A shipment is created when
  `TradeActivated` arrives for a `Cargo` commodity.
- **Actualization is a separate transition, not an amendment.**
  - `CargoLoaded` makes Trading move the trade `Active → Delivered`, with
    `ActualVolume` = B/L quantity next to the unchanged contract `Volume`.
  - A B/L outside the tolerance band is rejected (409). Changing the
    contract quantity is a commercial decision and goes through `Amend`.
  - For cargo commodities the manual `/deliver` endpoint returns 409.
- **Contract terms on the trade:** for `Cargo` commodities, `CargoTerms`
  holds FOB, tolerance %, laycan and a snapshot of the commodity's quality
  spec at activation. FOB only: title passes at loading, so loading is
  delivery, and freight stays out.
- **Invoice gets a lifecycle:** `Standard` (flow commodities, unchanged),
  `Provisional` (on `TradeDelivered` for cargo, B/L quantity × contract
  price) and `Final` (on `CargoQualityCertified`, B/L quantity × adjusted
  price, referencing the provisional, with the balance). The adjusted
  price is a pro-rata calorific value adjustment, rounded to 4 dp because
  it is an invoice unit price. The amount is rounded once to 2 dp.
- **Risk closes the contract quantity.** `TradeDelivered` carries
  `ContractVolume` and `Volume` (actual). Risk removes the contract
  quantity it added at activation. Settlement invoices the actual.
- **No saga.** Each shipment step is one module-local transition triggered
  by a person, followed by an event. There is no cross-module timeout or
  compensation to orchestrate (contrast
  [ADR-0008](0008-saga-placement-and-ownership.md)).

## Relationship to existing ADRs

| ADR | Effect |
|---|---|
| [0001](0001-modular-monolith.md) | Upheld. A new module with impl/Contracts and arch tests. Trading depends on `Logistics.Contracts` (core depending on a supporting module's contracts, as ADR-0008 allows). Logistics depends on `Trading.Contracts` for `TradeActivated`. Neither touches the other's impl. |
| [0006](0006-audit-trail-instead-of-event-sourcing.md) | Upheld. `shipment_history` is append-only with `SELECT, INSERT` grants only, like `trade_history`. |
| [0007](0007-decimal-rounding.md) | Upheld. Two named rounding points: the adjusted unit price (4 dp) and the invoice amount (2 dp). |
| [0008](0008-saga-placement-and-ownership.md) | Upheld. No saga is introduced, because the conditions that justified one there (timeout, compensation, cross-module approval) are absent. |
| [0010](0010-audit-module-placement.md) | Upheld. Cargo events carry `Actor`/`Reason` and Audit consumes them. |
| [0011](0011-trade-lifecycle-extension.md) | **Amended in part.** Decision 4 made `Deliver` a manual `Active → Delivered`. For cargo commodities delivery now comes from loading, and the manual endpoint refuses. Why: a manual delivery would invoice the contract quantity instead of the loaded one. Decision 3 (`Amend` changes only volume and price, as a commercial correction) is upheld, and is why actualization is not an amendment. |
| [0012](0012-marketdata-settlement-first-entities.md) | **Amended in part.** ADR-0012 created `Invoice` with no lifecycle and deferred one to "its own ADR when Settlement has a second real consumer". This is that ADR: `CargoQualityCertified` is the second consumer. The unique index on `trade_id` becomes unique on `(trade_id, kind)`. |
| [0014](0014-valuation-worker.md) | **Amended in part.** The position closed on `TradeDelivered.Volume`. It now closes on `ContractVolume`, because closing on the actual quantity would leave a phantom ±tolerance residue in a delivered position. For flow commodities the two are equal, so their behaviour is unchanged. |
| [0015](0015-reporting-worker.md) | Upheld, made explicit. Coal is not a REMIT product. Reporting skips `Cargo` commodities. |
| [0027](0027-credit-reservation-concurrency.md) | Upheld. The credit check uses contract quantity. Tolerance headroom is not reserved (stated simplification). |

## Consequences

- Seamline gains the three CTRM features it lacked: logistics, actualized
  quantity and quality-adjusted pricing. Each has a worked example in the
  spec and a test for it.
- A new schema (`logistics`) with RLS and append-only grants, a new arch
  test row, new endpoints and UI.
- Invoices now come in kinds. Every reader of `/invoices` must handle
  `kind`, including the UI and the E2E test.
- **Simplifications, stated:** FOB only; one cargo per trade; no
  demurrage, freight, discharge re-measurement or claims workflow; the
  credit check ignores tolerance headroom.

## Revisit criteria

- CIF or part-cargoes are needed. That brings freight, multiple shipments
  per trade and a different title-transfer point, which needs a new ADR.
- Tolerance headroom matters to credit. Then reserve at
  `Volume × (1 + tolerance)`.
