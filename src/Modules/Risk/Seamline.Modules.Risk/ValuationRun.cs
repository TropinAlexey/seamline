using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// One row per tenant per EOD run, written whether or not the run valued
// anything for the tenant (ADR-0041). "The latest run" is the newest of
// these, not the newest valuation row: a run that skipped a storage
// contract (no curve point) or found it empty writes no inventory row, and
// picking the newest row would show the previous run's P&L as current.
// Append-only, like the rows it marks (ADR-0006).
internal sealed class ValuationRun : TenantOwnedEntity<Guid>
{
    public Guid RunId { get; private set; }
    public DateTimeOffset ValuedAt { get; private set; }

    private ValuationRun() { }

    public static ValuationRun Mark(TenantId tenantId, EodRun run) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, RunId = run.RunId, ValuedAt = run.ValuedAt };
}
