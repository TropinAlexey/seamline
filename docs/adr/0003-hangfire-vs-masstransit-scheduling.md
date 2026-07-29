# ADR-0003: Hangfire vs MassTransit `Schedule<>`

**Status:** Accepted
**Date:** 2026-07

## Context

Two different kinds of "do this later" already exist in the codebase by the
time this is written down: `TradeApprovalStateMachine`'s approval timeout
(ADR-0008) uses MassTransit's `Schedule<>`, and `Valuation.Worker`'s EOD
revaluation (ADR-0014) uses Hangfire. Both landed before this ADR did —
ADR-0008's own Negative section already named the risk directly: "the
system now has two scheduling mechanisms... it becomes unclear within a
month where any given job is triggered from" without a rule written down.
This is that rule, made explicit rather than left implicit in two other
ADRs' prose.

## Decision

**A timeout owned by one specific process instance uses MassTransit
`Schedule<>`.** `TradeApprovalStateMachine.ApprovalTimeout` is scoped to one
saga instance (`CorrelateById(context => context.Message.TradeId)`) — it
only exists because that particular trade's approval is pending, lives and
dies with that saga instance, and is explicitly `Unschedule`d the moment the
instance resolves (approve, reject, or cancel). Scheduling it through the
same message bus and the same transactional outbox the rest of the saga's
messages go through means the schedule token, the state transition, and the
outbox write commit together — no separate mechanism, no separate failure
mode to reason about for something that's fundamentally part of one saga's
lifecycle.

**Recurring work with no owning instance uses Hangfire.**
`Valuation.Worker`'s EOD job isn't scoped to any single trade, saga, or
request — it runs once a day, for every tenant, regardless of what
individual sagas exist or have ever existed. There's no "instance" to
correlate it to, and MassTransit's `Schedule<>` is built around exactly that
correlation (a saga instance, an event, a token). Hangfire's own model — a
recurring job on a cron expression, independent of any particular saga or
request — matches this shape directly, and it comes with what a real
recurring job needs that `Schedule<>` doesn't: persistent job state visible
outside the message bus, retry/failure tracking per run, and (eventually) a
dashboard to see run history.

**The test, concretely**: if the "later" only makes sense in the context of
one specific instance of something (a saga, a trade, a request) and stops
being meaningful once that instance resolves — `Schedule<>`. If it would
still make sense to run even if every current instance of everything
disappeared — Hangfire.

## Consequences

### Positive

- The ambiguity ADR-0008 flagged is resolved with one sentence-length test,
  not a growing list of case-by-case precedents.
- Both mechanisms stay narrowly scoped to what they're actually good at —
  `Schedule<>` never has to pretend to be a cron scheduler, Hangfire never
  has to pretend to understand saga correlation.

### Negative

- Two scheduling mechanisms still exist in the codebase, with two different
  storage models (`Schedule<>`'s token lives in the saga's own outbox;
  Hangfire owns its own `hangfire` schema in the same database) and two
  different places to look when something didn't run. Accepted — the
  alternative (forcing one mechanism to cover both shapes) was rejected
  below for a concrete reason, not merged away for the sake of having one.

## Alternatives considered

**Use Hangfire for the approval timeout too.** Rejected. Hangfire jobs are
identified by job ID, not saga correlation — wiring a Hangfire-scheduled job
back to the specific `TradeApprovalState` instance it belongs to (and
cancelling it cleanly when that instance resolves early) would mean
re-deriving the correlation MassTransit's saga machinery already provides
for free, and losing the same-transaction guarantee between the schedule and
the outbox write.

**Use MassTransit `Schedule<>`/a recurring message for the EOD job.**
Rejected. `Schedule<>` schedules a message *to* a saga instance; there is no
saga instance for "revalue every tenant's book today." A recurring message
published on a timer would need something to own the timer in the first
place — which is exactly the "instance-independent recurring work" case
this ADR routes to Hangfire, just built by hand instead of using the tool
built for it.

## Revisit criteria

- **If a third scheduling need appears that doesn't cleanly fit either
  category** (e.g., a timeout that outlives its own saga instance, or
  recurring work that needs saga-style correlation): revisit the test above
  rather than forcing a fit.
