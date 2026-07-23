using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        });

        group.MapGet("/{id:guid}", async (Guid id, ICounterpartyDirectory directory, CancellationToken ct) =>
        {
            var counterparty = await directory.FindAsync(id, ct);
            return counterparty is null ? Results.NotFound() : Results.Ok(counterparty);
        });

        return app;
    }
}

internal sealed record CreateCounterpartyRequest(string Name, decimal CreditLimit);
