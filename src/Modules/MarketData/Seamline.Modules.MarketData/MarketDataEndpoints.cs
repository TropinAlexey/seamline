using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

        MapIndexEndpoints(app);

        return app;
    }

    // Price indices and their daily fixings (ADR-0035, spec 03).
    private static void MapIndexEndpoints(IEndpointRouteBuilder app)
    {
        var indices = app.MapGroup("/indices").WithTags("MarketData");

        indices.MapGet("/", async (MarketDataDbContext db, CancellationToken ct) =>
        {
            var items = await db.PriceIndices
                .OrderBy(i => i.Code)
                .Select(i => new PriceIndexEntry(i.Code, i.CommodityCode, i.Name))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .Produces<List<PriceIndexEntry>>();

        indices.MapGet("/{code}/fixings", async (
            string code,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            MarketDataDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.PriceIndices.AnyAsync(i => i.Code == code, ct))
                return Results.NotFound();

            var query = db.IndexFixings.Where(f => f.IndexCode == code);
            if (from is not null)
                query = query.Where(f => f.FixingDate >= from);
            if (to is not null)
                query = query.Where(f => f.FixingDate <= to);
            var items = await query
                .OrderBy(f => f.FixingDate)
                .Select(f => new IndexFixingEntry(f.FixingDate, f.Value, f.PublishedAt, f.PublishedBy))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .Produces<List<IndexFixingEntry>>()
        .Produces(StatusCodes.Status404NotFound);

        // A fixing is never overwritten: a second value for the same day is a
        // 409, and corrections are out of scope (ADR-0035). Publishing is a
        // market data action for middle office, like a curve point.
        indices.MapPost("/{code}/fixings", async (
            string code,
            PublishFixingRequest request,
            MarketDataDbContext db,
            ITenantContext tenant,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (!await db.PriceIndices.AnyAsync(i => i.Code == code, ct))
                return Results.NotFound();
            if (await db.IndexFixings.AnyAsync(f => f.IndexCode == code && f.FixingDate == request.FixingDate, ct))
                return FixingExists(code, request.FixingDate);

            var publishedBy = user.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            var fixing = IndexFixing.Publish(tenant.TenantId, code, request.FixingDate, request.Value, publishedBy);
            db.IndexFixings.Add(fixing);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Two publishers raced past the check above.
                return FixingExists(code, request.FixingDate);
            }

            return Results.Created($"/indices/{code}/fixings",
                new IndexFixingEntry(fixing.FixingDate, fixing.Value, fixing.PublishedAt, fixing.PublishedBy));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice))
        .Produces<IndexFixingEntry>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }

    private static IResult FixingExists(string code, DateOnly date) =>
        Results.Conflict(new ErrorResponse($"{code} already has a fixing for {date:yyyy-MM-dd}; published fixings are never overwritten."));
}

internal sealed record SetCurvePointRequest(string CommodityCode, string DeliveryPeriod, decimal Price);

internal sealed record CurvePointEntry(string CommodityCode, string DeliveryPeriod, decimal Price, DateTimeOffset PublishedAt);

internal sealed record PriceIndexEntry(string Code, string CommodityCode, string Name);

internal sealed record IndexFixingEntry(DateOnly FixingDate, decimal Value, DateTimeOffset PublishedAt, string PublishedBy);

internal sealed record PublishFixingRequest(DateOnly FixingDate, decimal Value);
