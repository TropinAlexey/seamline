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
