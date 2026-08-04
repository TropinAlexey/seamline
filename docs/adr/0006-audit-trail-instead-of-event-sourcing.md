# ADR-0006: Versioned Append-Only History Instead of Event Sourcing

**Status:** Accepted
**Date:** 2026-07

## Context

Seamline captures wholesale commodity trades (power and gas forwards) for
multiple tenants. Trades are long-lived: a trade booked today may be amended
several times, will be revalued daily until delivery, must be reported to a
regulator, and will be settled months later.

Event Sourcing is the canonical pattern for this domain. Trading and banking
are the textbook examples in almost every talk and article on the subject, and
for good reasons: regulatory audit, temporal queries, and replay into new
projections are exactly what Event Sourcing is good at. Choosing anything else
therefore requires an explicit justification rather than silence.

The requirements that actually drive this decision are:

1. **Full history.** A trade is never edited in place. An amendment supersedes
   the previous version; both must remain retrievable.
2. **Point-in-time reconstruction.** REMIT reporting must report a trade *as it
   stood at the moment of reporting*, not as it stands now. A late amendment
   must not silently rewrite what was already submitted.
3. **Tamper-evident audit trail.** Who changed what, when, and why — including
   business intent, not just a row diff.
4. **A known and stable query set.** Current position by commodity and delivery
   period; current credit exposure by counterparty; a trade as of a given date;
   valuation history with the market data that produced it. These questions are
   fixed by the domain and by regulation, not discovered over time.
5. **EU jurisdiction.** Counterparty contact data falls under GDPR, including
   the right to erasure.

## Decision

Use **current-state tables plus append-only versioned history**, with domain
events published through a transactional outbox but *not* used as the source of
truth.

Concretely:

- `trading.trade` — current state, one row per trade.
- `trading.trade_history` — append-only. One row per version:
  `(trade_id, version, valid_from, <full field snapshot>,
  changed_by, change_reason)`. No `valid_to` column — see below.
- `audit.audit_event` — cross-module actor/action/timestamp/context record.
- Amendments create a new version; the previous version is never mutated.
  History rows store `valid_from` only — no `valid_to` column. The end of a
  version's validity is derived at query time as the next version's
  `valid_from` (or "still open" if there is no next version). An earlier
  draft of this decision described a version as being "closed by setting
  `valid_to`", which is wrong: closing a row that way requires an `UPDATE`,
  directly contradicting "never mutated" below. Deriving `valid_to` instead
  of storing it removes the contradiction and keeps the table genuinely
  insert-only.
- `UPDATE` and `DELETE` on history tables are revoked at the database role
  level, so immutability is enforced by the database rather than by convention —
  and because there is no `valid_to` column to update, that revocation is
  never in tension with normal operation; it has nothing legitimate to block.
- Derived data carries provenance: every `risk.valuation_snapshot` records the
  `curve_version` that produced it, making any past valuation reproducible.

Domain events (`TradeSubmitted`, `TradeConfirmed`, `TradeAmended`, ...) are
first-class and are published transactionally via the outbox (see ADR-0004).
The event stream exists and is modelled properly; it simply is not the
system of record.

## Consequences

### Positive

- Full history, point-in-time reconstruction, and a tamper-evident audit trail
  are all satisfied without an event store, projection layer, or upcasting
  infrastructure.
- Queries stay direct. Current position and exposure are ordinary reads against
  current-state tables, with no eventual consistency between write and read
  models to reason about.
- `change_reason` captures business intent, which a purely technical event log
  or a trigger-based audit does not.
- GDPR erasure remains tractable. Personal data lives in counterparty records
  that can be redacted; it is not scattered across an immutable log that must
  never be rewritten.
- Onboarding cost is low. A .NET developer can read the schema and understand
  the system in an hour.

### Negative

- **No replay into unforeseen projections.** If a future requirement needs a
  read model that cannot be derived from versioned state, it cannot be
  backfilled from history. This is the real cost, and it is accepted knowingly.
- **Immutability must be enforced, not assumed.** Without the revoked
  permissions above, an accidental `UPDATE` in a repository method would
  silently corrupt the audit trail. The database-level guard is therefore not
  optional.
- **History tables grow wide.** Each version snapshots the full row rather than
  a delta. Accepted: trade volumes here are far below the scale where this
  matters, and full snapshots make point-in-time reads trivial.

### Neutral

- Domain events are modelled anyway. Should Event Sourcing later be justified,
  the migration is to start persisting events as the write model — not to
  invent the events, which is the expensive half of that work.

## Alternatives considered

**1. Full Event Sourcing (Marten or EventStoreDB).**
Rejected. The benefits that justify its cost — replay into projections nobody
anticipated, and events as a product in their own right — do not apply here:
the query set is fixed by the domain and by regulation. The costs do apply, and
permanently: event schema versioning and upcasters that can never be deleted,
projection rebuild operations, an additional storage engine to run and
understand, and an unresolved tension between an immutable log and the GDPR
right to erasure.

**2. Bitemporal / system-versioned tables.**
Considered and partially adopted in spirit. PostgreSQL has no native
`SYSTEM VERSIONING`, so this would be hand-rolled — which is what the history
table above is. Full bitemporality (separating valid time from transaction
time) is deliberately not implemented: it doubles the conceptual load and the
domain here does not have retroactive corrections to a distinct business
timeline.

**3. Trigger-based audit tables only.**
Rejected. Triggers capture *rows that changed*, not *why they changed*. There is
no place for `change_reason`, no notion of a business-level trade version, and
the audit trail ends up describing the schema rather than the domain.

## Revisit criteria

Move to Event Sourcing if any of the following becomes true:

- The set of required read models becomes genuinely open-ended, and new
  analytical views must be built over historical data that versioned state
  cannot reconstruct.
- A regulator or auditor requires the *sequence* of intent — not just the
  sequence of states — as the authoritative record.
- Trade volume reaches a scale where full-row history snapshots become a
  storage or write-throughput problem.

Until then, the cost is not justified by the requirements.
