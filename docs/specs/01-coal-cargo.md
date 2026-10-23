# Spec 01 — Coal cargo: logistics, actualization, quality, provisional and final invoices

Status: **specified** ([ADR-0034](../adr/0034-coal-cargo-logistics.md)).
Depends on [spec 00](00-commodity-master-data.md).

## Problem

A power or gas forward is delivered when its period passes; the delivered
quantity is the contract quantity. A seaborne coal cargo is not like that:

- it needs a vessel nominated inside an agreed loading window (laycan);
- the quantity actually loaded is known only from the bill of lading (B/L),
  and the contract allows it to differ within a tolerance;
- the price depends on measured quality, known only from the certificate of
  analysis, days after loading;
- so the seller invoices provisionally at loading and finally after the
  certificate.

None of this exists today. `Trade.Deliver()` is one manual transition and
`Invoice` is created final on `TradeDelivered`.

## Actors

| Role | Does |
|---|---|
| FO | Books the coal trade with its cargo terms; amends contract quantity if a loading would fall outside tolerance |
| MO | Credit approval as today |
| BO (operations) | Nominates the vessel, records loading (B/L) and the quality certificate; sees provisional and final invoices |

Seamline keeps its three roles. Operations work is BO's in this demo.

## Contract terms (on the trade)

Coal trades are FOB only (title and risk pass at loading; Incoterms 2020,
public). For `Cargo` commodities the trade carries `CargoTerms`:

| Field | Type | Example |
|---|---|---|
| `incoterm` | enum, `FOB` only | FOB |
| `tolerance_pct` | numeric(5,2), 0–20, seller's option | 10.00 |
| `laycan_from`, `laycan_to` | date, `from ≤ to`, inside `deliveryPeriod` | 2027-03-10 … 2027-03-20 |
| `quality_spec` | snapshot of the commodity's spec at activation | base NAR 6000, reject < 5700, S ≤ 1.00 %, ash ≤ 16.00 % |

`deliveryPeriod` stays `YYYY-MM` and means the shipment month: positions,
curves and stress keep working per month. The laycan dates live alongside
it, they do not replace it.

## Shipment lifecycle (Logistics module)

| From | Action (BO) | To | Rule |
|---|---|---|---|
| — | `TradeActivated` for a `Cargo` commodity | `Open` | One shipment per trade |
| `Open` | Nominate vessel (name, IMO number, ETA) | `Nominated` | ETA within laycan, IMO is 7 digits |
| `Nominated` | Record loading (B/L date, B/L quantity) | `Loaded` | B/L quantity within contract ± tolerance, else 409 |
| `Loaded` | Record certificate (NAR, sulphur, ash) | `QualityCertified` | Every parameter within rejection limits |
| `Loaded` | Record certificate outside limits | `QualityRejected` | Terminal. The claim is handled outside the system |

Every transition writes an append-only `logistics.shipment_history` row
with actor and reason (ADR-0006).

## Actualization

Loading is not an amendment. `Amend` stays what ADR-0011 made it, a
commercial correction of the contract. Actualization records what physically
happened within the contract:

1. Logistics publishes `CargoLoaded(TradeId, BlQuantity, BlDate, Actor)`.
2. Trading moves the trade `Active → Delivered`, records
   `ActualVolume = BlQuantity` next to the unchanged contract `Volume`, and
   publishes `TradeDelivered` with both quantities.
3. For cargo commodities, `POST /trades/{id}/deliver` returns 409: delivery
   comes from loading, not from a button.

A B/L quantity outside tolerance is a breach, not an actualization. The
endpoint rejects it with 409 and the message tells the user to amend the
contract first (FO), which goes through the normal amend path.

## Settlement

| Moment | Invoice | Amount |
|---|---|---|
| `TradeDelivered` (cargo) | `Provisional` | `round(BlQuantity × ContractPrice, 2)` |
| `CargoQualityCertified` | `Final`, references the provisional | `round(BlQuantity × AdjustedPrice, 2)`; balance = final − provisional |

`AdjustedPrice = round(ContractPrice × MeasuredNar / BaseNar, 4)`, a
pro-rata calorific value adjustment. It is rounded to the price scale
because it appears on the invoice as a unit price. The amount is then
rounded once to cents (ADR-0007).

Flow commodities keep one `Standard` invoice on `TradeDelivered`, exactly
as today.

## Worked example

