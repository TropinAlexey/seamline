using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Seamline.Modules.Settlement.Internal;

public static class SettlementEndpoints
{
    public static IEndpointRouteBuilder MapSettlementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices", async (SettlementDbContext db, CancellationToken ct) =>
        {
            var invoices = await db.Invoices
                .Select(i => new { i.TradeId, i.CounterpartyId, i.Amount, i.IssuedAt })
                .ToListAsync(ct);
            return Results.Ok(invoices);
        }).WithTags("Settlement");

        return app;
    }
}
