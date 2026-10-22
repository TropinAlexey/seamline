using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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

        return app;
    }
}

internal sealed record CreateCounterpartyRequest(string Name, decimal CreditLimit);
