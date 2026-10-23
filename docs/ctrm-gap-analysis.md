# CTRM gap analysis

Seamline describes itself as a mini-CTRM. This page checks that claim
against the code and records what is missing, so the wording stays honest.

## ETRM vs CTRM

An **ETRM** (energy trading and risk management) system captures energy
trades, values them against forward curves, controls credit and reports to
the regulator. Power and pipeline gas fit this model well: the commodity is
a flow, delivery is "the period passed", and the delivered quantity is the
contracted one, give or take metering.

A **CTRM** (commodity trading and risk management) system adds the physical
side of commodities that move as discrete lots: ships, trains, tanks and
warehouses. A trade becomes a cargo with a vessel and a loading window. The
quantity is known only after loading. The price depends on measured quality
and is often fixed later against an index. Stock sits in storage with its
own cost basis. These are the processes that set a CTRM apart, and they
are what this page measures Seamline against.

## Capability table

| Capability | Today | What a CTRM needs | Gap | Closed by |
|---|---|---|---|---|
| **Units and master data** | **Implemented.** `Commodity` in Reference with unit (MWh / t), delivery model (Flow / Cargo) and contract quality. Unknown codes are rejected on `POST /trades`. Curve import no longer hard-codes POWER/GAS. The UI labels volumes with units. One currency (EUR), stated. | A commodity record: unit of measure, delivery model, quality specification. | Closed. COAL itself is seeded with step 2. | [Spec 00](specs/00-commodity-master-data.md), [ADR-0033](adr/0033-commodity-master-data.md) |
| **Logistics** | `Trade.Deliver()` is one manual, all-or-nothing transition ([ADR-0011](adr/0011-trade-lifecycle-extension.md)). | Shipment lifecycle: nomination, vessel, laycan, loading, bill of lading, discharge. | No shipment, no vessel, no loading window. | [Spec 01](specs/01-coal-cargo.md), [ADR-0034](adr/0034-coal-cargo-logistics.md) |
| **Volume actualization** | Delivered quantity equals contract volume by construction. `Amend` changes volume and price as a commercial correction. | Contract quantity ± tolerance; the actual quantity comes from the B/L and drives invoicing and position close-out. | No actual quantity, no tolerance. | [Spec 01](specs/01-coal-cargo.md), [ADR-0034](adr/0034-coal-cargo-logistics.md) |
| **Quality** | None. | Contract quality specification, measured quality from a certificate, price adjustment and rejection limits. | Not modelled at all. | [Spec 01](specs/01-coal-cargo.md), [ADR-0034](adr/0034-coal-cargo-logistics.md) |
| **Settlement lifecycle** | `Invoice` is created final on `TradeDelivered` ([ADR-0012](adr/0012-marketdata-settlement-first-entities.md)): one amount, no lifecycle. | Provisional invoice on loading, final invoice once quantity, quality and price are known; the difference is settled. | No provisional/final split. | [Spec 01](specs/01-coal-cargo.md), [ADR-0034](adr/0034-coal-cargo-logistics.md) |
| **Pricing** | Fixed `decimal Price` per trade. Monthly curve points only; no daily prices are stored ([ADR-0012](adr/0012-marketdata-settlement-first-entities.md), [ADR-0018](adr/0018-curve-import.md)). | Formula prices: average of an index over a pricing window plus a premium; daily fixings; fixed vs unfixed exposure. | No index, no fixings, no formula. | [Spec 03](specs/03-formula-pricing.md), [ADR-0035](adr/0035-formula-pricing.md) |
| **Inventory** | None. `Position` is contractual exposure per (commodity, period), not stock ([ADR-0014](adr/0014-valuation-worker.md)). | Storage capacity, injections and withdrawals, stock valued at weighted average cost, unrealized P&L on stock. | No storage, no stock ledger. | [Spec 02](specs/02-gas-storage.md), [ADR-0036](adr/0036-gas-storage-inventory.md) |

What Seamline already does well and keeps: the trade lifecycle with a
four-eyes credit saga, append-only history, multi-tenancy with RLS, EOD
valuation and stress, simplified REMIT reporting, and the audit trail.
These are the ETRM core the CTRM processes build on.

## Order of delivery

Each step is implemented end-to-end (domain, migrations, endpoints, UI,
integration and E2E tests, README) before the next one starts
([ADR-0032](adr/0032-ctrm-scope.md)):

1. Commodity master data and units. Every later step depends on it.
   **Done.**
2. Coal cargo: logistics, actualization, quality, provisional and final
   invoices.
3. Formula pricing: index fixings, pricing windows, fixed vs unfixed
   exposure. It reuses the provisional/final invoice from step 2.
4. Gas storage: inventory and WACOG.

## Wording rules

- **Implemented** is claimed only for behaviour that exists in code and is
  covered by tests.
- **Specified** means an ADR and a spec exist, but no code. README lists
  specified processes as such and links to the spec.
- The README tagline and the CLAUDE.md scope boundaries change only when a
  step is implemented. Until then Seamline is "an ETRM core for power and
  gas, with CTRM processes specified".
- Every simplification stays stated, as before: no options, no VaR, single
  currency, simplified REMIT.
