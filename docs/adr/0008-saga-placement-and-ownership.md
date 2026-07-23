# ADR-0008: Saga Placement and Ownership

**Status:** Accepted
**Date:** 2026-07
**Deciders:** Alexey Tropin

## Context

Confirming a trade requires a credit check against the counterparty's limit.
The process touches three modules: `Trading` owns the trade, `Risk` computes
credit exposure and records reservations, `Reference` holds the limit as master
data on the counterparty.

Two facts shape this decision, and they pull in opposite directions.

**First: there is no technical distribution here.** All three modules run in the
same process against the same PostgreSQL instance. A check-and-reserve against a
limit is `BEGIN ... COMMIT`. Introducing a distributed coordination protocol
over a non-distributed system would be ceremony — precisely the kind of
cargo-culting this project argues against (ADR-0001).

**Second: there is a real boundary, and it is not technical.** When a trade
would push exposure beyond the counterparty's limit, the decision belongs to a
human risk manager. That takes hours, not milliseconds. No transaction can be
held open across it, and no amount of shared-database convenience changes that.
Segregation of duties makes it worse in the useful sense: the approver must not
be the trader who booked it, so this is genuinely two actors and two points in
time.

So the question is not "saga or transaction" but "where is the boundary, and
where does the coordinator live when we need one".

## Decision

**1. Two paths, chosen by the domain.**

- *Within limit* — a single local transaction. Check, reserve, confirm. No saga.
- *Breach* — a saga: provisional reservation → approval requested → wait with
  timeout → `TradeActivated` on approval, or compensation (release the
  reservation, reject the trade) on rejection or expiry.

**2. Orchestration, not choreography.** The process has a timeout, a human
decision point, and a compensation path. It must be inspectable as a single
thing.

**3. The saga lives in the `Trading` module.**

The governing rule: *a saga belongs to the module that owns the aggregate whose
lifecycle it drives.* The saga state machine and the trade state machine are the
same machine — `Submitted → CreditPending → Active | Rejected`. Splitting them
across modules would create two sources of truth for one lifecycle.

`Trading` is the core domain and may depend on the contracts of supporting
modules. The reverse is not permitted.

**4. Ownership stays where the data belongs.**

| Module | Owns |
|---|---|
| `Trading` | The saga, the trade state, the orchestration |
| `Risk` | Exposure calculation and the credit reservation record |
| `Reference` | The credit limit itself, as counterparty master data |
| `Identity` | The right to approve (role `risk`, not `trader`) |

`Trading` never writes to another module's schema. It issues `ReserveCredit`
through `Risk.Contracts` and reacts to `CreditReserved` / `CreditLimitBreached`.
This is enforced by architecture tests, not by convention.

**5. No dedicated saga schema.**

- `trading.trade_approval_saga` — saga state, owned by `Trading`
- `risk.credit_reservation` — reservation record, owned by `Risk`
- `messaging.outbox_message`, `messaging.inbox_state` — infrastructure

`messaging` is the single cross-cutting schema in the system. That exception is
deliberate and narrow: it is transport, not domain (see ADR-0004).

**6. Implementation.** `MassTransitStateMachine<TradeApprovalState>` with the
EF Core saga repository and optimistic concurrency on `RowVersion`.
`CorrelationId = TradeId`, so redelivery of any event resolves to the same
instance and idempotency follows from the correlation rather than from
bookkeeping. The approval timeout uses MassTransit `Schedule<>`.

**7. Hosted in the API process** for now. The state is persistent and external
to the host, so relocating it later is a deployment change, not a redesign.

## Consequences

### Positive

- **Every saga step is atomic.** Saga state and the outbox share a database, so
  a state transition and the message it produces commit together. No lost
  messages, no state that ran ahead of its own publication. This is a direct
  dividend of extracting processes but not databases (ADR-0002).
- One lifecycle, one state machine, one place to look when a trade is stuck.
- The compensation path is exercised routinely rather than theoretically,
  because the approval timeout fires on its own in normal operation.
- Module ownership stays intact and testable: the saga coordinates, it does not
  reach across schemas.

### Negative

- **Isolation is what we actually give up** — the I in ACID, not atomicity. A
  provisional reservation is visible to everyone while approval is pending, so
  the domain must define what "pending" means: whether the reserved amount
  counts against the limit for the next trade booked. It does here, deliberately
  — the conservative reading is the correct one for credit risk.
- `Trading` takes a dependency on `Risk.Contracts` and `Reference.Contracts`.
  Acceptable direction (core → supporting), but it makes `Trading` the module
  with the most contract dependencies, and that should stay under review.
- The system now has two scheduling mechanisms. Without the rule in ADR-0003 —
  timeouts belonging to a process instance use MassTransit, instance-independent
  recurring work uses Hangfire — it becomes unclear within a month where any
  given job is triggered from.

### Neutral

- `Trading` grows. If it ever needs to be split, the saga state is already
  persistent and host-independent, so the seam is in place.

## Alternatives considered

**1. A dedicated `Seamline.Sagas` project or module.**
Rejected. It is a technical layer, not a bounded context: it has no domain
language of its own, it must know about every module, and everything
"process-shaped" accumulates in it. That directly contradicts the module rule in
ADR-0001 — modules are bounded contexts, not layers — and reproduces a small
monolith inside the monolith.

**2. Placing the saga in `Risk`.**
Rejected. `Risk` owns the credit decision, not the trade lifecycle. The saga's
terminal states are trade states. Putting it there would leave the trade's
lifecycle authoritative in two modules at once.

**3. Choreography instead of orchestration.**
Rejected. Choreography works for two or three reactive steps. This process has a
timeout, an external human decision, and a compensation branch — with
choreography it would exist nowhere readable, distributed across handlers, and
answering "why is this trade stuck" would require reconstructing it from logs.

**4. Running every trade confirmation through the saga.**
Rejected. Within-limit confirmations are a single local transaction on a single
database. Wrapping them in a distributed coordination protocol would add
latency, moving parts, and intermediate states to buy nothing. The saga is used
where the boundary is real.

## Revisit criteria

- **Approval workflows multiply.** If several modules develop their own
  human-approval processes, extract a workflow capability — but as a bounded
  context with its own language (approvals, delegation, escalation), never as a
  technical `Sagas` layer.
- **The state machine grows past roughly seven states or branches heavily.**
  At that point a dedicated workflow engine earns its keep.
- **`Trading` and `Risk` are ever split into separate deployables with separate
  databases.** Placement stays; the atomicity argument above does not, and the
  reservation protocol would need to be re-examined.
