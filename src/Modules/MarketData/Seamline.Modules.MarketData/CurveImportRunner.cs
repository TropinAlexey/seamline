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
// published a commodity (manually, via POST /curve-points) is left alone.
// The commodity list comes from the same table rather than a hard-coded
// POWER/GAS pair (ADR-0033), so a new commodity is imported as soon as some
// tenant publishes a first point for it.
internal sealed class CurveImportRunner(IServiceProvider services, ILogger<CurveImportRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var month = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var deliveryPeriod = $"{month:yyyy-MM}";

        foreach (var commodityCode in await DiscoverCommodityCodesAsync(cancellationToken))
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

        await PublishTodaysFixingsAsync(month, cancellationToken);
    }

    // Today's fixing for every price index some tenant has (ADR-0035). The
    // indices are synthetic — real benchmarks are proprietary — so the value
    // always comes from SyntheticCurveSource, whatever curve source is
    // configured. Fixings are append-only: one that already exists, published
    // by hand or by an earlier run, is left as it is. Weekends have none.
    private async Task PublishTodaysFixingsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        if (!PricingCalendar.IsPricingDay(today))
            return;

        List<(Guid TenantId, string Code, string CommodityCode)> indices;
        await using (var owner = CreateOwnerContext())
        {
            indices = (await owner.PriceIndices.IgnoreQueryFilters()
                    .Select(i => new { TenantId = i.TenantId.Value, i.Code, i.CommodityCode })
                    .ToListAsync(cancellationToken))
                .Select(i => (i.TenantId, i.Code, i.CommodityCode))
                .ToList();
        }

        var source = new SyntheticCurveSource();
        foreach (var (tenantId, code, commodityCode) in indices)
        {
            using var scope = services.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(new TenantId(tenantId));
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

            if (await db.IndexFixings.AnyAsync(f => f.IndexCode == code && f.FixingDate == today, cancellationToken))
                continue;
            db.IndexFixings.Add(IndexFixing.Publish(
                new TenantId(tenantId), code, today, source.DailyFixing(commodityCode, today), "curve-import"));
            await db.SaveChangesAsync(cancellationToken);
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
        await using var db = CreateOwnerContext();
        return await db.PriceCurvePoints.IgnoreQueryFilters()
            .Where(p => p.CommodityCode == commodityCode)
            .Select(p => p.TenantId.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    // Same owner-connection read as the tenant discovery above.
    private async Task<List<string>> DiscoverCommodityCodesAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateOwnerContext();
        return await db.PriceCurvePoints.IgnoreQueryFilters()
            .Select(p => p.CommodityCode)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);
    }

    private MarketDataDbContext CreateOwnerContext()
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<MarketDataDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"));
        return new MarketDataDbContext(optionsBuilder.Options, new TenantContext());
    }
}
