using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Identity.Contracts;

namespace Seamline.Modules.Settlement.Internal;

public static class SettlementEndpoints
{
    public static IEndpointRouteBuilder MapSettlementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices", async (SettlementDbContext db, CancellationToken ct) =>
        {
            var invoices = await db.Invoices
                .OrderByDescending(i => i.IssuedAt)
                .Select(i => new InvoiceEntry(i.TradeId, i.CounterpartyId, i.Amount, i.IssuedAt))
                .ToListAsync(ct);
            return Results.Ok(invoices);
        })
        .WithTags("Settlement")
        .Produces<List<InvoiceEntry>>()
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.BackOffice));

        return app;
    }
}

internal sealed record InvoiceEntry(Guid TradeId, Guid CounterpartyId, decimal Amount, DateTimeOffset IssuedAt);
