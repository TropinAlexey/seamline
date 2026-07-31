using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Seamline.SharedKernel;

namespace Seamline.Modules.MarketData.Internal;

// The actual curve-import pass (ADR-0018). Lives inside MarketData, not
// Valuation.Worker — PriceCurvePoint/MarketDataDbContext are internal by
// design, so the worker reaches this through
// MarketDataModuleExtensions.RunCurveImportAsync, the same shape Risk's
// EndOfDayValuationRunner already uses for the EOD job.
//
// Scope, deliberately: only refreshes a (tenant, commodity) pair that
// already has at least one PriceCurvePoint row for that commodity — import
// keeps an existing relationship fresh, it doesn't invent a brand-new
// tenant/commodity pairing out of nothing. A tenant that has never
// published POWER or GAS (manually, via POST /curve-points) is left alone.
internal sealed class CurveImportRunner(IServiceProvider services, ILogger<CurveImportRunner> logger)
{
    private static readonly string[] Commodities = ["POWER", "GAS"];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var month = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var deliveryPeriod = $"{month:yyyy-MM}";

        foreach (var commodityCode in Commodities)
        {
            var tenantIds = await DiscoverTenantIdsWithExistingCurvePointAsync(commodityCode, cancellationToken);
            if (tenantIds.Count == 0)
            {
                logger.LogInformation("Curve import: no tenant has ever published {Commodity} — nothing to refresh.", commodityCode);
                continue;
            }

            // Fetched once per commodity, not once per tenant — the market
            // price is the same for everyone, so there is no reason to hit
            // the external API N times for N tenants.
            var price = await FetchPriceAsync(commodityCode, month, cancellationToken);
            if (price is null)
            {
                logger.LogWarning(
                    "Curve import: no price available for {Commodity} this run — {TenantCount} tenant(s) keep their existing point.",
                    commodityCode, tenantIds.Count);
                continue;
            }

            foreach (var tenantId in tenantIds)
            {
                await UpsertAsync(tenantId, commodityCode, deliveryPeriod, price.Value, cancellationToken);
            }
        }
    }

    private async Task<decimal?> FetchPriceAsync(string commodityCode, DateOnly month, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var sourceKey = configuration[$"MarketData:CurveImport:Sources:{commodityCode}"] ?? "Synthetic";
        var source = scope.ServiceProvider.GetRequiredKeyedService<ICurveSource>(sourceKey);

        return await source.GetMonthlyAveragePriceAsync(commodityCode, month, cancellationToken);
    }

    private async Task UpsertAsync(Guid tenantId, string commodityCode, string deliveryPeriod, decimal price, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.SetTenant(new TenantId(tenantId));

        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        // Same upsert semantics MarketDataEndpoints' POST /curve-points
        // already uses — import intentionally overwrites an earlier manual
        // publish for the current month.
        var existing = await db.PriceCurvePoints.FirstOrDefaultAsync(
            p => p.CommodityCode == commodityCode && p.DeliveryPeriod == deliveryPeriod, cancellationToken);

        if (existing is null)
        {
            db.PriceCurvePoints.Add(PriceCurvePoint.Create(new TenantId(tenantId), commodityCode, deliveryPeriod, price));
        }
        else
        {
            existing.UpdatePrice(price);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // No tenant registry exists anywhere in this project (ADR-0005) — same
    // reasoning as EndOfDayValuationRunner/RemitReportingRunner. Bypasses
    // RLS and the EF query filter for this one read via the owner
    // connection; every write after this goes through the normal
    // restricted seamline_app connection with RLS and the query filter
    // both active.
    private async Task<List<Guid>> DiscoverTenantIdsWithExistingCurvePointAsync(string commodityCode, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<MarketDataDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"));

        await using var db = new MarketDataDbContext(optionsBuilder.Options, new TenantContext());
        return await db.PriceCurvePoints.IgnoreQueryFilters()
            .Where(p => p.CommodityCode == commodityCode)
            .Select(p => p.TenantId.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
