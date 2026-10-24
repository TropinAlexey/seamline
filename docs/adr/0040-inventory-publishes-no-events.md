# ADR-0040: Inventory publishes no events, and keeps WACOG at full precision

## Status

Accepted. Implemented ([spec 02](../specs/02-gas-storage.md)).

## Context

[ADR-0036](0036-gas-storage-inventory.md) creates an Inventory module for
gas storage. While implementing it, two parts of the plan did not survive:

1. **Events.** Spec 02 has Inventory publish `GasInjected` and
   `GasWithdrawn` for Audit. This is the constraint
   [ADR-0037](0037-shipment-lives-in-trading.md) and
   [ADR-0038](0038-price-fixing-is-confirmed-in-trading.md) already met:
   the bus outbox is bound to `TradingDbContext`
   ([ADR-0004](0004-transactional-outbox.md)). An Inventory endpoint
   saving `InventoryDbContext` cannot put an event into that outbox in the
   same transaction as the movement.
2. **Rounding.** ADR-0036 rounds the WACOG to 4 dp once per movement, when
   it is stored. A unit test of spec 02's worked example, injected the way
   operations would (2,000 MWh a day for 30 days), ended at 29.0002, not
   29.0000. Each rounding to the price scale feeds into the next blend, so
   the error accumulates with the number of movements.

## Decision

- **Inventory publishes nothing.**
  - It stays a module of its own, as ADR-0036 decided: it records physical
    movements entered by operations, and it has no lifecycle that drives a
    trade, so ADR-0037's reason to move into Trading does not apply.
  - Its append-only movement ledger already records who moved what, when
    and why, and the balance each movement produced. It is the audit
    record of storage, the same way `trade_history` is the record of a
    trade (ADR-0006).
  - Risk reads stock through `IInventoryDirectory` (`Inventory.Contracts`),
    a synchronous query interface, as ADR-0036 already said for valuation.
- **WACOG is stored to 12 dp and shown to 4 dp.**
  - The balance keeps the weighted average at 12 decimal places
    (`numeric(28,12)`), rounded explicitly at that scale.
  - It is rounded to 4 dp wherever it leaves the aggregate:
    - the API;
    - the ledger's `wacog_after`;
    - a withdrawal's recorded unit cost;
    - the valuation snapshot.
  - Unrealized P&L is computed from the full-precision value and rounded
    once to cents (ADR-0007: explicit rounding at the point it happens,
    and once per result).
- **Movements on one contract are serialized.** The rate and capacity
  checks read the day's movements and the balance, then write both. Each
  movement locks the contract row (`SELECT … FOR UPDATE`) for its
  transaction, the same as Trading's cargo steps. An integration test sends
  two injections at the same moment; without the lock both passed the daily
  rate.

## Relationship to existing ADRs

| ADR | Effect |
|---|---|
| [0036](0036-gas-storage-inventory.md) | **Amended in part.** WACOG precision: stored at 12 dp instead of 4 dp; the 4 dp figure is for display. Why: per-movement rounding drifted from the worked example by 0.0002 after 30 movements, and the drift grows with use. The module, the ledger and balance split, valuation in Risk and the shared averaging rule all stand. (The events were only in spec 02, not in ADR-0036.) |
| [0004](0004-transactional-outbox.md) | Upheld. No event leaves outside a transaction, because Inventory sends none. |
| [0006](0006-audit-trail-instead-of-event-sourcing.md) | Upheld. The ledger is append-only (`SELECT, INSERT` only, tested) and is the audit record. |
| [0007](0007-decimal-rounding.md) | Upheld, applied more carefully: one stored scale (12 dp) and one display scale (4 dp), each rounded explicitly. |
| [0010](0010-audit-module-placement.md) | Upheld. Storage movements do not appear on the Audit page. Their record is the ledger shown on the Storage page. |

## Consequences

- The Audit page shows trade and cargo events but not storage movements.
  Someone auditing storage reads the ledger, which carries the same actor
  and reason fields.
- Displayed and stored WACOG can differ beyond the fourth decimal. This is
  intended. Recomputing a displayed figure by hand from rounded inputs can
  differ by a unit in the last place from a figure computed at full
  precision.

## Revisit criteria

- Another module needs to react to a storage movement (for example,
  Settlement invoicing storage fees). That needs an outbox per module,
  which is already ADR-0037's and ADR-0038's revisit trigger.
