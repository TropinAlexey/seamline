# ADR-0027: Credit reservation concurrency control

## Status

Accepted

## Context

`CreditReservationService.TryReserveAsync` reads existing reservations for a
counterparty, sums exposure, checks the limit, and inserts a new reservation.
Without serialization, two concurrent trade submissions against the same
counterparty can both read the same set of reservations, both pass the limit
check, and both insert — breaching the credit limit with neither party
aware.

This is a classic TOCTOU (time-of-check-to-time-of-use) race and a
well-known problem in CTRM systems.

### Options considered

| Option | Pros | Cons |
|---|---|---|
| **Serializable isolation level** | Strongest guarantee | Punishes all transactions, not just same-counterparty; higher deadlock rate |
| **SELECT FOR UPDATE on reservation rows** | Row-level, no schema change | Locks N rows; empty-table case needs a sentinel |
| **Optimistic concurrency (version column on a ledger row)** | No blocking | Requires a new `credit_ledger` table; retry logic on conflict |
| **PostgreSQL advisory lock on counterparty ID** | One SQL call; no schema change; blocks only same-counterparty; auto-released on commit | Hash collisions theoretically possible (int64 space) |

## Decision

Use `pg_advisory_xact_lock(lockKey)` where `lockKey` is a `long` combining
`tenantId.GetHashCode()` (high 32 bits) and `counterpartyId.GetHashCode()`
(low 32 bits), inside an explicit transaction wrapping the read-check-insert
in `TryReserveAsync`.

The tenant dimension in the lock key ensures that tenants never serialize
against each other — consistent with the system's multi-tenancy invariant
(`tenant_id` on every table, EF global query filter, PostgreSQL RLS).

The advisory lock is acquired after `BeginTransactionAsync` and released
automatically when the transaction commits. Concurrent reservations against
**different** counterparties proceed without contention. Same-counterparty
reservations within the same tenant are serialized: the second caller blocks
until the first commits or rolls back, then reads the updated set of
reservations.

`Guid.GetHashCode()` is stable within a .NET version but not contractually
stable across major versions. This is fine for transient advisory locks
(no lock survives a process restart). Hash collisions cause unnecessary
serialization — a performance concern, not a correctness one.

## Consequences

- **Correctness**: the credit limit cannot be exceeded by concurrent
  submissions; the second transaction always sees the first's reservation.
- **Performance**: same-counterparty submissions are serialized, but this is
  the correct behavior — concurrent approvals against the same limit must be
  ordered. Different counterparties are unaffected.
- **Portability**: `pg_advisory_xact_lock` is PostgreSQL-specific. If the
  system moves to another RDBMS, replace with that engine's equivalent (SQL
  Server `sp_getapplock`, MySQL `GET_LOCK`).
- **Testing**: in-memory EF provider does not support advisory locks; unit
  tests continue using the in-memory provider (they are single-threaded and
  don't exercise the race). The concurrency invariant is covered by an
  integration test against a real PostgreSQL instance via Testcontainers.
- **Upgrade path**: if advisory lock contention shows up in traces (unlikely
  at this scale), switch to a dedicated `credit_ledger` row per counterparty
  with optimistic concurrency (`xmin` or EF `ConcurrencyToken`).
