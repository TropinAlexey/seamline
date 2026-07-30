# ADR-0016: Stress Scenarios Instead of VaR

**Status:** Accepted
**Date:** 2026-07

## Context

CLAUDE.md's scope boundaries state this directly: "No VaR. Stress scenarios
instead." Until now that line was aspirational — `Valuation.Worker`
(ADR-0014) computes real end-of-day MtM, but nothing stresses it. The gap
tracker carried two named scenario shapes: a flat ±10% shock across every
curve, and a shock on a single commodity.

## Decision

**Two scenario types, four scenario rows per position per EOD run**:
`FlatShock` at ±10% (every curve point moves together — a systemic,
book-wide move) and `SingleCommodityShock` at ±25% (only the position's own
commodity moves — an idiosyncratic, concentration-risk move). Different
magnitudes on purpose: at the same magnitude, the two scenarios produce
*identical* MtM for every position, because a position's MtM only ever
depends on its own commodity's curve price — shocking every other curve has
zero effect on a position that doesn't hold that commodity. A flat shock at
10% and a single-commodity shock also at 10% would be two labeled rows
carrying the same number. Giving the concentration shock a larger,
idiosyncratic-event magnitude (25%) is both more realistic (a single-name
move is rarely as small as a systemic one) and the only way the two scenario
types actually diverge at the per-position grain this project computes at.

**Computed inside the same EOD pass `EndOfDayValuationRunner` already
runs**, not a separate on-demand HTTP endpoint or a second batch pass. The
runner already loads every open `Position` and its current `PriceCurvePoint`
once per tenant, per run; stress scenarios reuse exactly that data instead
of re-querying it. This was an explicit tradeoff the user asked for
("maximum reuse of this data... optimal and fast generation") over the
simpler-to-build alternative of a synchronous, ad-hoc HTTP endpoint that
would look up the same data again per request.

**MtM computation is now a shared method, not duplicated code.**
`ValuationSnapshot.Create` already computed `Math.Round((curvePrice -
weightedAvgPrice) * netVolume, 2, MidpointRounding.ToEven)` inline. Adding a
second entity that needs the identical formula for a shocked price would
duplicate it — instead, a shared `MtmCalculator.Calculate(curvePrice,
weightedAvgPrice, netVolume)` is the one place this formula exists,
called by both `ValuationSnapshot.Create` and the new
`StressScenarioResult.Create`. This is a direct, stronger reading of
ADR-0007's "rounding is a named, explicit operation at the point it
happens" — one *named method*, not just one line repeated twice.

**`risk.stress_scenario_result`** — append-only, same shape as
`valuation_snapshot`: `Id, TenantId, CommodityCode, DeliveryPeriod,
NetVolume, WeightedAvgPrice, ScenarioType, ShockPercentage, ShockedPrice,
MtmAmount, ValuedAt`. `seamline_app` gets `SELECT, INSERT` only, RLS
enabled. `ScenarioType` is `FlatShock | SingleCommodityShock`;
`ShockPercentage` carries the signed magnitude (+10/-10/+25/-25) so the two
scenario types and both directions share one table and one entity rather
than four near-identical ones.

**Skipped the same way real valuation is**: `NetVolume == 0` positions
produce no rows (shocking a flat position's MtM is always zero, not worth
storing); a position with no published curve point is skipped and logged,
same as `EndOfDayValuationRunner` already does for real valuation.

## Consequences

### Positive

- CLAUDE.md's stated scope boundary is now true in code, not just in
  prose.
- Zero new queries against the database — stress scenarios ride along on
  data the EOD pass was already going to fetch for real valuation.
- The shared `MtmCalculator` closes a real, if minor, duplication risk
  before it existed: two formulas computing "the same" number that could
  have drifted apart under a future edit to one but not the other.

### Negative

- Fixed magnitudes (±10%, ±25%), not user-configurable — matches exactly
  what CLAUDE.md/the TODO asked for, but a real stress-testing tool would
  let a risk manager pick the shock size and which commodity to isolate.
  Explicitly out of scope here (see Revisit criteria).
- `stress_scenario_result` grows by four rows per open position per day,
  on top of `valuation_snapshot`'s one — no retention policy, same
  accepted-for-now gap ADR-0014 already noted for its own table.

## Alternatives considered

**On-demand HTTP endpoint, computed synchronously per request.** Rejected
per explicit direction from the user: this would re-fetch `Position` and
`PriceCurvePoint` data the EOD pass already has in hand, and would need its
own persistence decision (store per request, or not at all) instead of
reusing the append-only pattern `valuation_snapshot` already established.

**Same ±10% magnitude for both scenario types.** Rejected — mathematically
redundant per position (see Decision above); the two types would carry
identical numbers with no additional information, just two more rows to
store and read per position.

**A single "shock" table with real MtM (`ScenarioType = None`) folded in
alongside the stress rows**, instead of keeping `valuation_snapshot`
separate. Rejected — `valuation_snapshot` already exists, is documented
(ADR-0014), and has no `ScenarioType` concept; retrofitting it would be a
breaking schema change to an already-shipped table for a saving of one
table, not a real simplification.

## Revisit criteria

- **If a risk manager needs to pick shock size or target commodity at
  request time**: that's the on-demand endpoint rejected above, revisited
  with a real reason once someone actually needs it — not speculatively
  now.
- **If `stress_scenario_result` needs retention limits**: same criterion
  ADR-0014 already named for `valuation_snapshot` — design it once an
  actual row-count/storage number motivates it.
