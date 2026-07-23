using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

public static class TradingEndpoints
{
    public static IEndpointRouteBuilder MapTradingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/trades").WithTags("Trading");

        group.MapPost("/", async (CreateTradeRequest request, TradingDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            var trade = Trade.CreateDraft(
                tenant.TenantId,
                request.CommodityCode,
                request.DeliveryPeriod,
                request.Direction,
                request.Volume,
                request.Price,
                request.CounterpartyId);

            db.Trades.Add(trade);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/trades/{trade.Id}", new { trade.Id });
        });

        group.MapPost("/{id:guid}/confirm", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            var tradeConfirmed = trade.Confirm();

            // Written to the outbox table in the same SaveChanges transaction as
            // the trade's state change; MassTransit's bus outbox dispatches it
            // afterwards. Publish never races with the state change being lost.
            await publisher.Publish(tradeConfirmed, ct);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        return app;
    }
}

internal sealed record CreateTradeRequest(
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    decimal Volume,
    decimal Price,
    Guid CounterpartyId);
