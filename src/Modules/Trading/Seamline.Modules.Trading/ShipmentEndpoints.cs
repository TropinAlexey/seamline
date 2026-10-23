using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Risk.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

// Cargo shipments (ADR-0034, ADR-0037). Operations is back office's job in
// this demo; front and middle office read along.
internal static class ShipmentEndpoints
{
    public static void MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/shipments").WithTags("Trading");

        // Every Active or later cargo trade, with its shipment if one has
        // been nominated — "awaiting nomination" is an Active cargo trade
        // with no shipment row yet.
        group.MapGet("/", async (TradingDbContext db, CancellationToken ct) =>
        {
            var trades = await db.Trades.AsNoTracking()
                .Where(t => t.CargoTerms != null && (t.State == TradeState.Active || t.State == TradeState.Delivered))
                .OrderBy(t => t.CargoTerms!.LaycanFrom).ThenBy(t => t.Id)
                .ToListAsync(ct);
            var tradeIds = trades.Select(t => t.Id).ToList();
            var shipments = await db.Shipments.AsNoTracking()
                .Where(s => tradeIds.Contains(s.TradeId))
                .ToDictionaryAsync(s => s.TradeId, ct);

            return Results.Ok(trades.Select(t => ShipmentEntry.From(t, shipments.GetValueOrDefault(t.Id))).ToList());
        })
        .Produces<List<ShipmentEntry>>();

        group.MapGet("/{tradeId:guid}", async (Guid tradeId, TradingDbContext db, CancellationToken ct) =>
        {
            var trade = await db.Trades.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tradeId && t.CargoTerms != null, ct);
            if (trade is null)
                return Results.NotFound();
            var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.TradeId == tradeId, ct);
            return Results.Ok(ShipmentEntry.From(trade, shipment));
        })
        .Produces<ShipmentEntry>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{tradeId:guid}/history", async (Guid tradeId, TradingDbContext db, CancellationToken ct) =>
        {
            var history = await db.ShipmentHistory.AsNoTracking()
                .Where(h => h.TradeId == tradeId)
                .OrderBy(h => h.Version)
                .Select(h => new ShipmentHistoryEntry(h.Version, h.ValidFrom, h.ChangedBy, h.ChangeReason, h.State))
                .ToListAsync(ct);
            return Results.Ok(history);
        })
        .Produces<List<ShipmentHistoryEntry>>();

        group.MapPost("/{tradeId:guid}/nominate", async (
            Guid tradeId, NominateVesselRequest request, TradingDbContext db, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == tradeId, ct);
            if (trade is null)
                return Results.NotFound();
            if (await db.Shipments.AnyAsync(s => s.TradeId == tradeId, ct))
                throw new DomainRuleViolationException($"Trade {tradeId} already has a nominated vessel.");

            var (shipment, history, nominated) = Shipment.Nominate(
                trade, request.VesselName, request.Imo, request.Eta, TradingEndpoints.ActorName(user));
            db.Shipments.Add(shipment);
            db.ShipmentHistory.Add(history);
            await publisher.Publish(nominated, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(ShipmentEntry.From(trade, shipment));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.BackOffice))
        .Produces<ShipmentEntry>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        // Loading is delivery under FOB: the shipment's B/L and the trade's
        // Active -> Delivered commit in one transaction and TradeDelivered
        // goes out through the same outbox (ADR-0037).
        group.MapPost("/{tradeId:guid}/load", async (
            Guid tradeId, RecordLoadingRequest request, TradingDbContext db, IFormulaPricingService formulaPricing,
            IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            await using var transaction = await TradeRowLock.AcquireAsync(db, tradeId, ct);
            var (trade, shipment) = await LoadAsync(db, tradeId, ct);
            if (trade is null || shipment is null)
                return Results.NotFound();

            // An unfixed index trade is invoiced provisionally at today's
            // expected price, which Risk computes (ADR-0035).
            var expectedPrice = trade.Price is null
                ? (await formulaPricing.PriceFormulaAsync(trade, ct))?.ExpectedPrice
                : null;

            var actor = TradingEndpoints.ActorName(user);
            var shipmentHistory = shipment.RecordLoading(request.BlQuantity, request.BlDate, actor);
            var (tradeHistory, delivered) = trade.DeliverLoaded(actor, request.BlQuantity, request.BlDate, expectedPrice);
            db.ShipmentHistory.Add(shipmentHistory);
            db.TradeHistory.Add(tradeHistory);
            await publisher.Publish(delivered, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Ok(ShipmentEntry.From(trade, shipment));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.BackOffice))
        .Produces<ShipmentEntry>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/{tradeId:guid}/certify", async (
            Guid tradeId, RecordCertificateRequest request, TradingDbContext db, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            // Serialized with the price fixing (TradeRowLock): whichever of the
            // two commits second sees the first and carries both halves.
            await using var transaction = await TradeRowLock.AcquireAsync(db, tradeId, ct);
            var (trade, shipment) = await LoadAsync(db, tradeId, ct);
            if (trade is null || shipment is null)
                return Results.NotFound();

            var (history, outcome) = shipment.RecordCertificate(
                trade.CargoTerms!, request.MeasuredNarKcalKg, request.SulphurPct, request.AshPct, TradingEndpoints.ActorName(user), trade.Price);
            db.ShipmentHistory.Add(history);
            await publisher.Publish(outcome, outcome.GetType(), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Ok(ShipmentEntry.From(trade, shipment));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.BackOffice))
        .Produces<ShipmentEntry>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }

    private static async Task<(Trade? Trade, Shipment? Shipment)> LoadAsync(TradingDbContext db, Guid tradeId, CancellationToken ct)
    {
        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == tradeId, ct);
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.TradeId == tradeId, ct);
        return (trade, shipment);
    }
}

internal sealed record NominateVesselRequest(string VesselName, string Imo, DateOnly Eta);

internal sealed record RecordLoadingRequest(decimal BlQuantity, DateOnly BlDate);

internal sealed record RecordCertificateRequest(decimal MeasuredNarKcalKg, decimal SulphurPct, decimal AshPct);

internal sealed record ShipmentHistoryEntry(int Version, DateTimeOffset ValidFrom, string ChangedBy, string ChangeReason, ShipmentState State);

// Tolerance band computed server-side so the UI never does arithmetic on
// quantities (ADR-0030). State is null while the vessel is not nominated.
internal sealed record ShipmentEntry(
    Guid TradeId,
    string CommodityCode,
    string DeliveryPeriod,
    decimal ContractVolume,
    decimal ToleranceMin,
    decimal ToleranceMax,
    DateOnly LaycanFrom,
    DateOnly LaycanTo,
    ShipmentState? State,
    string? VesselName,
    string? Imo,
    DateOnly? Eta,
    decimal? BlQuantity,
    DateOnly? BlDate,
    decimal? MeasuredNarKcalKg,
    decimal? SulphurPct,
    decimal? AshPct)
{
    public static ShipmentEntry From(Trade trade, Shipment? shipment)
    {
        var terms = trade.CargoTerms!;
        var (min, max) = terms.ToleranceBand(trade.Volume);
        return new ShipmentEntry(
            trade.Id, trade.CommodityCode, trade.DeliveryPeriod, trade.Volume, min, max, terms.LaycanFrom, terms.LaycanTo,
            shipment?.State, shipment?.VesselName, shipment?.Imo, shipment?.Eta, shipment?.BlQuantity, shipment?.BlDate,
            shipment?.MeasuredNarKcalKg, shipment?.SulphurPct, shipment?.AshPct);
    }
}
