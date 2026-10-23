# ADR-0035: Formula pricing and daily index fixings

## Status

Accepted. Implemented ([spec 03](../specs/03-formula-pricing.md)).
**Amended in part by:** [ADR-0038](0038-price-fixing-is-confirmed-in-trading.md) (no `IndexFixingPublished` event; back office confirms the final price in Trading).

## Context

Every trade has a fixed `decimal Price`. Physical coal is usually priced as
"average of an index over a pricing window plus a premium". The price
becomes known only as the index fixes day by day. Price risk is held only
on the fixed share, and the final invoice waits for the window to close.
Seamline stores no daily prices at all: curve points are monthly and
overwritten in place
([ADR-0012](0012-marketdata-settlement-first-entities.md),
[ADR-0018](0018-curve-import.md)).

## Decision

- **Pricing terms on the trade:** `Fixed(price)` (today) or
  `IndexAverage(index, window_from, window_to, premium)`. Index pricing is
  limited to `Cargo` commodities (coal) in this step: index-priced power
  and gas would need REMIT price-formula fields that the simplified report
  does not carry.
- **Daily index fixings** live in MarketData:
  - an append-only `index_fixing` table, one row per (tenant, index, date),
    never overwritten;
  - MO-only publishing, like curve points;
  - the synthetic source produces a deterministic weekday series.
- **Expected price** = (Σ fixings + unfixed days × forward) / N + premium.
  It is computed by Risk, in one place, and used by valuation, the credit
  check (ADR-0031's single exposure calculator) and the trade's pricing
  view. It is never stored. It is rounded once at each result (ADR-0007).
- **Final price**: when the last pricing day fixes, Trading writes the
  formula price to the trade as a new history version
  ("Pricing window fixed", actor `system`) and publishes `TradePriceFixed`.
- **Valuation**: formula trades are valued trade by trade
  (`(F − expected) × V`) and kept out of the position's blended weighted
  average price. Their unfixed share carries no outright price risk, and
  blending it into a fixed average would misstate the position.
- **Invoices** reuse ADR-0034's lifecycle:
  - the provisional invoice uses the expected price at loading;
  - the final invoice needs both the quality certificate and a fully fixed
    window.

## Relationship to existing ADRs

| ADR | Effect |
|---|---|
| [0006](0006-audit-trail-instead-of-event-sourcing.md) | Upheld. Fixing the price is a new trade version with actor and reason, not an in-place edit. |
| [0007](0007-decimal-rounding.md) | Upheld. The named rounding points are the displayed expected price (4 dp), the final price (4 dp), quantities (3 dp) and money (2 dp). Intermediate values are unrounded. |
| [0011](0011-trade-lifecycle-extension.md) | **Amended in part.** `Price` may be empty for an index trade until fixed, and "Pricing window fixed" is a new `system` transition inside `Active`. Why: a placeholder price would be a fiction every consumer had to know to ignore. `Amend` of an index trade changes volume only; changing its pricing terms is a cancel and rebook, consistent with ADR-0011's rule that non-correction changes are a new trade. |
| [0012](0012-marketdata-settlement-first-entities.md), [0018](0018-curve-import.md) | **Amended in part.** Both rejected storing price history ("a curve point is a refreshed market input"). A fixing is different: it is a settlement input, and an invoice is computed from it, so it must be reproducible and must never change after use. Curve points stay upsert-in-place. Only fixings are append-only. |
| [0014](0014-valuation-worker.md) | **Amended in part.** The position's weighted average price excludes formula trades, which are valued per trade with their own snapshot rows tied to the EOD `run_id`. Why: an expected price that changes daily cannot be blended into a cost basis that ADR-0014 keeps fixed on partial reduction. |
| [0015](0015-reporting-worker.md) | Upheld. Index pricing is not offered on REMIT commodities, so the simplified report stays correct. |
| [0016](0016-stress-scenarios.md) | Upheld. The shock moves the forward. For formula trades only the fixed share responds, which follows from the MtM formula with no new scenario type. |
| [0031](0031-risk-read-models-for-the-ui.md) | Upheld, extended. One exposure calculator stays the single source; it takes the expected price for formula trades. |

## Consequences

- A trader sees price risk shrink as the window fixes, which a fixed-price
  system cannot show.
- New tables (`price_index`, `index_fixing`), new events
  (`IndexFixingPublished`, `TradePriceFixed`), a nullable price on `Trade`.
  Every reader of `Trade.Price` must handle "not fixed yet".
- **Simplifications, stated:**
  - no fixing corrections;
  - no holiday calendar (weekdays only);
  - one averaging method;
  - the index and the delivery forward are treated as the same market (no
    basis risk);
  - coal only.

## Revisit criteria

- Fixings get corrected in practice. That needs versioned fixings and
  re-invoicing, and its own ADR.
- Index-priced gas is required. That needs REMIT price-formula fields in
  the report (ADR-0015).
