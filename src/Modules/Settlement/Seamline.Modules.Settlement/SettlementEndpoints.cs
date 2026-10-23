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
        app.MapGet("/invoices", async (Guid? tradeId, SettlementDbContext db, CancellationToken ct) =>
        {
            var query = db.Invoices.AsQueryable();
            if (tradeId is not null)
                query = query.Where(i => i.TradeId == tradeId);
            var invoices = await query
                .OrderByDescending(i => i.IssuedAt)
                .Select(i => new InvoiceEntry(i.TradeId, i.CounterpartyId, i.Amount, i.IssuedAt, i.Kind, i.Quantity, i.UnitPrice, i.Balance))
                .ToListAsync(ct);
            return Results.Ok(invoices);
        })
        .WithTags("Settlement")
        .Produces<List<InvoiceEntry>>()
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.BackOffice));

        return app;
    }
}

internal sealed record InvoiceEntry(
    Guid TradeId,
    Guid CounterpartyId,
    decimal Amount,
    DateTimeOffset IssuedAt,
    InvoiceKind Kind,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? Balance);
