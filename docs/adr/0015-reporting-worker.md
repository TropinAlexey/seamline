# ADR-0015: Reporting.Worker — Simplified REMIT Submission

**Status:** Accepted
**Date:** 2026-07

## Context

`Reporting.Worker` is the second of the two processes ADR-0001 named as
extracted on purpose (`Valuation.Worker` landed first, ADR-0014). Its job:
submit simplified REMIT-style trade reports to a stub regulator endpoint
(`acer-stub`) — not a compliant implementation, stated as such in README
and CLAUDE.md's clean-room section. Two facts from earlier ADRs constrain
the design before any new decision gets made:

- **ADR-0006**: "REMIT reporting must report a trade as it stood at the
  moment of reporting, not as it stands now. A late amendment must not
  silently rewrite what was already submitted." The source of truth for
  this worker therefore has to be `trading.trade_history` (versioned,
  append-only — each row is already an immutable point-in-time snapshot),
  never the live `trading.trade` row.
- **ADR-0002**: extraction criteria already named this worker's
  justification — "batch submission runs against an external regulator
  endpoint the API's request path has no business waiting on."

## Decision

**Batch, via Hangfire — same shape as `Valuation.Worker`,** not an
event-driven consumer reacting to `TradeActivated`/`TradeAmended` directly.
An event-driven design was considered and rejected below; batch scanning
`trade_history` satisfies ADR-0006's point-in-time requirement for free
(each history row is already immutable) while matching the load-profile
argument ADR-0002 already committed to.

**Reportable transitions**: a `trade_history` row is reportable when
`State ∈ {Active, Cancelled, Rejected}`. `Draft`/`Submitted`/`CreditPending`
are internal-only states with no external effect (same reasoning ADR-0010
already applied to `audit_event`); `Delivered` is physical fulfillment, not
a contract lifecycle event REMIT's trade-report table covers.

**Action derivation**: `State` alone doesn't distinguish a new trade from
an amendment — `Trade.Amend()` (ADR-0011) keeps `State == Active` and only
bumps `Version`. So: `State == Active` with no prior successfully-reported
`New` action for that `TradeId` → `New`; `State == Active` with one already
recorded → `Modify`; `State ∈ {Cancelled, Rejected}` → `Terminate`. This
reuses the status table itself for the check — no separate "is this the
first activation" bookkeeping.

**`trading.remit_report`** — append-only, `seamline_app` gets
`SELECT, INSERT` only, RLS enabled, same shape as `trade_history` and
`audit_event`. **A row is inserted only after a successful ack from
acer-stub** — there's no `Pending`/`Failed` status column. A `trade_history`
row with no matching `remit_report` row just means "not yet successfully
reported"; the next EOD run finds it again via a `LEFT JOIN` and retries.
Idempotency comes from this presence check, not from a state machine — if
a resend ever reaches acer-stub for an already-acked report, the stub's own
"duplicate" response is still recorded as terminal success, same as a
fresh "accepted" ack.

**Reporting logic lives inside `Trading`**, which owns `trade_history` —
not a new module, not inside `Valuation.Worker`'s `Risk`. Mirrors
`EndOfDayValuationRunner`'s placement exactly: an internal
`RemitReportingRunner`, reached through one public
`TradingModuleExtensions.RunReportingBatchAsync(IServiceProvider,
CancellationToken)`, since `RemitReport`/`TradingDbContext`/`Trade` stay
internal by design (`InternalVisibilityTests`).

**HTTP submission uses `Microsoft.Extensions.Http.Resilience`**
(`AddStandardResilienceHandler()` on an `IHttpClientFactory`-registered
client) — retry, timeout, and circuit-breaker bundled, built on Polly v8.
Not a hand-rolled retry loop: unlike MassTransit's bus-level retry (message
redelivery semantics, already used for the saga's race condition — ADR-0004
territory), this is a plain outbound HTTP call to a flaky external
endpoint, exactly the shape this package exists for. A new dependency,
added deliberately for something not worth reinventing — same reasoning
ADR-0004 already used to justify MassTransit's own EF Core outbox over a
hand-rolled one.

**`acer-stub` is a real service in `docker-compose.yml`**, not a
configurable-URL-with-no-actual-server placeholder. A new minimal project
(`src/Seamline.AcerStub`, one Minimal API endpoint) that returns
500/timeout/duplicate/success at random — the only way to actually exercise
the retry/idempotency logic above rather than assert it exists.

## Consequences

### Positive

- ADR-0001's architectural thesis is now fully delivered — both named
  extracted processes exist, not one.
- The point-in-time correctness `trade_history` already gives every other
  consumer (Audit, now Reporting) needed zero new machinery — reading an
  append-only table is naturally safe for "report it as it stood," the
  same dividend ADR-0006 already paid for elsewhere.
- Retry/idempotency against a genuinely flaky dependency is demonstrated
  against a real HTTP boundary, not asserted in prose.

### Negative

- `remit_report`'s "row exists = success" model means a permanently
  failing submission (not just transient) is silently retried forever,
  once per EOD run, with no escalation path. Acceptable at this project's
  scale — a real deployment would need alerting on a report that's stayed
  unreported past some threshold, out of scope here.
- `Seamline.AcerStub` is a third process to run locally
  (`Seamline.Api` + `Seamline.Valuation.Worker` + `Seamline.AcerStub`
  + `Seamline.Reporting.Worker`) — a real, if small, local-dev cost for a
  project that started as "one process, `docker compose up`."

## Alternatives considered

**Event-driven consumer (`TradeActivatedConsumer`-shaped) instead of
batch.** Rejected. Nothing about a per-trade REMIT submission needs
same-transaction atomicity with the trade's own write — unlike Audit's
consumer, which persists into this same database. Routing single-trade HTTP
calls through a consumer would also fight ADR-0002's own load-profile
argument (an external regulator call the API process shouldn't wait on) by
putting it back in-process. Batch already satisfies point-in-time
correctness for free, so there's no correctness reason to prefer per-event
either.

**Explicit status column (`Pending`/`Sent`/`Failed`) instead of
presence-of-row.** Rejected as unnecessary state to maintain — a `LEFT
JOIN` against `trade_history` already answers "what's unreported" without
tracking transitions between states nothing ever reads. Would earn its
keep if a future need (e.g. surfacing "3 failed attempts" to an operator)
requires it — not speculatively now.

**Hand-rolled retry loop, matching MassTransit's own
`Intervals(100, 250, 500, 1000, 2000)` pattern.** Rejected — that pattern
is bus-message redelivery, a different problem (idempotent consumers,
outbox-backed) from a single outbound HTTP call needing retry/timeout/
circuit-breaker. `Microsoft.Extensions.Http.Resilience` is the standard
answer for the latter in current .NET; reimplementing it by hand would be
exactly the kind of solved-problem duplication ADR-0004 already argued
against for the outbox.

## Revisit criteria

- **If failed reports need operator visibility**: add the status
  column/alerting then, informed by what "3 days unreported" actually
  needs to look like — not speculatively now.
- **If a second module needs outbound HTTP resilience**: extract
  `AddTradingReportingClient`'s resilience configuration into something
  shared, rather than duplicating the `AddStandardResilienceHandler()` call
  site by site.