Buy 75,000 t COAL, shipment month 2027-03, FOB, EUR 110.0000/t, tolerance
10.00 %, laycan 2027-03-10 … 2027-03-20, base NAR 6000 kcal/kg.

- **Tolerance band:** 75,000 × 0.90 = 67,500 t to 75,000 × 1.10 = 82,500 t.
- **Loading:** B/L 2027-03-14, 78,500.000 t. Within the band, so the
  shipment is `Loaded` and the trade is `Delivered` with
  `ActualVolume = 78,500`.
- **Provisional invoice:** 78,500 × 110.0000 = **8,635,000.00**.
- **Certificate:** NAR 5,920.0, sulphur 0.85 %, ash 14.20 %. All within
  limits, so the shipment is `QualityCertified`.
- **Adjusted price:** 110.0000 × 5,920.0 / 6,000.0 = 108.53333… →
  **108.5333**.
- **Final invoice:** 78,500 × 108.5333 = **8,519,864.05**.
- **Balance:** 8,519,864.05 − 8,635,000.00 = **−115,135.95**, owed back to
  the buyer.
- **Risk:** the position on COAL 2027-03 was opened at +75,000 t on
  activation. `TradeDelivered` closes the contract 75,000 t, so the position
  is flat. The extra 3,500 t is physical fulfilment, not open exposure.

A B/L of 83,000 t would be rejected (above 82,500). A certificate with
sulphur 1.20 % would put the shipment in `QualityRejected` and no final
invoice would be issued.

## Integration events (`Logistics.Contracts`)

All carry `TradeId, TenantId, Actor, Reason`; published through the outbox.

- `CargoNominated(VesselName, Imo, Eta)`
- `CargoLoaded(BlQuantity, BlDate)`
- `CargoQualityCertified(MeasuredNar, SulphurPct, AshPct, BaseNar)`
- `CargoQualityRejected(MeasuredNar, SulphurPct, AshPct)`

`TradeActivated` gains an optional `CargoTerms` and `TradeDelivered` gains
`ContractVolume` (additive changes). Consumers: Trading (`CargoLoaded`),
Settlement (`TradeDelivered`, `CargoQualityCertified`), Audit (all of them).

## Module ownership

| Module | Owns |
|---|---|
| Trading | Contract and cargo terms, the trade lifecycle, `ActualVolume` |
| Logistics (new) | Shipment, vessel nomination, B/L, certificate, shipment history |
| Settlement | Provisional and final invoices and the balance |
| Risk | Position close-out on the contract quantity |
| Reporting | Nothing new: coal is not a REMIT product and is skipped |

## API and UI

- `GET /shipments?state=` (BO, MO), `GET /shipments/{tradeId}`.
- `POST /shipments/{tradeId}/nominate`, `/load`, `/certify` (BO).
- `GET /invoices` gains `kind`, `quantity`, `unitPrice`, `balance`.
- UI:
  - a **Shipments** page for BO, a list by state;
  - trade detail for a coal trade shows a **Shipment** panel (states,
    vessel, B/L, certificate) and an **Invoices** panel (provisional,
    final, balance);
  - the new-trade form shows cargo terms when the commodity is `Cargo`.

## Tests

- **Unit:** tolerance band edges (67,500 and 82,500 accepted, 67,499.999
  and 82,500.001 rejected); laycan check; rejection limits per parameter;
  adjusted price and the worked example's amounts to the cent.
- **Integration:**
  - the full chain (nominate → load → trade `Delivered` with
    `ActualVolume` → provisional invoice → certify → final invoice and
    balance);
  - `deliver` on a cargo trade returns 409;
  - an out-of-tolerance B/L returns 409;
  - a quality rejection issues no final invoice;
  - roles: FO cannot load, BO cannot book;
  - RLS on the logistics tables;
  - append-only grants on `shipment_history`;
  - coal is not REMIT-reported.
- **E2E:** the worked example end to end in the UI.

## Out of scope

CIF/CFR and freight, demurrage and despatch, discharge-port
re-measurement, partial cargoes and part-loading, blending, quality
penalties beyond the pro-rata NAR adjustment, claims handling, laycan
expiry automation, multi-currency (spec 00).

## Public sources

Incoterms 2020 (ICC) for FOB. The shape of quantity tolerance, laycan,
NAR-based price adjustment and rejection limits follows publicly available
standard coal trading terms (globalCOAL SCoTA). The REMIT scope (electricity
and natural gas only) comes from Regulation (EU) No 1227/2011. All numbers
are Seamline's own demo values.
