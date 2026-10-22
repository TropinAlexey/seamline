using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Identity.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.MarketData.Internal;

public static class MarketDataEndpoints
{
    public static IEndpointRouteBuilder MapMarketDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/curve-points").WithTags("MarketData");

        // Upsert: publishing a curve point twice for the same (commodity,
        // period) updates the price rather than erroring — this is a
        // market data feed, not an audited transaction (see PriceCurvePoint).
        group.MapPost("/", async (SetCurvePointRequest request, MarketDataDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            var existing = await db.PriceCurvePoints.FirstOrDefaultAsync(
                p => p.CommodityCode == request.CommodityCode && p.DeliveryPeriod == request.DeliveryPeriod, ct);

            if (existing is null)
            {
                var curvePoint = PriceCurvePoint.Create(tenant.TenantId, request.CommodityCode, request.DeliveryPeriod, request.Price);
                db.PriceCurvePoints.Add(curvePoint);
            }
            else
            {
                existing.UpdatePrice(request.Price);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok();
        })
        // Moving a price moves every position's live MtM — a risk (MO)
        // action, not something a trader should be able to do to their own
        // book. The scheduled curve import writes through the worker, not
        // this endpoint, so it is unaffected (ADR-0030).
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice))
        .Produces(StatusCodes.Status200OK)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/", async (MarketDataDbContext db, CancellationToken ct) =>
        {
            var curvePoints = await db.PriceCurvePoints
                .OrderBy(p => p.CommodityCode).ThenBy(p => p.DeliveryPeriod)
                .Select(p => new CurvePointEntry(p.CommodityCode, p.DeliveryPeriod, p.Price, p.PublishedAt))
                .ToListAsync(ct);
            return Results.Ok(curvePoints);
        })
        .Produces<List<CurvePointEntry>>();

        return app;
    }
}

internal sealed record SetCurvePointRequest(string CommodityCode, string DeliveryPeriod, decimal Price);

internal sealed record CurvePointEntry(string CommodityCode, string DeliveryPeriod, decimal Price, DateTimeOffset PublishedAt);
