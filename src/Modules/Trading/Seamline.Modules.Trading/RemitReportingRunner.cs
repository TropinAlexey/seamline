using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// The actual REMIT batch pass (ADR-0015). Lives inside Trading, not in
// Reporting.Worker — Trade/TradeHistory/RemitReport/TradingDbContext are
// internal by design (InternalVisibilityTests), so the worker reaches this
// through TradingModuleExtensions.RunReportingBatchAsync, the same shape
// Risk's EndOfDayValuationRunner already uses for Valuation.Worker.
internal sealed class RemitReportingRunner(IServiceProvider services, ILogger<RemitReportingRunner> logger)
{
    private static readonly TradeState[] ReportableStates = [TradeState.Active, TradeState.Cancelled, TradeState.Rejected];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await DiscoverTenantIdsWithUnreportedTradesAsync(cancellationToken);
        logger.LogInformation("Reporting run starting for {TenantCount} tenant(s) with unreported trades.", tenantIds.Count);

        foreach (var tenantId in tenantIds)
        {
            await ReportTenantAsync(tenantId, cancellationToken);
        }
    }

    private async Task ReportTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.SetTenant(new TenantId(tenantId));

        var db = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<IRemitSubmissionClient>();

        // Ordered by (TradeId, Version) on purpose: New/Modify derivation
        // below depends on whether an earlier version of the same trade was
        // already recorded — out-of-order processing within a run would
        // misclassify a Modify as a New before its New has landed.
        var candidates = await db.TradeHistory
            .Where(h => ReportableStates.Contains(h.State))
            .Where(h => !db.RemitReports.Any(r => r.TradeId == h.TradeId && r.Version == h.Version))
            .OrderBy(h => h.TradeId).ThenBy(h => h.Version)
            .ToListAsync(cancellationToken);

        var failedTradeIds = new HashSet<Guid>();

        foreach (var history in candidates)
        {
            // An earlier version of this same trade failed to submit this
            // run — its own retry (next run) has to land before this one
            // can be classified correctly, so skip it for now rather than
            // risk sending two "New" reports for the same trade.
            if (failedTradeIds.Contains(history.TradeId))
                continue;

            var action = history.State switch
            {
                TradeState.Cancelled or TradeState.Rejected => RemitAction.Terminate,
                TradeState.Active => await HasExistingNewReportAsync(db, history.TradeId, cancellationToken)
                    ? RemitAction.Modify
                    : RemitAction.New,
                _ => throw new InvalidOperationException($"Trade history state {history.State} is not reportable.")
            };

            try
            {
                var reportXml = RemitReportXml.Build(history, action);
                var ackId = await client.SubmitAsync(reportXml, cancellationToken);

                db.RemitReports.Add(RemitReport.CreateAcknowledged(new TenantId(tenantId), history.TradeId, history.Version, action, ackId));
                // Committed immediately, not batched — the next candidate's
                // HasExistingNewReportAsync check (and the failedTradeIds
                // guard above) both depend on seeing this run's own prior
                // successes, not just what existed before the run started.
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "REMIT submission failed for trade {TradeId} v{Version} — will retry next run.",
                    history.TradeId, history.Version);
                failedTradeIds.Add(history.TradeId);
            }
        }
    }

    private static Task<bool> HasExistingNewReportAsync(TradingDbContext db, Guid tradeId, CancellationToken cancellationToken) =>
        db.RemitReports.AnyAsync(r => r.TradeId == tradeId && r.Action == RemitAction.New, cancellationToken);

    // No tenant registry exists anywhere in this project (ADR-0005) — same
    // reasoning as Valuation.Worker's EndOfDayValuationRunner. The owner
    // connection with IgnoreQueryFilters() is the only way to discover
    // which tenants have unreported trades, bypassing RLS and the EF query
    // filter for this one read. Every per-tenant read/write after this goes
    // through the normal restricted seamline_app connection.
    private async Task<List<Guid>> DiscoverTenantIdsWithUnreportedTradesAsync(CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var optionsBuilder = new DbContextOptionsBuilder<TradingDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"));

        await using var db = new TradingDbContext(optionsBuilder.Options, new TenantContext());
        var candidates = await db.TradeHistory.IgnoreQueryFilters()
            .Where(h => ReportableStates.Contains(h.State))
            .Where(h => !db.RemitReports.IgnoreQueryFilters().Any(r => r.TradeId == h.TradeId && r.Version == h.Version))
            .ToListAsync(cancellationToken);

        return candidates.Select(h => h.TenantId.Value).Distinct().ToList();
    }
}
