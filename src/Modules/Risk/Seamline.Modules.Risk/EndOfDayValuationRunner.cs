using Seamline.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await DiscoverTenantIdsWithOpenPositionsAsync(cancellationToken);
        logger.LogInformation("Valuation run starting for {TenantCount} tenant(s) with open positions.", tenantIds.Count);

        foreach (var tenantId in tenantIds)
        {
            await ValueTenantAsync(tenantId, cancellationToken);
        }
    }

    private async Task ValueTenantAsync(Guid tenantId, CancellationToken cancellationToken)
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
                position.NetVolume, position.WeightedAvgPrice, curvePoint.Price, curvePoint.PublishedAt));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // No tenant registry exists anywhere in this project — multi-tenancy is
    // shared-schema + tenant_id, not a central Tenant table (ADR-0005). The
    // owner connection is the only way to discover which tenants have open
    // positions at all, bypassing RLS and the EF query filter for this one
    // read. Every per-tenant read/write after this still goes through the
    // restricted seamline_app connection with RLS and the query filter both
    // active, same as everywhere else in the app.
    private async Task<List<Guid>> DiscoverTenantIdsWithOpenPositionsAsync(CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<RiskDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"));

        await using var db = new RiskDbContext(optionsBuilder.Options, new TenantContext());
        var positions = await db.Positions.IgnoreQueryFilters()
            .Where(p => p.NetVolume != 0)
            .ToListAsync(cancellationToken);

        return positions.Select(p => p.TenantId.Value).Distinct().ToList();
    }
}
