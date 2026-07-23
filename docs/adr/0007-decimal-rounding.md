# ADR-0007: `decimal` for Money and Volume, Rounding Always Explicit

**Status:** Accepted
**Date:** 2026-07

## Context

Seamline stores and computes trade prices, volumes, mark-to-market, and
invoice amounts. Every one of these is a number a counterparty, an auditor,
or a regulator can reconcile against an external record. A silent rounding
difference or a `double`-precision artifact turns into a real discrepancy
the moment two systems compare the same trade.

## Decision

- **All money and volume values are `decimal`.** Never `double` or `float`,
  anywhere in the domain model, DTOs, or database columns — including
  intermediate calculation values, not just stored fields.
- **PostgreSQL columns are `numeric` with an explicit precision and scale**
  matching the domain quantity (e.g., price per MWh, volume in MWh), not the
  provider default.
- **Rounding is a named, explicit operation at the point it happens**, never
  an implicit consequence of a type conversion or a column's scale. Every
  place a value is rounded states the rounding mode
  (`MidpointRounding.ToEven` unless the domain specifically requires
  otherwise) and the number of decimal places, in code, not left to the
  database or the ORM to decide silently.
- **Mark-to-market** — `(forward_price - trade_price) * volume` — is
  computed in `decimal` throughout; the result is rounded once, explicitly,
  when it is persisted to `risk.valuation_snapshot`, not at each
  intermediate step.

## Consequences

### Positive

- No floating-point representation error in any value that has to
  reconcile against a counterparty's or a regulator's numbers.
- A rounding difference, if one is ever reported, is traceable to one named
  line of code rather than an accumulation of implicit conversions.

### Negative

- `decimal` arithmetic is slower than `double`. Accepted: none of Seamline's
  computations are in a hot path where this matters at the project's scale
  (mark-to-market on a bounded book, not high-frequency trading).
- Every new numeric field requires a deliberate precision/scale decision
  rather than a database default. Accepted as the cost of the guarantee
  above.

## Alternatives considered

**`double` for market data / volume, `decimal` only for money.** Rejected.
Mark-to-market multiplies a price difference by a volume; if either operand
carries floating-point error, the product does too. Consistency across the
whole calculation is simpler to reason about — and to explain — than a rule
that depends on which field is being touched.

**Rely on column scale (e.g., `numeric(18,4)`) for implicit rounding.**
Rejected. Implicit rounding via storage truncation hides *where* a value was
rounded, which matters when a P&L number has to be explained after the fact.
An explicit rounding call in code is one grep away from the answer.
