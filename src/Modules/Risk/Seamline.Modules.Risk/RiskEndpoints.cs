using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.MarketData.Contracts;
using Seamline.Modules.Reference.Contracts;
using Seamline.Modules.Risk.Contracts;

namespace Seamline.Modules.Risk.Internal;

public static class RiskEndpoints
{
    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder app)
    {
        // Live view: positions marked against the current curve. Every total
        // is summed here, from already-rounded per-row amounts, so the UI
        // never adds money (ADR-0030).
        app.MapGet("/positions", async (RiskDbContext db, ICurvePointDirectory curvePointDirectory, CancellationToken ct) =>
        {
            var positions = await db.Positions.AsNoTracking()
                .Where(p => p.NetVolume != 0)
                .OrderBy(p => p.CommodityCode).ThenBy(p => p.DeliveryPeriod)
                .ToListAsync(ct);

            var items = new List<PositionRef>(positions.Count);
            foreach (var position in positions)
            {
                var curvePoint = await curvePointDirectory.FindAsync(position.CommodityCode, position.DeliveryPeriod, ct);
                decimal? mtm = curvePoint is null
                    ? null
                    : MtmCalculator.Calculate(curvePoint.Price, position.WeightedAvgPrice, position.NetVolume);
                items.Add(new PositionRef(
                    position.CommodityCode, position.DeliveryPeriod, position.NetVolume,
                    position.WeightedAvgPrice, curvePoint?.Price, mtm));
            }

            return Results.Ok(new PositionsResponse(items, SumOrNull(items.Select(i => i.MtmAmount))));
        })
        .WithTags("Risk")
        .Produces<PositionsResponse>();

        app.MapGet("/valuations/latest", async (RiskDbContext db, CancellationToken ct) =>
        {
            // Every row of a run shares RunId and ValuedAt (EodRun), so the
            // newest row identifies the newest run.
            var latest = await db.ValuationSnapshots.AsNoTracking()
                .OrderByDescending(v => v.ValuedAt)
                .Select(v => new { v.RunId, v.ValuedAt })
                .FirstOrDefaultAsync(ct);
            if (latest is null)
                return Results.Ok(new ValuationRunResponse(null, null, [], 0m));

            var items = await db.ValuationSnapshots.AsNoTracking()
                .Where(v => v.RunId == latest.RunId)
                .OrderBy(v => v.CommodityCode).ThenBy(v => v.DeliveryPeriod)
                .Select(v => new ValuationEntry(
                    v.CommodityCode, v.DeliveryPeriod, v.NetVolume, v.WeightedAvgPrice,
                    v.CurvePrice, v.CurvePublishedAt, v.MtmAmount))
                .ToListAsync(ct);

            return Results.Ok(new ValuationRunResponse(latest.RunId, latest.ValuedAt, items, items.Sum(i => i.MtmAmount)));
        })
        .WithTags("Risk")
        .Produces<ValuationRunResponse>();

        app.MapGet("/stress-results/latest", async (RiskDbContext db, CancellationToken ct) =>
        {
            var latest = await db.StressScenarioResults.AsNoTracking()
                .OrderByDescending(s => s.ValuedAt)
                .Select(s => new { s.RunId, s.ValuedAt })
                .FirstOrDefaultAsync(ct);
            if (latest is null)
                return Results.Ok(new StressRunResponse(null, null, 0m, []));

            var runId = latest.RunId;
            // Base MtM from the same run's valuation snapshots — never from a
            // different run, or the comparison would mix two curves.
            var baseTotal = await db.ValuationSnapshots.AsNoTracking()
                .Where(v => v.RunId == runId)
                .SumAsync(v => v.MtmAmount, ct);

            var rows = await db.StressScenarioResults.AsNoTracking()
                .Where(s => s.RunId == runId)
                .OrderBy(s => s.ScenarioType).ThenByDescending(s => s.ShockPercentage)
                .ThenBy(s => s.CommodityCode).ThenBy(s => s.DeliveryPeriod)
                .ToListAsync(ct);

            var scenarios = rows
                .GroupBy(s => new { s.ScenarioType, s.ShockPercentage })
                .Select(g => new StressScenarioGroup(
                    g.Key.ScenarioType,
                    g.Key.ShockPercentage,
                    g.Select(s => new StressEntry(s.CommodityCode, s.DeliveryPeriod, s.NetVolume, s.ShockedPrice, s.MtmAmount)).ToList(),
                    g.Sum(s => s.MtmAmount)))
                .ToList();

            return Results.Ok(new StressRunResponse(runId, latest.ValuedAt, baseTotal, scenarios));
        })
        .WithTags("Risk")
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice, IdentityRoles.BackOffice))
        .Produces<StressRunResponse>();

        // Context for the four-eyes decision (spec §4.5): the limit comes
        // from Reference through its Contracts; exposure is computed exactly
        // as the credit check computes it.
        app.MapGet("/counterparties/{id:guid}/exposure", async (
            Guid id,
            RiskDbContext db,
            ICounterpartyDirectory counterparties,
            CreditExposureCalculator exposureCalculator,
            CancellationToken ct) =>
        {
            var counterparty = await counterparties.FindAsync(id, ct);
            if (counterparty is null)
                return Results.NotFound();

            var reservations = await db.CreditReservations.AsNoTracking().HoldingCredit(id).ToListAsync(ct);
            var exposure = await exposureCalculator.ForReservationsAsync(reservations, ct);
            return Results.Ok(new CounterpartyExposure(id, counterparty.CreditLimit, exposure));
        })
        .WithTags("Risk")
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice, IdentityRoles.FrontOffice))
        .Produces<CounterpartyExposure>()
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static decimal? SumOrNull(IEnumerable<decimal?> amounts)
    {
        var known = amounts.Where(a => a.HasValue).Select(a => a!.Value).ToList();
        return known.Count == 0 ? null : known.Sum();
    }
}

internal sealed record PositionsResponse(IReadOnlyList<PositionRef> Items, decimal? TotalMtm);

internal sealed record ValuationEntry(
    string CommodityCode,
    string DeliveryPeriod,
    decimal NetVolume,
    decimal WeightedAvgPrice,
    decimal CurvePrice,
    DateTimeOffset CurvePublishedAt,
    decimal MtmAmount);

internal sealed record ValuationRunResponse(Guid? RunId, DateTimeOffset? RunAt, IReadOnlyList<ValuationEntry> Items, decimal TotalMtm);

internal sealed record StressEntry(string CommodityCode, string DeliveryPeriod, decimal NetVolume, decimal ShockedPrice, decimal MtmAmount);

internal sealed record StressScenarioGroup(
    ScenarioType ScenarioType,
    decimal ShockPercentage,
    IReadOnlyList<StressEntry> Items,
    decimal TotalMtm);

internal sealed record StressRunResponse(Guid? RunId, DateTimeOffset? RunAt, decimal BaseTotalMtm, IReadOnlyList<StressScenarioGroup> Scenarios);

internal sealed record CounterpartyExposure(Guid CounterpartyId, decimal CreditLimit, decimal CurrentExposure);
