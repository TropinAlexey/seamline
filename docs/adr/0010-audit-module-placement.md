# ADR-0010: Audit Module Placement

**Status:** Accepted
**Date:** 2026-07
**Deciders:** Alexey Tropin

## Context

ADR-0006 decided `audit.audit_event` should exist — "cross-module
actor/action/timestamp/context record" — but never assigned it an owner. It
sat unimplemented. Filling the gap means answering the question ADR-0006
left open: which module writes to it?

None of the ownership rules already in place answer this directly.
ADR-0008's rule — *a saga belongs to the module that owns the aggregate
whose lifecycle it drives* — doesn't apply: `audit_event` isn't a lifecycle,
it's an observation of lifecycles that live in other modules (`Trading`'s
`TradeActivated`/`TradeRejected` today; more modules' events over time). No
existing module owns "an event happened somewhere else" as part of its
domain.

The `messaging` schema is the one precedent for something cross-cutting in
this codebase, but ADR-0008 is explicit that it's transport infrastructure,
not domain — "it is transport, not domain." `audit_event` is domain data
(who did what, when, in what context), not a delivery mechanism, so it isn't
covered by that exception either.

## Decision

**A new `Audit` module**, `Seamline.Modules.Audit` + `.Contracts`, structured
like every other module (impl in `*.Internal`, arch-tested boundaries), but
with a narrower role than any existing one:

- It **owns `audit.audit_event`** and nothing else.
- It **never publishes**. No other module reacts to an audit record; there
  is nothing to react to.
- It **only consumes integration events that already cross a module
  boundary** — `TradeActivated`, `TradeRejected` today. It never reaches
  into another module's schema or exposes a synchronous query interface of
  its own for other modules to call. This keeps `Audit` a pure sink: every
  other module's dependency graph is unaffected by Audit's existence.
- **Actor and reason travel on the event itself.** `TradeActivated` and
  `TradeRejected` gained `Actor`/`Reason` fields, populated from the same
  `changedBy`/`changeReason` values `Trade.Activate`/`Reject` already pass to
  `TradeHistory.CreateSnapshot`. The alternative — Audit querying `Trading`
  for who did what — would be a synchronous cross-module call for
  information the publishing module already has in hand at the moment it
  publishes.
- **Append-only, same enforcement as `trade_history`** (ADR-0006):
  `seamline_app` gets `GRANT SELECT, INSERT` on `audit.audit_event`, nothing
  more. No `UPDATE`/`DELETE` grant is the actual guarantee, not a convention.

## Consequences

### Positive

- Every other module's boundary is unchanged. Audit depends on the
  `.Contracts` of the modules it observes; nothing depends on Audit.
- Extending coverage to a new module's events later is additive: give the
  new event an `Actor`/`Reason` (or equivalent) and add one more consumer to
  `Audit`. No change to the modules already wired up.
- The append-only guarantee is enforced the same way as `trade_history`, so
  there's one pattern to reason about for both audit-shaped tables, not two.

### Negative

- Coverage is opt-in per event. An event that doesn't carry actor/reason
  fields won't produce a useful audit row even if a consumer is added for
  it — there's no compiler-enforced requirement that integration events
  carry this information. This is a convention, not a guarantee.
- Only events that already cross a module boundary are auditable this way.
  Purely intra-module transitions (e.g. `Trade.Submit`, `EnterCreditPending`
  — visible in `trade_history` but never published) are out of scope for
  `audit.audit_event` by construction. That split — cross-module actions in
  `audit_event`, full intra-module history in each module's own
  `*_history` table — is deliberate, not an oversight: `audit_event` is
  ADR-0006's cross-module record, not a replacement for `trade_history`.

## Alternatives considered

**1. Fold audit writing into each publishing module (`Trading` writes its
own audit rows).**
Rejected. That's exactly the `messaging`-schema shape ADR-0008 already ruled
out for a different reason: it would mean every module doing its own thing
under a "shared" schema, no single owner, no consistent enforcement of the
append-only grant. `audit.audit_event` needs one owner, same reasoning as
`trade_history` needing one.

**2. A synchronous `IAuditWriter` in SharedKernel, called directly by every
module.**
Rejected. SharedKernel is base types shared by value (`TenantId`, `Entity`),
not a service locator for cross-module behavior — adding a service there
would make SharedKernel a hidden coupling point between every module,
exactly what Contracts projects and the bus exist to avoid.

## Revisit criteria

- **If audit needs to read across the whole system for reporting** (e.g. a
  compliance dashboard), that's a query interface (`Audit.Contracts`) other
  modules never call, not more publish rights for `Audit` itself — it still
  never gets to write anywhere but its own schema.
- **If most modules end up with an audit consumer**, consider whether the
  event contracts should standardize an `IAuditableEvent` shape (`Actor`,
  `Reason`) rather than repeating the two fields ad hoc per record.
