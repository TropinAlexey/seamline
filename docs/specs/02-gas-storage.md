# Spec 02 — Gas storage: inventory and WACOG

Status: **specified** ([ADR-0036](../adr/0036-gas-storage-inventory.md)).
Depends on [spec 00](00-commodity-master-data.md).

## Problem

Seamline knows contractual exposure: `Position` is the net volume bought
minus sold per (commodity, month), and it closes on delivery. It does not
know physical stock. A gas trader who books storage capacity injects gas
in summer and withdraws it in winter. The stock in the ground has its own
cost basis, its own limits (capacity, daily injection and withdrawal rates)
and its own unrealized P&L against the curve. That is inventory, the third
thing (after logistics and quality) a CTRM has and an ETRM usually doesn't.

## Actors

| Role | Does |
|---|---|
| BO (operations) | Registers a storage contract, records injections and withdrawals |
| MO | Sees inventory valuation in the EOD run |
| FO, MO, BO | Read stock and WACOG |

## Data (Inventory module)

`inventory.storage_contract` (tenant-owned):

| Field | Type | Example |
|---|---|---|
| `facility` | text | "Demo Storage North" |
| `commodity_code` | text, a `Flow` commodity in MWh | GAS |
| `capacity` | numeric(18,3) MWh | 100,000.000 |
| `max_injection_per_day` | numeric(18,3) MWh | 2,000.000 |
| `max_withdrawal_per_day` | numeric(18,3) MWh | 3,000.000 |
| `valid_from`, `valid_to` | date | 2027-04-01 … 2028-03-31 |

`inventory.inventory_movement`, append-only (`SELECT, INSERT` only,
ADR-0006):

| Field | Type |
|---|---|
| `storage_contract_id`, `gas_day` | uuid, date |
| `kind` | `Injection` \| `Withdrawal` |
| `quantity` | numeric(18,3) MWh, > 0 |
| `unit_cost` | numeric(18,4) EUR/MWh. Injections: the price paid for the gas. Withdrawals: the WACOG at that moment, recorded by the system |
| `trade_id` | optional, the delivered trade the gas came from or went to |
| `actor`, `reason`, `recorded_at` | as everywhere |

`inventory.inventory_balance`, current state, one row per contract:
`quantity`, `wacog` (numeric(18,4)), `version`. It is derived from the
movements in the same transaction, the same split as `trade` and
`trade_history`.

## Rules

1. Gas day within the contract's validity.
2. The day's total injection is at most `max_injection_per_day`, and the
   day's total withdrawal at most `max_withdrawal_per_day`.
3. After an injection, stock is at most `capacity`. A withdrawal is at most
   the current stock: inventory never goes negative.
4. **WACOG:**
   - an injection blends: `wacog' = (q × wacog + q_in × cost_in) / (q + q_in)`;
   - a withdrawal leaves the WACOG unchanged;
   - an empty storage resets the WACOG to 0.

   This is exactly the rule `Position.Apply` already uses for a long
   position (ADR-0014). The calculation moves into SharedKernel so both
   use one implementation.
5. Rounding (ADR-0007):
   - `wacog` is rounded to 4 dp, `ToEven`, once per movement, when it is
     stored;
   - the withdrawal cost `quantity × wacog` is rounded to 2 dp when shown.
6. **Valuation.** The EOD run (Risk) values stock against the GAS curve
   point for the valuation month:
   `unrealized = MtmCalculator.Calculate(curvePrice, wacog, quantity)`. It
   is stored in an append-only `risk.inventory_valuation` with the EOD
   `run_id` (ADR-0031). If no curve point exists, the stock is skipped and
   logged, the same as for positions (ADR-0014).

## Worked example

Contract: 100,000 MWh capacity, 2,000 MWh/day injection, 3,000 MWh/day
withdrawal.

| Step | Movement | Stock | WACOG |
|---|---|---|---|
| 1 | Inject 40,000 MWh @ 28.0000 (over 20 gas days at 2,000/day) | 40,000 | 28.0000 |
| 2 | Inject 20,000 MWh @ 31.0000 (10 days) | 60,000 | (40,000 × 28 + 20,000 × 31) / 60,000 = **29.0000** |
| 3 | Withdraw 15,000 MWh (5 days at 3,000/day) | 45,000 | 29.0000 (unchanged) |

- Cost of the gas withdrawn: 15,000 × 29.0000 = **435,000.00**.
- EOD with the GAS curve at 33.0000: unrealized = (33.0000 − 29.0000) ×
  45,000 = **180,000.00**.
- Injecting 2,500 MWh on one gas day returns 409 (above 2,000/day).
  Withdrawing 46,000 MWh returns 409 (above the stock).

## Inventory vs position (no double counting)

A position is a contract that has not yet delivered. Inventory is gas that
has. A buy trade for April adds to the April position. When it is
delivered, the position closes (ADR-0014). If BO then injects that gas,
quoting the trade id, it appears in inventory. The two are shown side by
side and never summed: Positions page "Live" tab, and a new "Storage" tab.

## Integration events (`Inventory.Contracts`)

`GasInjected` and `GasWithdrawn`, each with
`(StorageContractId, GasDay, Quantity, UnitCost, WacogAfter, TradeId?,
Actor, Reason)`. They go to Audit. Risk reads balances through
`IInventoryDirectory` (a synchronous query interface) during the EOD pass,
the same pattern as `ICurvePointDirectory`.

## API and UI

- `GET /storage-contracts`, `POST /storage-contracts` (BO).
- `POST /storage-contracts/{id}/injections` and `/withdrawals` (BO).
- `GET /storage-contracts/{id}/movements`.
- `GET /inventory/valuations/latest` (MO, BO).
- UI:
  - a **Storage** page: one card per contract with a fill gauge (stock vs
    capacity), WACOG and unrealized P&L, plus the movement ledger;
  - the BO forms enforce nothing the server doesn't.

## Tests

- **Unit:**
  - the WACOG rule, including the worked example, emptying to zero and
    refilling;
  - each rate, capacity and negative-stock limit at its edge;
  - the SharedKernel averaging function gives the same results as before
    for `Position` (regression).
- **Integration:**
  - movements produce the balance in the same transaction;
  - append-only grants on `inventory_movement`;
  - RLS;
  - roles;
  - EOD writes `inventory_valuation` with the run id;
  - no curve point means no valuation row.
- **E2E:** the worked example via the UI.

## Out of scope

Storage-capacity trading and REMIT reporting of storage contracts,
nominations to the storage operator, ratchets (rates that depend on the
fill level), gas-in-storage transfers between contracts, realized P&L
linking a withdrawal to a sale, cushion gas, intrinsic and extrinsic
storage valuation.

## Public sources

The capacity and injection/withdrawal-rate model is general industry
knowledge; public storage transparency data (GIE AGSI+) uses the same
quantities. Weighted average cost is a standard inventory costing method.
All numbers are Seamline's own demo values.
