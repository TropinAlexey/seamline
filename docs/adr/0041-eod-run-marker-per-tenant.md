# ADR-0041: The latest EOD run is a per-tenant run marker

## Status

Accepted. Amends in part [ADR-0031](0031-risk-read-models-for-the-ui.md).

## Context

[ADR-0031](0031-risk-read-models-for-the-ui.md) gave every EOD row a
`run_id`. The three latest-run views (`/valuations/latest`,
`/stress-results/latest`, `/inventory/valuations/latest`) found the
latest run as the run of the newest row in their own table.

That is only right while every run writes a row to every table. Review of
gas storage ([spec 02](../specs/02-gas-storage.md)) found where it is not:

- A run skips a storage contract when the run's month has no curve point
  (spec 02, rule 6), and writes nothing for an empty one.
- The newest `inventory_valuation` row then belongs to an older run.
  The Storage page showed that run's unrealized P&L as if it were today's,
  for gas that may no longer be in the ground.
- The same holds for positions. A tenant whose positions all closed, or
  all lacked a curve point, kept showing the last run that valued one.
- Tenant discovery made it worse. It visited only tenants with something
  open now, so a tenant whose last position closed was never visited
  again.

## Decision

- **A run marker per tenant.**
  - The EOD run writes one `risk.valuation_run` row (`tenant_id`, `run_id`,
    `valued_at`) for every tenant it visits. It writes the row in the same
    `SaveChanges` as that tenant's valuations, and whether or not it
    valued anything.
  - The table is append-only (`SELECT, INSERT` only, ADR-0006) and has RLS
    from creation (ADR-0005).
- **The latest run is the newest marker.** All three views select rows by
  the run id of the tenant's newest marker. A view with no rows for that run
  returns the run with no items, not an older run.
- **Discovery visits every tenant that has ever had something to value.**
  This means any position row (flat ones too), any index-priced trade and
  any storage contract (empty ones too). A tenant with nothing open gets a
  marker and an empty valuation. At this scale that is cheap, and it is
  the only way an emptied storage stops showing P&L.
- **Backfill.** The migration derives a marker for every past run from the
  rows it wrote. A past run that wrote no row for a tenant left no trace,
  so it cannot be recovered. The first run after the migration corrects
  every tenant.

## Relationship to existing ADRs

| ADR | Effect |
|---|---|
| [0031](0031-risk-read-models-for-the-ui.md) | **Amended in part.** "The latest run" is the tenant's newest `valuation_run` marker, not the newest row of each table. Why: a run that writes no row to a table made that table's view show an older run as current. The run id on every row, server-side totals and the stress base total from the same run all stand. |
| [0014](0014-valuation-worker.md) | Upheld. Positions without a curve point are still skipped and logged. Now the view shows them as not valued in this run. |
| [0036](0036-gas-storage-inventory.md), [0040](0040-inventory-publishes-no-events.md) | Upheld. Risk still reads stock through `IInventoryDirectory`. Its discovery call now returns tenants with any storage contract (`TenantsWithStorageAsync`), tenant ids only. |

## Consequences

- An empty Storage card and a skipped contract show "—" for unrealized
  P&L after the next run, instead of an old figure.
- The Valuations and Stress pages can show a run with no rows. That is
  the truthful answer when nothing could be valued.
- Every EOD run writes one marker per known tenant, including dormant
  ones. If tenants ever number in the thousands, discovery needs a tenant
  registry. That was already ADR-0005's revisit trigger.
