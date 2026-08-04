# ADR-0011: Trade Lifecycle Extension — Cancelled, Amended, Delivered

**Status:** Accepted
**Date:** 2026-07

## Context

The lifecycle comment in `Trade.cs` had named `Cancelled`, `Amended`,
`Delivered`, `Settled` as deliberately out of scope since the first vertical
slice. Filling them in means answering three questions the earlier ADRs
didn't: which states can cancel, what "amend" changes, and what delivery
means when `Settlement` is still an empty module.

## Decision

**1. `Cancel`: allowed from `Draft`, `Submitted`, `CreditPending` — not
`Active`.**

None of these three states has ever created a position — `Position` is only
touched by `TradeActivatedConsumer`, and none of them have reached `Active`.
So `Cancel` never needs a cross-module event for `Risk`; it's purely a
`Trading`-local concern. Cancelling a trade that's already `Active` is a
different operation (`Amend` down to zero is closer, or a future
Settlement-driven reversal) and is deliberately not folded into `Cancel`.

**2. Cancelling out of `CreditPending` goes through the saga, not a direct
mutation.**

`CreditPending` means `TradeApprovalStateMachine` (ADR-0008) is holding an
approval-timeout schedule and, if the breach path reserved credit, a
provisional `CreditReservation`. A direct `trade.Cancel()` in the endpoint
would leave the saga running and the reservation held — the timeout would
still fire, or a stray `/approve` could still land, on a trade that's
already `Cancelled`.

So `/cancel` on a `CreditPending` trade publishes `TradeCancelRequested`
(correlated by `TradeId`, same as `TradeApprovalGranted`/`Denied`) instead
of touching the `Trade` directly. The saga unschedules the timeout,
publishes `TradeApprovalCancelled`, and finalizes — mirroring the
approve/reject paths exactly. `TradeApprovalCancelledConsumer` is where the
`Trade` actually gets cancelled and the reservation released, the same way
`TradeApprovalCompletedConsumer` is where `Activate`/`Reject` actually
happen. `Draft` and `Submitted` never started a saga, so those cancel
inline in the endpoint — no round trip through the bus for a case that has
nothing to coordinate.

**3. `Amend`: same `Trade.Id`, new version, `Volume`/`Price` only, `State`
stays `Active`.**

Amending is a correction to an already-confirmed trade, not a new trade —
so it reuses `TradeHistory`'s existing versioning rather than creating a
second aggregate. `CommodityCode`/`DeliveryPeriod`/`Direction`/
`CounterpartyId` are not amendable: changing any of those isn't a
correction, it's cancelling one trade and booking another.

**Amend does not re-run the credit check.** Re-validating exposure on every
correction would resurrect the full breach saga (ADR-0008) for what's meant
to be a lightweight fix — and a genuine increase in exposure from an
amendment is a real gap, not a false economy; it's a stated simplification
here, not an oversight, per CLAUDE.md's "simplifications are deliberate and
stated."

`TradeAmended` carries both `OldVolume` and the new `Volume` (not just the
delta) so `Risk`'s `TradeAmendedConsumer` can apply
`(NewVolume - OldVolume) × sign` to the existing position — the same
`Position.Apply` used by `TradeActivatedConsumer` — instead of recomputing
the position from every trade.

**4. `Deliver`: `Active → Delivered`, no cross-module event.**

`Settlement`, `MarketData`'s valuation, and any EOD sweep are still empty
modules (Phase 2 in the TODO tracker) — nothing exists yet that would react
to a delivery. Publishing an event today would mean inventing a consumer
whose only job is not existing yet. `Deliver` stays a `Trading`-local
transition until a real consumer shows up; adding the event then is
additive, not a redesign.

**5. `Settled` is out of scope entirely**, not merely deferred like the
other three. It's the result of `Settlement` module's own work (invoice
paid) — `Trading` cannot decide when a trade is settled without that module
existing. Modeling a `Settled` state on the `Trade` aggregate now would mean
guessing at a lifecycle event owned by a module that has zero entities
today.

## Consequences

### Positive

- `Cancel`'s two paths (inline vs. saga-routed) reuse exactly the pattern
  already established for approve/reject — no new coordination primitive.
- `Amend`'s delta-based position update means `Risk` never needs to know
  about `Trade` beyond the fields it already gets from `TradeActivated`.
- Nothing in `Settlement`/`MarketData` needs to exist for `Deliver` to be
  meaningful and testable today.

### Negative

- **Amend doesn't protect the credit limit.** A trade amended to a much
  larger volume can push exposure past the counterparty's limit with no
  saga, no approval, and no rejection path — accepted here as the
  documented ceiling on this feature, not fixed silently.
- **No audit coverage for `Cancel`.** Per ADR-0010, `Audit` only consumes
  events that already cross a module boundary; `Cancel` never publishes one
  (see Decision 1), so cancellations don't appear in `audit.audit_event`
  today. `trade_history` still has the full record — this is a gap in the
  cross-module log specifically, not in traceability overall.
- **`Deliver` has no automatic trigger.** Nothing checks `DeliveryPeriod`
  against the current date; it's a manual endpoint call until Phase 2's EOD
  sweep exists.

## Alternatives considered

**Full saga for every lifecycle transition (Cancel, Amend, Deliver).**
Rejected. Cancel already routes through the existing approval saga when the
trade is in `CreditPending`; Amend is a same-aggregate mutation with no
external coordination; Deliver is a terminal state with no compensating
action. Adding saga orchestration to transitions that don't need external
approval adds complexity without a corresponding safety gain.

**Separate `TradeVersion` entity instead of in-place Amend.** Rejected.
Amend changes volume/price on the same `Trade.Id` — creating a new version
entity would force every downstream consumer (`Risk`, `Settlement`) to
track which version is current, duplicating the problem `trade_history`
already solves for audit.

## Revisit criteria

- **If amendments turn out to meaningfully move exposure in practice**,
  `Amend` needs its own credit-check path — not necessarily the full
  breach saga, but at minimum a synchronous re-check with the option to
  reject the amendment outright.
- **If `Cancel` needs to be audited**, that's either a new
  `TradeCancelled` event crossing to `Audit` (breaking Decision 1's "no
  event needed" reasoning, since it'd exist for `Audit` alone) or an
  extension of `Audit.Contracts` to accept module-local facts directly —
  worth its own ADR either way, not a quiet exception to ADR-0010.
- **When `Settlement` gains its first entity**, revisit both `Deliver` (does
  it publish a `TradeDelivered` event Settlement or Valuation consumes?) and
  whether `Settled` belongs on `Trade` at all or purely in `Settlement`'s
  own schema with a reference back to the trade.
