# ADR-0038: The final price is confirmed in Trading, not triggered by a MarketData event

## Status

Accepted. Implemented ([spec 03](../specs/03-formula-pricing.md)).

## Context

[ADR-0035](0035-formula-pricing.md) has MarketData publish
`IndexFixingPublished` for every fixing. Trading consumes it, detects that
the last pricing day has fixed, and writes the final price as actor
`system`.

Implementing it hit the constraint [ADR-0037](0037-shipment-lives-in-trading.md)
recorded for the shipment: **the bus outbox is bound to `TradingDbContext`**
([ADR-0004](0004-transactional-outbox.md)). A MarketData endpoint saves
`MarketDataDbContext`, and the scoped `IPublishEndpoint` writes outbox
rows into a context nobody saves. So `IndexFixingPublished` would either
be lost or published outside the fixing's transaction, which ADR-0004 rules
out.

ADR-0037 also left a lesson on record: check where events are published
from, not only who owns what. ADR-0035 was written before that lesson.

## Decision

- **No `IndexFixingPublished` event.** MarketData stores fixings and exposes
  them through a read-only query interface, `IIndexFixingDirectory` in
  `MarketData.Contracts`. This is the same pattern as
  `ICurvePointDirectory`. MarketData publishes nothing.
- **The pricing calculation stays in Risk, as ADR-0035 says.**
  `IFormulaPricingService` (`Risk.Contracts`) reads fixings and the forward
  and returns k of N, the expected price, fixed and unfixed quantity, and
  the final price once k = N. Trading calls it, the same way it calls
  `ICreditReservationService` at submit.
- **Back office confirms the final price**, with
  `POST /trades/{id}/fix-price`:
  - Trading asks Risk for the final price;
  - it refuses with 409 while k < N;
  - otherwise it writes the price as a new trade version ("Pricing window
    fixed", actor = the confirming user) and publishes `TradePriceFixed`
    through the outbox, in one transaction.

  This replaces ADR-0035's automatic, actor-`system` transition.
- **Trading decides which event completes the cargo's settlement.** Both
  halves of the final invoice live in Trading: the quality certificate and
  the fixed price.
  - If the certificate comes first, `CargoQualityCertified` says
    `PricePending`, and `TradePriceFixed` later carries the certificate
    data.
  - If the price is fixed first, `CargoQualityCertified` carries
    `FinalPrice`.

  Exactly one of the two events issues the final invoice. Settlement keeps
  no half-state.

  This holds only if the two steps cannot overlap. Each reads the half
  the other writes, so loading, the certificate and the price fixing each
  take a row lock on the trade (`SELECT … FOR UPDATE`) for the length of
  their transaction. Whichever step commits second sees the first. An
  integration test sends a certificate and a fixing at the same moment;
  without the lock it reproduced a cargo that never got a final invoice.
- **The provisional price travels on `TradeDelivered`.** Trading asks Risk
  for the expected price at loading and puts it in `TradeDelivered.Price`.
  Settlement does not call Risk, which spec 03 had proposed. Settlement
  stays a pure event sink with no synchronous dependency.

## Relationship to existing ADRs

| ADR | Effect |
|---|---|
| [0035](0035-formula-pricing.md) | **Amended in part.** The trigger for the final price changes, from an event on the last fixing (actor `system`) to a back-office confirmation (actor a person). Everything else stands: fixings are append-only, the expected price is computed in Risk only, formula trades are valued per trade, and index pricing is offered on coal only. Why: an automatic trigger needs an event from MarketData, and that event cannot be published transactionally (ADR-0004, ADR-0037). A person confirming a final price also matches practice: a price confirmation goes to the counterparty. |
| [0004](0004-transactional-outbox.md) | Upheld. Every event is still written in the transaction of the state change it announces. |
| [0037](0037-shipment-lives-in-trading.md) | Upheld and applied. The same constraint gives the same answer: the lifecycle step goes where the outbox is. |
| [0031](0031-risk-read-models-for-the-ui.md) | Upheld. One calculator, now with a second entry point for pricing, used by the credit check, EOD valuation, the pricing view and Trading. |
| [0006](0006-audit-trail-instead-of-event-sourcing.md) | Upheld. The fixed price is a new version with a named actor, which is better evidence than `system`. |

## Consequences

- No fixing event is ever lost, because there is none to lose. Trading
  reads fixings when it needs them.
- The price stays unfixed until someone confirms it, even after the window
  has fixed. The pricing view shows "final price" (what the fixings give)
  apart from "price fixed" (confirmed on the trade). A forgotten
  confirmation is visible there, and on the Positions page as an open
  formula trade with every day fixed.
- MarketData stays free of messaging. Its only dependency is still
  SharedKernel plus its own Contracts.

## Revisit criteria

- The system gets an outbox per module (also ADR-0037's revisit trigger).
  Then MarketData can publish fixings, and a consumer in Trading can fix
  the price automatically, keeping the confirmation as an optional
  four-eyes step.
- Price fixings become frequent enough that confirming each one is a
  burden. Then a scheduled Trading job can apply the same endpoint logic
  as actor `system`.
