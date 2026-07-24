using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Risk.Contracts;
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

        group.MapGet("/{id:guid}", async (Guid id, TradingDbContext db, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            return trade is null
                ? Results.NotFound()
                : Results.Ok(new { trade.Id, trade.State, trade.Volume, trade.Price, trade.Version });
        });

        group.MapPost("/{id:guid}/submit", async (
            Guid id,
            TradingDbContext db,
            ITenantContext tenant,
            ICreditReservationService creditReservationService,
            IPublishEndpoint publisher,
            CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            var submitHistory = trade.Submit("trader", "Submitted for credit check");
            db.TradeHistory.Add(submitHistory);

            var reservation = await creditReservationService.TryReserveAsync(
                tenant.TenantId.Value, trade.CounterpartyId, trade.Id, trade.Notional, ct);

            if (reservation.Outcome == CreditReservationOutcome.Reserved)
            {
                var (activateHistory, activated) = trade.Activate("system", "Within credit limit — approved automatically");
                db.TradeHistory.Add(activateHistory);
                await publisher.Publish(activated, ct);
            }
            else
            {
                var pendingHistory = trade.EnterCreditPending("system", "Credit limit breached — awaiting risk approval");
                db.TradeHistory.Add(pendingHistory);
                await publisher.Publish(new TradeApprovalRequested(trade.Id, tenant.TenantId.Value, trade.CounterpartyId, trade.Notional), ct);
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(new { trade.Id, trade.State, reservation.Outcome });
        });

        group.MapPost("/{id:guid}/approve", async (Guid id, HttpRequest request, TradingDbContext db, IPublishEndpoint publisher, CancellationToken ct) =>
        {
            if (!IsRiskActor(request))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            await publisher.Publish(new TradeApprovalGranted(id), ct);
            // UseBusOutbox() buffers every Publish pending some DbContext's
            // SaveChanges in this scope — with no DbContext touched at all,
            // there's nothing to flush the buffer, and the message is
            // silently lost when the scope ends. There's no entity write
            // here, but this call still has to happen.
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        });

        group.MapPost("/{id:guid}/reject", async (Guid id, HttpRequest request, TradingDbContext db, IPublishEndpoint publisher, CancellationToken ct) =>
        {
            if (!IsRiskActor(request))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            await publisher.Publish(new TradeApprovalDenied(id), ct);
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        });

        group.MapPost("/{id:guid}/cancel", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            // CreditPending has a live saga holding the approval timeout —
            // cancellation has to go through it (same reasoning as
            // approve/reject), not mutate the Trade directly here. Draft
            // and Submitted never started a saga, so those cancel inline.
            if (trade.State == TradeState.CreditPending)
            {
                await publisher.Publish(new TradeCancelRequested(id), ct);
                await db.SaveChangesAsync(ct); // flush the outbox — see the comment on /approve
                return Results.Accepted();
            }

            var history = trade.Cancel("trader", "Cancelled by trader");
            db.TradeHistory.Add(history);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { trade.Id, trade.State });
        });

        group.MapPost("/{id:guid}/amend", async (Guid id, AmendTradeRequest request, TradingDbContext db, IPublishEndpoint publisher, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            var (history, amended) = trade.Amend("trader", request.Reason, request.Volume, request.Price);
            db.TradeHistory.Add(history);
            await publisher.Publish(amended, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { trade.Id, trade.State, trade.Volume, trade.Price });
        });

        group.MapPost("/{id:guid}/deliver", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            var (history, delivered) = trade.Deliver("trader", "Delivered");
            db.TradeHistory.Add(history);
            await publisher.Publish(delivered, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { trade.Id, trade.State });
        });

        return app;
    }

    // Stand-in for real authorization — there is no Identity module yet.
    // Same pattern as the tenant header: unverified, but structurally
    // demonstrates segregation of duties (the approver must not be the
    // trader who booked the trade) in the code path, not just in an ADR.
    private static bool IsRiskActor(HttpRequest request) =>
        request.Headers.TryGetValue("X-Actor-Role", out var role) && role == "risk";
}

internal sealed record CreateTradeRequest(
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    decimal Volume,
    decimal Price,
    Guid CounterpartyId);

internal sealed record AmendTradeRequest(decimal Volume, decimal Price, string Reason);
