# ADR-0033: Commodity master data and units of measure

## Status

Accepted. Implemented ([spec 00](../specs/00-commodity-master-data.md)). COAL is seeded by the cargo step ([ADR-0034](0034-coal-cargo-logistics.md)).

## Context

Commodities are free strings. Trade creation checks only that the code is
not blank. `CurveImportRunner` and `SyntheticCurveSource` hard-code
`POWER` and `GAS`. No volume carries a unit. Adding coal, a commodity
measured in tonnes and delivered by cargo with a quality specification,
cannot be done on strings. Every later CTRM step
([ADR-0032](0032-ctrm-scope.md)) needs to know, per commodity:
- its unit;
- whether it flows or ships;
- what quality it must meet.

## Decision

- **`Commodity` is master data in Reference**, next to `Counterparty`:
  - `code` and `name`;
  - `unit` (`MWh` | `Tonne`);
  - `delivery_model` (`Flow` | `Cargo`);
  - an optional `quality_spec` for `Cargo` commodities.

  It is exposed through `ICommodityDirectory` in `Reference.Contracts`, the
  same synchronous read pattern as `ICounterpartyDirectory`.
- **Tenant-owned**, with `tenant_id` and RLS like every other table. Each
  firm defines its own commodity set and contract quality specs. The
  migration seeds `POWER` and `GAS` for both demo tenants. `COAL` is seeded
  by the cargo step, so it is not bookable before it can be delivered.
- **The unit lives on the commodity, not on each trade.** A trade's volume
  is in its commodity's unit. `unit` and `delivery_model` are immutable
  once created, because changing them would reinterpret existing volumes.
- **One currency, EUR.** No currency field is added, because a field that
  can only hold one value is noise. Multi-currency is out of scope and
  stated in README.
- **Unknown commodity on `POST /trades` → 400**, the same handling as an
  unknown counterparty ([ADR-0030](0030-web-ui.md)).
- **Curve import iterates the commodities that already have curve points**
  instead of a hard-coded list. MarketData stays independent of Reference's
  master data, and a new commodity is imported once some tenant publishes
  a first point.

## Relationship to existing ADRs

| ADR | Effect |
|---|---|
| [0005](0005-multi-tenancy.md) | Upheld. A global, tenant-less commodity table was considered (commodities are market-wide). It was rejected because ADR-0005 requires `tenant_id` on every table and per-firm quality specs are realistic. |
| [0007](0007-decimal-rounding.md) | Upheld. Quality parameters are `decimal` with explicit precision (NAR numeric(7,1), percentages numeric(5,2)). |
| [0012](0012-marketdata-settlement-first-entities.md) | Upheld. Curve points stay keyed by commodity code. The code is now validated against master data. |
| [0018](0018-curve-import.md) | **Amended in part.** ADR-0018 has the runner fetch "exactly twice per run (POWER, GAS)". It now fetches once per commodity found in existing curve points, the same "refresh what exists" scoping ADR-0018 already applies to tenants. The per-commodity fetch-once rule and the "never throws" source contract are unchanged. Why: a hard-coded pair cannot include coal without editing the runner for every new commodity. |
| [0030](0030-web-ui.md) | Upheld. The new-trade form reads commodities from the API instead of a constant. |

## Consequences

- Typos in commodity codes are rejected at the boundary instead of creating
  an orphan position.
- Volumes become interpretable: the UI labels MWh or t.
- A new migration in Reference, and existing trades must resolve. The seed
  covers every code already used (`POWER`, `GAS`).
- Unit conversion (therms, MMBtu, GJ) is not offered. A commodity has one
  unit.

## Revisit criteria

- A second currency is needed (USD-quoted coal benchmarks). That needs FX
  rates and per-currency credit limits, and its own ADR.
