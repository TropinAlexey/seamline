# Spec 03 — Formula pricing: index averages, fixings, fixed vs unfixed exposure

Status: **specified** ([ADR-0035](../adr/0035-formula-pricing.md)).
Depends on [spec 00](00-commodity-master-data.md) and
[spec 01](01-coal-cargo.md) (provisional and final invoices).

## Problem

Every Seamline trade has a fixed price. Physical coal (and much of physical
commodity trading) is priced by formula instead: "the average of an index
over the pricing window, plus a premium". The price is unknown at booking
and becomes known day by day as the index fixes. So the trader's price risk
shrinks as fixings come in, and the invoice can only be final after the
window closes. Handling that (fixings, expected price, fixed and unfixed
quantity) is a core CTRM capability that Seamline doesn't have.

## Scope of this step

Formula pricing is allowed for **COAL** (`Cargo` commodities) only.
Power and gas trades are REMIT-reportable, and an index-priced REMIT report
needs price-formula fields that the simplified reporting (ADR-0015) doesn't
model. Extending it to GAS is a later decision.

## Actors

| Role | Does |
|---|---|
| FO | Books a trade with `IndexAverage` pricing terms |
| MO | Publishes index fixings (as with curve points, MO-only); sees pricing exposure |
| BO | Sees provisional and final prices on invoices |

## Data

MarketData:

- `marketdata.price_index`: `code` (`COAL-IDX`), `commodity_code`
  (`COAL`), `name`. Tenant-owned.
- `marketdata.index_fixing`: `index_code`, `fixing_date`, `value`
  numeric(18,4), `published_at`, `published_by`. One row per (tenant,
  index, date), **append-only**. A published fixing is never overwritten;
  corrections are out of scope. `SyntheticCurveSource` gains a
  deterministic daily fixing series around the month's synthetic price,
  weekdays only.

Trading: the trade's `PricingTerms`, one of:

| Kind | Fields |
|---|---|
| `Fixed` | `price` numeric(18,4), exactly today's behaviour |
| `IndexAverage` | `index_code`, `window_from`, `window_to` (dates), `premium` numeric(18,4), may be negative |

For an `IndexAverage` trade, `Price` is empty until the window is fully
fixed. Then Trading sets it to the final formula price in a new history
version with reason "Pricing window fixed".

## Rules

1. **Pricing days** are the weekdays in `[window_from, window_to]`. Public
   holidays are ignored, a stated simplification. `N` = number of pricing
   days, known at booking.
2. **Expected price** after `k` fixings, with `F` the forward for the
   trade's commodity and delivery month:
   `expected = (Σ fixings + (N − k) × F) / N + premium`. It is computed on
   demand and never stored. Display rounds to 4 dp; calculations use the
   unrounded value and round once at the result (ADR-0007).
3. **Fixed and unfixed quantity:** `fixed = V × k / N`,
   `unfixed = V − fixed`, both rounded to 3 dp for display.
4. **Final price** when `k = N`: `average(fixings) + premium`, rounded to
   4 dp and written to the trade.
5. **MtM** of a formula trade (Buy; Sell flips the sign):
   `(F − expected) × V`, which simplifies to `(k/N × (F − avg_fixed) − premium) × V`.
   The unfixed part carries no outright price risk: it moves with `F` on
   both sides. Formula trades are valued trade by trade in the EOD run, not
   blended into the position's weighted average price.
