# Spec 03 — Formula pricing: index averages, fixings, fixed vs unfixed exposure

Status: **implemented** ([ADR-0035](../adr/0035-formula-pricing.md), amended by
[ADR-0038](../adr/0038-price-fixing-is-confirmed-in-trading.md): back office
confirms the final price in Trading; there is no fixing event).
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
| MO | Publishes index fixings (as with curve points, MO-only); sees pricing exposure, as FO does |
| BO | Confirms the final price once every pricing day has fixed (ADR-0038); sees provisional and final prices on invoices |

## Data

MarketData:

- `marketdata.price_index`: `code` (`COAL-IDX`), `commodity_code`
  (`COAL`), `name`. Tenant-owned.
- `marketdata.index_fixing`: `index_code`, `fixing_date`, `value`
  numeric(18,4), `published_at`, `published_by`. One row per (tenant,
  index, date), **append-only**. A published fixing is never overwritten;
  corrections are out of scope. `SyntheticCurveSource` has a deterministic
  weekday fixing series within ±2 % of the month's synthetic price, and the
  daily curve-import job publishes today's fixing for every index from it;
  a day that already has a fixing (published by hand) is left alone.
  Middle office can publish any weekday by hand.

Trading: the trade's `PricingTerms`, one of:

| Kind | Fields |
|---|---|
| `Fixed` | `price` numeric(18,4), exactly today's behaviour |
| `IndexAverage` | `index_code`, `window_from`, `window_to` (dates), `premium` numeric(18,4), may be negative |

For an `IndexAverage` trade, `Price` is empty until the window is fully
fixed and back office confirms it. Then Trading sets it to the final formula
price in a new history version with reason "Pricing window fixed: average
… premium … = …" (ADR-0038). The window lies inside the delivery month, so
the month's forward prices its unfixed days.

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
   4 dp and written to the trade when back office confirms it (ADR-0038).
5. **MtM** of a formula trade (Buy; Sell flips the sign):
   `(F − expected) × V`, which simplifies to `(k/N × (F − avg_fixed) − premium) × V`.
   The unfixed part carries no outright price risk: it moves with `F` on
   both sides. Formula trades are valued trade by trade in the EOD run, not
   blended into the position's weighted average price. A trade loaded
   before its window has fixed still carries price risk on its unfixed
   days, so it stays in the valuation until it is both delivered and
   fixed. Like a position, it is skipped (and logged) in a run where its
   month has no curve point, since there is no `F` to mark it at.
