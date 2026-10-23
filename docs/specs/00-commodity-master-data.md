# Spec 00 — Commodity master data and units of measure

Status: **implemented** ([ADR-0033](../adr/0033-commodity-master-data.md)), except the COAL seed, which ships with spec 01.
Prerequisite for specs 01–03.

## Problem

Commodities are free strings. `Trade.CreateDraft` only checks that the code
is not blank, so a trade on `"POWR"` is accepted. `"POWER"` and `"GAS"` are
hard-coded in `CurveImportRunner.Commodities` and
`SyntheticCurveSource.BasePrices`. No value carries a unit: MWh is implied
by a comment, and nothing stops a tonne from being added to a megawatt-hour.
A cargo commodity needs a unit, a delivery model and a quality
specification before any CTRM process can be built on it.

## Actors

| Role | Can |
|---|---|
| MO (middle office) | Create a commodity, change its quality specification |
| FO, MO, BO | Read commodities |

## Data

`reference.commodity`, tenant-owned (tenant_id + RLS, ADR-0005), one row
per (tenant, code):

| Field | Type | Notes |
|---|---|---|
| `code` | text, unique per tenant | `POWER`, `GAS`, `COAL` |
| `name` | text | Display name |
| `unit` | enum `MWh` \| `Tonne` | Unit of every volume on this commodity |
| `delivery_model` | enum `Flow` \| `Cargo` | `Flow`: delivered over the period, quantity = contract. `Cargo`: discrete shipment, actual quantity from the B/L (spec 01) |
| `quality_spec` | nullable, owned value | Cargo commodities only, see below |

`quality_spec` for COAL (values shaped on public standard coal contract
terms; Seamline's own demo numbers):

| Field | Precision | Demo value |
|---|---|---|
| `base_nar_kcal_kg` | numeric(7,1) | 6000.0 |
| `reject_below_nar_kcal_kg` | numeric(7,1) | 5700.0 |
| `max_sulphur_pct` | numeric(5,2) | 1.00 |
| `max_ash_pct` | numeric(5,2) | 16.00 |

Seeded by migration for both demo tenants: `POWER` (MWh, Flow) and `GAS`
(MWh, Flow). `COAL` (Tonne, Cargo, spec above) is seeded by the cargo step
(spec 01): until a cargo can be delivered, a bookable COAL would be a trade
the system cannot finish. MO can still create it through the API.

**Currency.** One currency for the whole system, EUR, as today. Real coal
benchmarks are quoted in USD; multi-currency (FX rates, per-currency credit
limits) is out of scope and stated in README.

## Rules

1. A trade's `commodityCode` must exist for the tenant, otherwise
   `POST /trades` returns 400 — the same handling as an unknown
   counterparty (ADR-0030).
2. A commodity's `unit` and `delivery_model` are immutable once created.
   Changing them would reinterpret every existing trade's volume.
3. Only `quality_spec` is updatable, and only for `Cargo` commodities. A
   change applies to trades activated after it. Trades activated earlier
   carry a snapshot (spec 01).
4. `CurveImportRunner` imports every commodity that already has a curve
   point for some tenant, instead of a hard-coded list. This is the same
   "refresh what exists" scoping ADR-0018 applies to tenants.
   `SyntheticCurveSource` keeps its per-code base prices and adds COAL
   (EUR 110/t).

## API and contracts

- `GET /commodities` (any role) → `CommodityRef[]`.
- `POST /commodities` (MO), `PUT /commodities/{code}/quality-spec` (MO).
- `Reference.Contracts`: `CommodityRef(Code, Name, Unit, DeliveryModel,
  QualitySpecRef?)` and `ICommodityDirectory.FindAsync(code)`, the same
  synchronous read pattern as `ICounterpartyDirectory`.

## UI

- The new-trade form takes its commodity list from `GET /commodities`.
  Volume labels show the unit (MWh, t).
- A read-only **Commodities** page for every role: unit, delivery model,
  contract quality. MO maintains commodities through the API.
- Trade blotter and detail show volumes with their unit.

## Tests

Integration (`CommodityTests`, 11 tests). Reference has no unit-test
project, and the factory rules are cheap to exercise through the API.
- every tenant starts with POWER and GAS in MWh;
- MO creates a cargo commodity with a quality spec;
- FO gets 403;
- a duplicate code gets 409;
- a spec must match the delivery model (400 both ways);
- a rejection floor above the base gets 400;
- a spec change works on cargo and gets 409 on flow, 404 for an unknown
  code;
- an unknown commodity on a trade gets 400;
- tenants are isolated by the query filter and by RLS.

Arch: the existing boundary tests cover Trading reaching commodities only
through `Reference.Contracts`. E2E: the Commodities page labels units, and
the trade form picks a commodity from master data.

## Out of scope

Multi-currency, unit conversion (therms, MMBtu, GJ), commodity
hierarchies, grade/brand catalogues.

## Public sources

Units and the cargo/flow distinction are general industry knowledge. The
coal quality parameters (net as received calorific value, sulphur, ash)
follow publicly available standard coal trading contract terms (globalCOAL
SCoTA). The demo values are Seamline's own.