6. **Credit exposure** (ADR-0031's single calculator): uses `expected` in
   place of the trade price. With no curve point there is no `F`, so it
   falls back to full notional, as today, at `avg_fixed + premium`. Before
   the first fixing there is no price at all, and the trade cannot be
   submitted without a curve point for its month (400).
7. **Stress** (ADR-0016): the shock moves `F`. For a formula trade only the
   fixed share responds, which falls out of rule 5 with no special case.
8. **Invoices with a cargo** (spec 01):
   - the provisional invoice at loading uses the expected price at that
     moment;
   - the final invoice needs both the quality certificate and `k = N`, and
     uses `final formula price × NAR adjustment`.

## Worked example

Buy 75,000 t COAL, shipment month 2027-03, `IndexAverage(COAL-IDX,
2027-03-01 … 2027-03-31, premium +1.5000)`.

- **Pricing days:** March 2027 starts on a Monday. That gives 20 weekdays in
  the first four weeks plus Mon 29, Tue 30 and Wed 31, so **N = 23**.
- **State after 10 fixings:** average 104.2000; forward COAL 2027-03
  F = 106.0000.
- **Expected price:** (10 × 104.2000 + 13 × 106.0000) / 23 + 1.5000 =
  2,420 / 23 + 1.5 = 105.2173913… + 1.5 = 106.7173913… → shown as
  **106.7174**.
- **Fixed quantity:** 75,000 × 10 / 23 = 32,608.695… → **32,608.696 t**.
  Unfixed: **42,391.304 t**.
- **MtM:** (106.0000 − 106.7173913…) × 75,000 = −53,804.347… →
  **−53,804.35**. Check: (10/23 × (106.0000 − 104.2000) − 1.5000) × 75,000
  gives the same.
- **Stress +10 % on F** (F = 116.6000): MtM changes by 10/23 × 10.6000 ×
  75,000 = **+345,652.17**. A fixed-price trade of the same size would move
  by 10.6000 × 75,000 = 795,000.00.
- **Window fully fixed** with average 105.0000: final price
  105.0000 + 1.5000 = **106.5000**. With the cargo of spec 01 (B/L
  78,500 t, NAR 5,920):
  - adjusted price: 106.5000 × 5,920 / 6,000 = 105.08 → **105.0800**;
  - final invoice: 78,500 × 105.0800 = **8,248,780.00**.

## Integration events

- `IndexFixingPublished(IndexCode, FixingDate, Value)` from MarketData,
  consumed by Trading to detect `k = N` and finalize the price.
- `TradePriceFixed(TradeId, FinalPrice, Actor = "system", Reason)` from
  Trading, consumed by Risk (EOD revaluation uses the fixed price),
  Settlement (final invoice) and Audit.

## Module ownership

| Module | Owns |
|---|---|
| MarketData | Price indices and fixings |
| Trading | Pricing terms, final price, the "fixed" transition |
| Risk | Expected price, fixed/unfixed exposure, MtM and stress of formula trades |
| Settlement | Provisional and final amounts using the expected/final price |

The expected-price calculation lives in one place (Risk) and is used by
valuation, the credit check and the exposure endpoint, the same rule as
ADR-0031's single exposure calculator. Settlement asks Risk for the expected
price through `Risk.Contracts` at provisional-invoice time.

## API and UI

- `GET /indices`, `GET /indices/{code}/fixings?from&to`,
  `POST /indices/{code}/fixings` (MO).
- `GET /trades/{id}/pricing` → `{ n, k, avgFixed, forward, expected,
  fixedQuantity, unfixedQuantity, finalPrice? }`.
- `GET /pricing-exposure` (MO) → unfixed quantity per index and month.
- UI:
  - new-trade form: a pricing selector (Fixed / Index average);
  - trade detail: a **Pricing** panel with a k-of-N progress bar, the
    expected price and fixed vs unfixed quantity;
  - a **Fixings** tab on the Market data page.

## Tests

- **Unit:**
  - weekday counting (the worked example's N = 23, plus a window across a
    weekend);
  - the expected price, fixed quantity, MtM and stress delta of the worked
    example to the last digit;
  - final price and invoice;
  - premium can be negative.
- **Integration:**
  - publishing the last fixing fixes the trade price (new history
    version, `TradePriceFixed` out);
  - a final invoice waits for both the certificate and `k = N`, in either
    order;
  - `IndexAverage` on GAS is rejected (400);
  - a fixing for a date that already has one is rejected (409);
  - FO cannot publish fixings.
- **E2E:** book an index trade, publish fixings, see the progress bar and
  the final price.

## Out of scope

Fixing corrections, holiday calendars, other averaging methods (monthly
average of weekly prices, lagged or trigger pricing, pricing on the B/L
date), currency conversion, index-priced GAS and its REMIT fields, basis
risk between the index and the delivery curve (`F` is assumed to be the
same market).

## Public sources

Average-of-index-plus-premium pricing and pricing windows are general
commodity trading practice and described in public exchange and price
reporting agency methodology overviews. Benchmark coal indices are
proprietary, so Seamline uses a synthetic `COAL-IDX` and its own demo
numbers.
