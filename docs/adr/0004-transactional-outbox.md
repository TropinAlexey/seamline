# ADR-0004: Transactional Outbox for Published Events

**Status:** Accepted
**Date:** 2026-07

## Context

`Trading` publishes integration events (`TradeActivated`, `TradeRejected`,
`TradeAmended`, `TradeDelivered`, and the saga's own messages) as a direct
consequence of state changes it persists — a trade activates, a row is
written to `trade_history`, and something else across a module boundary
needs to know. Writing the entity change and publishing the event are two
different operations against two different systems (PostgreSQL, the message
bus); without a shared transaction between them, either can succeed while
the other fails — a trade activates but the event is never published, or an
event goes out for a database write that then rolls back. ADR-0008 already
referenced this ADR by number ("`messaging` is the single cross-cutting
schema in the system... that exception is deliberate and narrow: it is
transport, not domain, see ADR-0004") before it existed as a document —
this is that document.

## Decision

**MassTransit's EF Core transactional outbox**, not a hand-rolled outbox
table or an at-least-once/idempotent-consumer-only approach.
`AddEntityFrameworkOutbox<TradingDbContext>` plus `UseBusOutbox()`
(`TradingModuleExtensions.AddTradingMassTransitConfiguration`) makes every
`Publish` inside a request buffer into the same `TradingDbContext` instance
handling that request's other writes. The buffered messages and the
entity changes commit in the same database transaction — `SaveChangesAsync`
either persists both or neither. A separate delivery service (part of
MassTransit's outbox machinery) polls the outbox table and actually sends
to the transport after the transaction has committed, so a message is never
visible to a consumer for a write that didn't happen.

**Only `Trading` has an outbox.** `Risk`, `Settlement`, and `Audit` consume
events but never publish any themselves — an outbox exists to make
publishing safe, so a module with nothing to publish doesn't need one.
If a future module starts publishing, it gets its own
`AddEntityFrameworkOutbox<TItsDbContext>` the same way, not a shared one —
each module's outbox lives against its own `DbContext`, so the
transactional guarantee stays scoped to that module's own writes.

**The outbox/inbox tables live in a dedicated `messaging` schema**, not
`trading`, even though `Trading` is currently the only module using them.
`messaging` is the one deliberate exception to "no cross-cutting schemas"
(ADR-0001) — explicitly infrastructure/transport, not a domain concept any
module owns. Originally created inside `trading` and moved via
`MoveOutboxToMessagingSchema` (`RenameTable`, not drop/recreate, so
existing data and the `seamline_app` grants on the tables themselves survive
— PostgreSQL privileges attach to the table object, not its schema path;
only `GRANT USAGE ON SCHEMA messaging` had to be issued fresh, since the
schema itself was new).

**Global retry, not per-message.** `Program.cs` sets one
`UseMessageRetry(r => r.Intervals(100, 250, 500, 1000, 2000))` for the whole
bus rather than a bespoke policy per consumer. Combined with
`OnMissingInstance(m => m.Fault())` on the saga's approve/reject/cancel
events (ADR-0008), this turns the outbox's inherent polling delay — an
approve call can arrive at the saga before the outboxed
`TradeApprovalRequested` that creates the saga instance has been delivered
— into a few retried deliveries instead of either a lost event or a
hand-written wait-and-retry loop at each call site.

## Consequences

### Positive

- No trade-side or consumer-side code has to reason about "what if the
  write succeeded but the publish didn't" (or vice versa) — the outbox
  makes that failure mode structurally impossible rather than something
  every publisher has to remember to guard against.
- The saga's own messages (`TradeApprovalCompleted`, `TradeApprovalCancelled`)
  go through the same outbox as the trade lifecycle events — one mechanism,
  not a special case for saga output vs. regular domain events.
- `messaging` as a schema makes the transport/domain boundary visible in
  `psql \dn` output, not just in prose — a schema a human or a future module
  can see is infrastructure without reading any code.

### Negative

- Publishing is not instant — a message becomes visible to consumers only
  after the outbox delivery service's next poll, not synchronously with the
  `SaveChangesAsync` that buffered it. This is exactly the race the saga's
  `OnMissingInstance(Fault())` + global retry exists to absorb; any new
  publisher needs to keep this delay in mind, not assume same-millisecond
  delivery.
- The outbox tables are extra rows PostgreSQL has to manage (write, poll,
  eventually clean up) for every published message — real but negligible
  overhead at this project's scale.

## Alternatives considered

**No outbox — publish directly from the request handler.** Rejected. This
is exactly the dual-write problem described in Context: nothing ties the
database commit and the bus publish together, so a crash or a transient bus
failure between the two leaves either an unpublished state change or a
published event for a change that never happened.

**A hand-rolled outbox table + a custom background poller.** Rejected.
MassTransit's EF Core outbox does exactly this, already integrated with
`Publish`/`IPublishEndpoint` and the saga's own EF Core repository, tested
by MassTransit's own test suite rather than this project's. Writing an
equivalent by hand would be reproducing a solved problem for no benefit —
the kind of thing ADR-0001 already argues against in the microservices
context, and the same reasoning applies at this smaller scale.

**One outbox per module regardless of whether it publishes.** Rejected —
considered briefly for consistency ("every module looks the same"), but
`Risk`/`Settlement`/`Audit` have no `Publish` call anywhere in their code;
wiring an outbox for messages that never get sent would be dead
infrastructure, exactly the kind of speculative setup this project avoids
elsewhere.

## Revisit criteria

- **When a second module starts publishing events** (a real candidate:
  `Valuation.Worker` if it ever needs to announce a completed revaluation
  rather than just writing `valuation_snapshot` rows): give it its own
  outbox against its own `DbContext`, following this same pattern, not a
  shared one.
- **If outbox polling latency ever becomes visible as a real problem**
  (not just the saga race already handled): reconsider `UseBusOutbox()`'s
  default polling interval, or whether a given publish actually needs the
  transactional guarantee at all.
