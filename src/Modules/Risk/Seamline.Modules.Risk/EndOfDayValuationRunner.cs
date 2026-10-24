using Seamline.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Seamline.Modules.Inventory.Contracts;
using Seamline.Modules.MarketData.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Seamline.Modules.Risk.Internal;

// The actual EOD mark-to-market pass (ADR-0014). Lives inside Risk, not in
// Valuation.Worker — Position/RiskDbContext/ValuationSnapshot are internal
// by design (see Seamline.ArchTests.InternalVisibilityTests), so the worker
// reaches this through RiskModuleExtensions.RunEndOfDayValuationAsync, the
// same public-extension-method-on-IServiceProvider shape
// MigrateRiskModuleAsync already uses, rather than a separate assembly
// reaching into Risk's internals directly.
internal sealed class EndOfDayValuationRunner(IServiceProvider services, ILogger<EndOfDayValuationRunner> logger)
{
    // Fixed scenarios (ADR-0016) — not user-configurable. Flat and
    // single-commodity deliberately use different magnitudes: at the same
    // magnitude the two scenario types would produce identical MtM for
    // every position, since a position's MtM only ever depends on its own
    // commodity's curve price.
    private static readonly (ScenarioType Type, decimal Percentage)[] Scenarios =
    [
        (ScenarioType.FlatShock, 10m),
        (ScenarioType.FlatShock, -10m),
        (ScenarioType.SingleCommodityShock, 25m),
        (ScenarioType.SingleCommodityShock, -25m)
    ];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await DiscoverTenantIdsToValueAsync(cancellationToken);
        var run = EodRun.Start();
        logger.LogInformation("Valuation run {RunId} starting for {TenantCount} tenant(s) with positions, index-priced trades or storage.", run.RunId, tenantIds.Count);