6. **Credit exposure** (ADR-0031's single calculator): uses `expected` in
   place of the trade price. With no curve point there is no `F`, so it
   falls back to full notional, as today, at `avg_fixed + premium`. With
   neither a curve point nor a fixing there is no price at all, and submit
   returns 400.
7. **Stress** (ADR-0016): the shock moves `F`. For a formula trade only the
   fixed share responds, which falls out of rule 5 with no special case.
8. **Invoices with a cargo** (spec 01):
   - the provisional invoice at loading uses the expected price at that
     moment (or the final price, if already fixed);
   - the final invoice needs both the quality certificate and the fixed
     price, in either order, and uses `final formula price × NAR
     adjustment`.

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
  by 10.6000 × 75,000 = 795,000.00. The stored stress MtM is
  (116.6000 − 112.7086956…) × 75,000 → **291,847.83**. Because each MtM is
  rounded once on its own (ADR-0007), the two stored figures differ by
  345,652.18, one cent more than the exact delta.
- **Window fully fixed** with average 105.0000: final price
  105.0000 + 1.5000 = **106.5000**. With the cargo of spec 01 (B/L
  78,500 t, NAR 5,920):
  - adjusted price: 106.5000 × 5,920 / 6,000 = 105.08 → **105.0800**;
  - final invoice: 78,500 × 105.0800 = **8,248,780.00**;
  - loaded after 10 fixings (as above), the provisional invoice is at the
    expected price of that day: 78,500 × 106.7174 = **8,377,315.90**, so
    the balance is **−128,535.90**.

## Integration events

- MarketData publishes no event (ADR-0038). Trading and Risk read fixings
  through `IIndexFixingDirectory` (`MarketData.Contracts`).
- `TradeActivated` gains `Pricing` and a nullable `Price`; `TradeAmended`'s
  prices are nullable.
- `TradePriceFixed(TradeId, FinalPrice, Actor, Reason, MeasuredNar?,
  BaseNar?)` from Trading, consumed by Risk (valuation uses the fixed
  price), Settlement (final invoice, when the certificate data rides along)
  and Audit.
- `CargoQualityCertified` gains `FinalPrice` and `PricePending`. Trading
  decides, in one transaction, which of the two events completes the
  final invoice.

## Module ownership

| Module | Owns |
|---|---|
| MarketData | Price indices and fixings |
| Trading | Pricing terms, final price, the "fixed" transition (confirmed by back office) |
| Risk | Expected price, fixed/unfixed exposure, MtM and stress of formula trades |
| Settlement | Provisional and final amounts using the expected/final price |

The expected-price calculation lives in one place (Risk,
`IFormulaPricingService` in `Risk.Contracts`) and is used by valuation, the
credit check, the exposure endpoint and Trading, the same rule as
ADR-0031's single exposure calculator. Trading puts the expected price of
the loading day on `TradeDelivered`, so Settlement needs no call to Risk.

## API and UI

- `GET /indices`, `GET /indices/{code}/fixings?from&to`,
  `POST /indices/{code}/fixings` (MO).
- `GET /trades/{id}/pricing` → `{ pricingDays, fixedDays, averageFixing,
  forward, expectedPrice, fixedQuantity, unfixedQuantity, formulaFinalPrice,
  confirmedPrice }`. The two final prices are different facts: what the
  fixings give, and what back office has written to the trade.
- `POST /trades/{id}/fix-price` (BO): 409 while a day is unfixed.
- `GET /pricing-exposure` (FO, MO) → each open formula trade with its
  fixed and unfixed quantity, expected price and MtM. Listed per trade, not
  summed per index and month: each trade has its own window, and a sum
  would hide which fixings move which trade. A sum per index and month can
  be derived from the list; the reverse is not possible.
- UI:
  - new-trade form: a pricing selector (Fixed / Index average);
  - trade detail: a **Pricing** panel with a k-of-N progress bar, the
    expected price, fixed vs unfixed quantity and the "Fix price" button
    for back office;
  - an **Index fixings** tab on the Market data page;
  - an **Index-priced trades** table on the Positions page.

## Tests

- **Unit:**
  - weekday counting (the worked example's N = 23, plus a window across a
    weekend);
  - the expected price, fixed quantity, MtM and stress delta of the worked
    example to the last digit;
  - final price and invoice;
  - premium can be negative.
- **Integration:**
  - confirming the price after the last fixing writes a new history
    version and sends out `TradePriceFixed`; before it, 409;
  - a final invoice waits for both the certificate and `k = N`, in either
    order, and also when the two arrive at the same moment;
  - `IndexAverage` on GAS is rejected (400);
  - a fixing for a date that already has one is rejected (409);
  - FO cannot publish fixings.
- **E2E:** book an index trade, publish fixings, see the progress bar, and
  fix the final price as back office.

## Out of scope

Fixing corrections, holiday calendars, other averaging methods (monthly
average of weekly prices, lagged or trigger pricing, pricing on the B/L
date), currency conversion, index-priced GAS and its REMIT fields, basis
risk between the index and the delivery curve (`F` is assumed to be the
same market), a market clock: a fixing may be published for any weekday, a
future one included, because the demo books trades in future months.
Publication is middle office only, append-only and records who published.
A zero or negative formula price (a premium deeper than the index) is
refused at submit, loading and fixing.

## Public sources

Average-of-index-plus-premium pricing and pricing windows are general
commodity trading practice and described in public exchange and price
reporting agency methodology overviews. Benchmark coal indices are
proprietary, so Seamline uses a synthetic `COAL-IDX` and its own demo
numbers.
