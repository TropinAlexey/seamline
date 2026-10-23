using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Reference.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Reference.Internal;

public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/counterparties").WithTags("Reference");

        group.MapPost("/", async (CreateCounterpartyRequest request, ReferenceDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            var counterparty = Counterparty.Create(tenant.TenantId, request.Name, request.CreditLimit);
            db.Counterparties.Add(counterparty);
            await db.SaveChangesAsync(ct);

            var dto = new CounterpartyRef(counterparty.Id, counterparty.Name, counterparty.CreditLimit);
            return Results.Created($"/counterparties/{counterparty.Id}", dto);
        })
        .Produces<CounterpartyRef>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/", async (ReferenceDbContext db, CancellationToken ct) =>
        {
            var counterparties = await db.Counterparties.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CounterpartyRef(c.Id, c.Name, c.CreditLimit))
                .ToListAsync(ct);
            return Results.Ok(counterparties);
        })
        .Produces<List<CounterpartyRef>>();

        group.MapGet("/{id:guid}", async (Guid id, ICounterpartyDirectory directory, CancellationToken ct) =>
        {
            var counterparty = await directory.FindAsync(id, ct);
            return counterparty is null ? Results.NotFound() : Results.Ok(counterparty);
        })
        .Produces<CounterpartyRef>()
        .Produces(StatusCodes.Status404NotFound);

        MapCommodityEndpoints(app);

        return app;
    }

    // Commodity master data (ADR-0033). Anyone reads it; only middle office
    // defines commodities and their contract quality, the same split as
    // curve points.
    private static void MapCommodityEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/commodities").WithTags("Reference");

        group.MapGet("/", async (ReferenceDbContext db, CancellationToken ct) =>
        {
            var commodities = await db.Commodities.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct);
            return Results.Ok(commodities.Select(c => c.ToRef()).ToList());
        })
        .Produces<List<CommodityRef>>();

        group.MapPost("/", async (CreateCommodityRequest request, ReferenceDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            if (await db.Commodities.AnyAsync(c => c.Code == request.Code, ct))
                throw new DomainRuleViolationException($"Commodity {request.Code} already exists.");

            var commodity = Commodity.Create(
                tenant.TenantId, request.Code, request.Name, request.Unit, request.DeliveryModel, ToQualitySpec(request.QualitySpec));
            db.Commodities.Add(commodity);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/commodities/{commodity.Code}", commodity.ToRef());
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice))
        .Produces<CommodityRef>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPut("/{code}/quality-spec", async (string code, QualitySpecRef request, ReferenceDbContext db, CancellationToken ct) =>
        {
            var commodity = await db.Commodities.FirstOrDefaultAsync(c => c.Code == code, ct);
            if (commodity is null)
                return Results.NotFound();

            commodity.ChangeQualitySpec(ToQualitySpec(request)!);
            await db.SaveChangesAsync(ct);
            return Results.Ok(commodity.ToRef());
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice))
        .Produces<CommodityRef>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }

    private static QualitySpec? ToQualitySpec(QualitySpecRef? spec) =>
        spec is null
            ? null
            : new QualitySpec(spec.BaseNarKcalKg, spec.RejectBelowNarKcalKg, spec.MaxSulphurPct, spec.MaxAshPct);
}

internal sealed record CreateCounterpartyRequest(string Name, decimal CreditLimit);

internal sealed record CreateCommodityRequest(
    string Code, string Name, UnitOfMeasure Unit, DeliveryModel DeliveryModel, QualitySpecRef? QualitySpec);