        foreach (var tenantId in tenantIds)
        {
            await ValueTenantAsync(tenantId, run, cancellationToken);
        }
    }

    private async Task ValueTenantAsync(Guid tenantId, EodRun run, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.SetTenant(new TenantId(tenantId));

        var db = scope.ServiceProvider.GetRequiredService<RiskDbContext>();
        var curveDirectory = scope.ServiceProvider.GetRequiredService<ICurvePointDirectory>();

        var positions = await db.Positions.Where(p => p.NetVolume != 0).ToListAsync(cancellationToken);
        foreach (var position in positions)
        {
            var curvePoint = await curveDirectory.FindAsync(position.CommodityCode, position.DeliveryPeriod, cancellationToken);
            if (curvePoint is null)
            {
                logger.LogWarning(
                    "No curve point published for {Commodity}/{Period} (tenant {TenantId}) — skipping this position for this run.",
                    position.CommodityCode, position.DeliveryPeriod, tenantId);
                continue;
            }

            db.ValuationSnapshots.Add(ValuationSnapshot.Create(
                new TenantId(tenantId), position.CommodityCode, position.DeliveryPeriod,
                position.NetVolume, position.WeightedAvgPrice, curvePoint.Price, curvePoint.PublishedAt, run));

            // Rides along on the same Position/curvePoint this pass already
            // fetched — no separate query, no separate batch (ADR-0016).
            foreach (var (scenarioType, percentage) in Scenarios)
            {
                db.StressScenarioResults.Add(StressScenarioResult.Create(
                    new TenantId(tenantId), position.CommodityCode, position.DeliveryPeriod,
                    position.NetVolume, position.WeightedAvgPrice, curvePoint.Price, scenarioType, percentage, run));
            }
        }

        await ValueFormulaTradesAsync(scope.ServiceProvider, db, run, tenantId, cancellationToken);
        await ValueStockAsync(scope.ServiceProvider, db, run, tenantId, cancellationToken);

        // Marks the run for the tenant even when it valued nothing, so the
        // latest-run views show this run and not an older one (ADR-0041).
        db.ValuationRuns.Add(ValuationRun.Mark(new TenantId(tenantId), run));
        await db.SaveChangesAsync(cancellationToken);
    }

    // Index-priced trades are valued one by one (ADR-0035): MtM is
    // (F − expected) × V, and each stress scenario recomputes the expected
    // price at the shocked forward. Same run, same RunId as the positions.
    private async Task ValueFormulaTradesAsync(
        IServiceProvider scopedServices, RiskDbContext db, EodRun run, Guid tenantId, CancellationToken cancellationToken)
    {
        var pricing = scopedServices.GetRequiredService<FormulaPricingService>();
        var curveDirectory = scopedServices.GetRequiredService<ICurvePointDirectory>();

        var trades = await db.FormulaTrades.Where(f => f.IsOpen).ToListAsync(cancellationToken);
        foreach (var trade in trades)
        {
            var curvePoint = await curveDirectory.FindAsync(trade.CommodityCode, trade.DeliveryPeriod, cancellationToken);
            if (curvePoint is null)
            {
                logger.LogWarning(
                    "No curve point published for {Commodity}/{Period} (tenant {TenantId}) — skipping formula trade {TradeId} for this run.",
                    trade.CommodityCode, trade.DeliveryPeriod, tenantId, trade.TradeId);
                continue;
            }

            var formula = await pricing.LoadAsync(trade.CommodityCode, trade.DeliveryPeriod, trade.Pricing, cancellationToken);
            var expected = trade.MarkPrice(formula)!.Value;
            db.ValuationSnapshots.Add(ValuationSnapshot.CreateForFormulaTrade(
                trade, expected, curvePoint.Price, curvePoint.PublishedAt, run));

            foreach (var (scenarioType, percentage) in Scenarios)
            {
                db.StressScenarioResults.Add(StressScenarioResult.CreateForFormulaTrade(
                    trade, formula, curvePoint.Price, scenarioType, percentage, run));
            }
        }
    }

    // Gas in storage (ADR-0036): stock × (curve − WACOG) against the curve
    // point of the run's own month. Skipped and logged without one, like a
    // position.
    private async Task ValueStockAsync(
        IServiceProvider scopedServices, RiskDbContext db, EodRun run, Guid tenantId, CancellationToken cancellationToken)
    {
        var inventory = scopedServices.GetRequiredService<IInventoryDirectory>();
        var curveDirectory = scopedServices.GetRequiredService<ICurvePointDirectory>();
        var period = $"{run.ValuedAt:yyyy-MM}";

        foreach (var stock in await inventory.ListStockAsync(cancellationToken))
        {
            var curvePoint = await curveDirectory.FindAsync(stock.CommodityCode, period, cancellationToken);
            if (curvePoint is null)
            {
                logger.LogWarning(
                    "No curve point published for {Commodity}/{Period} (tenant {TenantId}) — skipping storage {Facility} for this run.",
                    stock.CommodityCode, period, tenantId, stock.Facility);
                continue;
            }

            db.InventoryValuations.Add(InventoryValuation.Create(
                new TenantId(tenantId), stock, period, curvePoint.Price, curvePoint.PublishedAt, run));
        }
    }

    // No tenant registry exists anywhere in this project — multi-tenancy is
    // shared-schema + tenant_id, not a central Tenant table (ADR-0005). The
    // owner connection is the only way to discover which tenants to value at
    // all, bypassing RLS and the EF query filter for this one read. Every
    // per-tenant read/write after this still goes through the restricted
    // seamline_app connection with RLS and the query filter both active,
    // same as everywhere else in the app.
    //
    // A tenant is visited if it has ever had a position, an index-priced
    // trade or a storage contract, not only if one is open now: a tenant
    // whose last position closed or whose storage emptied still gets this
    // run's marker, so its latest run is today's, valuing nothing, rather
    // than yesterday's with stale P&L (ADR-0041).
    private async Task<List<Guid>> DiscoverTenantIdsToValueAsync(CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<RiskDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"));

        await using var db = new RiskDbContext(optionsBuilder.Options, new TenantContext());
        var withPositions = await db.Positions.IgnoreQueryFilters()
            .Select(p => p.TenantId.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var withFormulaTrades = await db.FormulaTrades.IgnoreQueryFilters()
            .Select(f => f.TenantId.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        using var inventoryScope = services.CreateScope();
        var withStorage = await inventoryScope.ServiceProvider
            .GetRequiredService<IInventoryDirectory>().TenantsWithStorageAsync(cancellationToken);
        return withPositions.Union(withFormulaTrades).Union(withStorage).ToList();
    }
}
