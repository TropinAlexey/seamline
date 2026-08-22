using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Risk.Contracts;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

public static class TradingEndpoints
{
    public static IEndpointRouteBuilder MapTradingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/trades").WithTags("Trading");

        group.MapPost("/", async (CreateTradeRequest request, TradingDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
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
        }).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice));

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
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            var actor = ActorName(user);
            var submitHistory = trade.Submit(actor, "Submitted for credit check");
            db.TradeHistory.Add(submitHistory);

            var signedVolume = trade.Direction == TradeDirection.Buy ? trade.Volume : -trade.Volume;
            var reservation = await creditReservationService.TryReserveAsync(
                tenant.TenantId.Value, trade.CounterpartyId, trade.Id,
                trade.CommodityCode, trade.DeliveryPeriod, signedVolume, trade.Price, ct);

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
        }).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice));

        group.MapPost("/{id:guid}/approve", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();
            if (trade.State != TradeState.CreditPending)
                return Results.Conflict(new { error = $"Trade {id} must be in CreditPending state to approve, was {trade.State}." });

            await publisher.Publish(new TradeApprovalGranted(id), ct);
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        }).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice));

        group.MapPost("/{id:guid}/reject", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();
            if (trade.State != TradeState.CreditPending)
                return Results.Conflict(new { error = $"Trade {id} must be in CreditPending state to reject, was {trade.State}." });

            await publisher.Publish(new TradeApprovalDenied(id), ct);
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        }).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice));

        group.MapPost("/{id:guid}/cancel", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
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
                await db.SaveChangesAsync(ct);
                return Results.Accepted();
            }

            var history = trade.Cancel(ActorName(user), "Cancelled by trader");
            db.TradeHistory.Add(history);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { trade.Id, trade.State });
        }).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice));

        group.MapPost("/{id:guid}/amend", async (Guid id, AmendTradeRequest request, TradingDbContext db, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            var (history, amended) = trade.Amend(ActorName(user), request.Reason, request.Volume, request.Price);
            db.TradeHistory.Add(history);
            await publisher.Publish(amended, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { trade.Id, trade.State, trade.Volume, trade.Price });
        }).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice));

        group.MapPost("/{id:guid}/deliver", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            var (history, delivered) = trade.Deliver(ActorName(user), "Delivered");
            db.TradeHistory.Add(history);
            await publisher.Publish(delivered, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { trade.Id, trade.State });
        }).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice));

        return app;
    }

    private static string ActorName(ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
}

internal sealed record CreateTradeRequest(
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    decimal Volume,
    decimal Price,
    Guid CounterpartyId);

internal sealed record AmendTradeRequest(decimal Volume, decimal Price, string Reason);
