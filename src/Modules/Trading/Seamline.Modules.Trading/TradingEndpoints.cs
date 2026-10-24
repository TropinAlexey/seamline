using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.MarketData.Contracts;
using Seamline.Modules.Reference.Contracts;
using Seamline.Modules.Risk.Contracts;
using Seamline.Modules.Trading.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Internal;

public static class TradingEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapTradingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/trades").WithTags("Trading");

        group.MapPost("/", async (
            CreateTradeRequest request,
            TradingDbContext db,
            ITenantContext tenant,
            ICounterpartyDirectory counterparties,
            ICommodityDirectory commodities,
            IIndexFixingDirectory indices,
            IPublishEndpoint publisher,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            // Commodity codes are master data (ADR-0033): a typo would
            // otherwise open a position nothing can ever price.
            var commodity = await commodities.FindAsync(request.CommodityCode, ct);
            if (commodity is null)
                return Results.BadRequest(new ErrorResponse($"Commodity {request.CommodityCode} not found."));

            // Cargo commodities need cargo terms, flow commodities must not
            // have them (ADR-0034). The quality spec is snapshotted from
            // master data at booking.
            CargoTerms? cargoTerms = null;
            if (commodity.DeliveryModel == DeliveryModel.Cargo)
            {
                if (request.CargoTerms is not { } terms)
                    return Results.BadRequest(new ErrorResponse($"{commodity.Code} is a cargo commodity: tolerance and laycan are required."));
                var spec = commodity.QualitySpec!;
                cargoTerms = new CargoTerms(terms.TolerancePct, terms.LaycanFrom, terms.LaycanTo,
                    spec.BaseNarKcalKg, spec.RejectBelowNarKcalKg, spec.MaxSulphurPct, spec.MaxAshPct);
            }
            else if (request.CargoTerms is not null)
            {
                return Results.BadRequest(new ErrorResponse($"{commodity.Code} is a flow commodity: cargo terms do not apply."));
            }

            // Index pricing: cargo commodities only, against an index of the
            // same commodity (ADR-0035). Exactly one of price and formula.
            IndexPricingTerms? pricing = null;
            if (request.IndexPricing is { } formula)
            {
                if (commodity.DeliveryModel != DeliveryModel.Cargo)
                    return Results.BadRequest(new ErrorResponse(
                        $"{commodity.Code} is a flow commodity: index pricing is offered on cargo commodities only."));
                var index = await indices.FindIndexAsync(formula.IndexCode, ct);
                if (index is null || index.CommodityCode != commodity.Code)
                    return Results.BadRequest(new ErrorResponse($"No {commodity.Code} index {formula.IndexCode}."));
                pricing = new IndexPricingTerms(index.Code, formula.WindowFrom, formula.WindowTo, formula.Premium);
            }

            // Checked here, not left to the credit check at submit time: a
            // draft pointing at a counterparty that doesn't exist would
            // otherwise be accepted and then fail every submit.
            if (await counterparties.FindAsync(request.CounterpartyId, ct) is null)
                return Results.BadRequest(new ErrorResponse($"Counterparty {request.CounterpartyId} not found."));

            var trade = Trade.CreateDraft(
                tenant.TenantId,
                request.CommodityCode,
                request.DeliveryPeriod,
                request.Direction,
                request.Volume,
                request.Price,
                request.CounterpartyId,
                cargoTerms,
                pricing);

            db.Trades.Add(trade);
            var actor = user.ActorName();
            db.TradeHistory.Add(TradeHistory.CreateSnapshot(trade, actor, "Created"));
            await publisher.Publish(new TradeCreated(trade.Id, tenant.TenantId.Value, actor, "Created"), ct);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/trades/{trade.Id}", new TradeCreatedResponse(trade.Id));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice))
        .Produces<TradeCreatedResponse>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/", async (
            [AsParameters] TradeListQuery query,
            TradingDbContext db,
            CancellationToken ct) =>
        {
            var page = Math.Max(query.Page ?? 1, 1);
            var pageSize = Math.Clamp(query.PageSize ?? 25, 1, MaxPageSize);

            var trades = db.Trades.AsNoTracking();
            if (query.State is { Length: > 0 } states)
                trades = trades.Where(t => states.Contains(t.State));
            if (!string.IsNullOrWhiteSpace(query.Commodity))
                trades = trades.Where(t => t.CommodityCode == query.Commodity);
            if (!string.IsNullOrWhiteSpace(query.Period))
                trades = trades.Where(t => t.DeliveryPeriod == query.Period);

            var total = await trades.CountAsync(ct);
            var items = await trades
                .OrderByDescending(t => t.LastChangedAt).ThenBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Results.Ok(new TradePage(items.Select(TradeSummary.From).ToList(), total));
        })
        .Produces<TradePage>();

        group.MapGet("/{id:guid}", async (Guid id, TradingDbContext db, CancellationToken ct) =>
        {
            var trade = await db.Trades.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            return trade is null
                ? Results.NotFound()
                : Results.Ok(TradeSummary.From(trade));
        })
        .Produces<TradeSummary>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/history", async (Guid id, TradingDbContext db, CancellationToken ct) =>
        {
            var history = await db.TradeHistory.AsNoTracking()
                .Where(h => h.TradeId == id)
                .OrderBy(h => h.Version)
                .ToListAsync(ct);

            return history.Count == 0
                ? Results.NotFound()
                : Results.Ok(history.Select(TradeHistoryEntry.From).ToList());
        })
        .Produces<List<TradeHistoryEntry>>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/remit-reports", async (Guid id, TradingDbContext db, CancellationToken ct) =>
        {
            var reports = await db.RemitReports.AsNoTracking()
                .Where(r => r.TradeId == id)
                .OrderBy(r => r.Version)
                .Select(r => new RemitReportEntry(r.Version, r.Action, r.AckId, r.SubmittedAt))
                .ToListAsync(ct);

            return Results.Ok(reports);
        })
        .Produces<List<RemitReportEntry>>();

        group.MapPost("/{id:guid}/submit", async (
            Guid id,
            TradingDbContext db,
            ITenantContext tenant,
            ICreditReservationService creditReservationService,
            IFormulaPricingService formulaPricing,
            IPublishEndpoint publisher,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            // An unfixed index trade is credit-checked at its expected price,
            // which needs at least a forward or a fixing (spec 03, rule 6).
            var notional = trade.Notional;
            if (trade.Price is null && await formulaPricing.PriceFormulaAsync(trade, ct) is { } priced)
            {
                notional = priced.ExpectedNotional;
                if (notional is null)
                    return Results.BadRequest(new ErrorResponse(
                        $"No forward curve point for {trade.CommodityCode} {trade.DeliveryPeriod} and no fixing yet: an index-priced trade cannot be credit-checked."));
                if (notional <= 0)
                    return Results.BadRequest(new ErrorResponse(
                        $"The expected price of {trade.Id} is {priced.ExpectedPrice:0.0000}, not positive: the premium is deeper than the index."));
            }

            var actor = user.ActorName();
            var submitHistory = trade.Submit(actor, "Submitted for credit check");
            db.TradeHistory.Add(submitHistory);
            await publisher.Publish(new TradeSubmitted(trade.Id, tenant.TenantId.Value, actor, "Submitted for credit check"), ct);

            var signedVolume = trade.Direction == TradeDirection.Buy ? trade.Volume : -trade.Volume;
            var reservation = await creditReservationService.TryReserveAsync(
                tenant.TenantId.Value, trade.CounterpartyId, trade.Id,
                trade.CommodityCode, trade.DeliveryPeriod, signedVolume, trade.Price, trade.Pricing, ct);

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
                await publisher.Publish(new TradeApprovalRequested(trade.Id, tenant.TenantId.Value, trade.CounterpartyId, notional!.Value), ct);
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(new TradeSubmitResponse(trade.Id, trade.State, reservation.Outcome));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice))
        .Produces<TradeSubmitResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/approve", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();
            if (trade.State != TradeState.CreditPending)
                return Results.Conflict(new ErrorResponse($"Trade {id} must be in CreditPending state to approve, was {trade.State}."));

            await publisher.Publish(new TradeApprovalGranted(id, user.ActorName()), ct);
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice))
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/reject", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();
            if (trade.State != TradeState.CreditPending)
                return Results.Conflict(new ErrorResponse($"Trade {id} must be in CreditPending state to reject, was {trade.State}."));

            await publisher.Publish(new TradeApprovalDenied(id, user.ActorName()), ct);
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.MiddleOffice))
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

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
                await publisher.Publish(new TradeCancelRequested(id, user.ActorName()), ct);
                await db.SaveChangesAsync(ct);
                return Results.Accepted();
            }

            var actor = user.ActorName();
            var history = trade.Cancel(actor, "Cancelled by trader");
            db.TradeHistory.Add(history);
            await publisher.Publish(new TradeCancelled(trade.Id, trade.TenantId.Value, actor, "Cancelled by trader"), ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new TradeStateResponse(trade.Id, trade.State));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice))
        .Produces<TradeStateResponse>()
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/amend", async (Guid id, AmendTradeRequest request, TradingDbContext db, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            var (history, amended) = trade.Amend(user.ActorName(), request.Reason, request.Volume, request.Price);
            db.TradeHistory.Add(history);
            await publisher.Publish(amended, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new TradeAmendResponse(trade.Id, trade.State, trade.Volume, trade.Price));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice))
        .Produces<TradeAmendResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/deliver", async (Guid id, TradingDbContext db, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();

            var (history, delivered) = trade.Deliver(user.ActorName(), "Delivered");
            db.TradeHistory.Add(history);
            await publisher.Publish(delivered, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new TradeStateResponse(trade.Id, trade.State));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.FrontOffice))
        .Produces<TradeStateResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        // Where an index-priced trade stands: k of N days fixed, expected
        // price, fixed and unfixed quantity (spec 03). Risk computes it; the
        // route lives here because the terms are the trade's.
        group.MapGet("/{id:guid}/pricing", async (Guid id, TradingDbContext db, IFormulaPricingService formulaPricing, CancellationToken ct) =>
        {
            var trade = await db.Trades.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null || await formulaPricing.PriceFormulaAsync(trade, ct) is not { } priced)
                return Results.NotFound();

            return Results.Ok(TradePricing.From(trade.Pricing!, priced, trade.Price));
        })
        .Produces<TradePricing>()
        .Produces(StatusCodes.Status404NotFound);

        // The window has fully fixed: back office confirms the final price
        // and Trading writes it (ADR-0038). One transaction: the new trade
        // version and TradePriceFixed through the outbox. If the cargo is
        // already certified, the event carries the certificate so the final
        // invoice can follow.
        group.MapPost("/{id:guid}/fix-price", async (
            Guid id, TradingDbContext db, IFormulaPricingService formulaPricing, IPublishEndpoint publisher, ClaimsPrincipal user, CancellationToken ct) =>
        {
            // Serialized with the certificate (TradeRowLock): whichever of the
            // two commits second sees the first and carries both halves.
            await using var transaction = await TradeRowLock.AcquireAsync(db, id, ct);
            var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trade is null)
                return Results.NotFound();
            if (await formulaPricing.PriceFormulaAsync(trade, ct) is not { } priced)
                return Results.Conflict(new ErrorResponse($"Trade {id} is fixed-price; there is no window to fix."));
            if (priced.FinalPrice is not { } finalPrice)
                return Results.Conflict(new ErrorResponse(
                    $"{priced.FixedDays} of {priced.PricingDays} pricing days of {trade.Pricing!.IndexCode} have fixed; the price can be fixed when all have."));

            var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.TradeId == id, ct);
            var certificate = shipment?.CertifiedQuality(trade.CargoTerms!);

            var (history, priceFixed) = trade.FixPrice(user.ActorName(), finalPrice, priced.AverageFixing!.Value, certificate);
            db.TradeHistory.Add(history);
            await publisher.Publish(priceFixed, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Ok(TradeSummary.From(trade));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.BackOffice))
        .Produces<TradeSummary>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        app.MapShipmentEndpoints();

        return app;
    }
}

internal sealed record CreateTradeRequest(
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    decimal Volume,
    decimal? Price,
    Guid CounterpartyId,
    CargoTermsRequest? CargoTerms = null,
    IndexPricingRequest? IndexPricing = null);

internal sealed record CargoTermsRequest(decimal TolerancePct, DateOnly LaycanFrom, DateOnly LaycanTo);

internal sealed record IndexPricingRequest(string IndexCode, DateOnly WindowFrom, DateOnly WindowTo, decimal Premium);

// Price is omitted when amending an index-priced trade: only its volume
// can change (ADR-0035).
internal sealed record AmendTradeRequest(decimal Volume, decimal? Price, string Reason);

internal sealed record TradePricing(
    string IndexCode,
    DateOnly WindowFrom,
    DateOnly WindowTo,
    decimal Premium,
    int PricingDays,
    int FixedDays,
    decimal? AverageFixing,
    decimal? Forward,
    decimal? ExpectedPrice,
    decimal FixedQuantity,
    decimal UnfixedQuantity,
    decimal? FormulaFinalPrice,
    decimal? ConfirmedPrice)
{
    // Two different facts (ADR-0038): FormulaFinalPrice is what the fixings
    // give once every day has fixed; ConfirmedPrice is the price back office
    // has written to the trade. The gap between them is a confirmation
    // still to do.
    public static TradePricing From(IndexPricingTerms terms, FormulaPricingRef priced, decimal? tradePrice) => new(
        terms.IndexCode, terms.WindowFrom, terms.WindowTo, terms.Premium,
        priced.PricingDays, priced.FixedDays, priced.AverageFixing, priced.Forward, priced.ExpectedPrice,
        priced.FixedQuantity, priced.UnfixedQuantity, priced.FinalPrice, tradePrice);
}

internal sealed record TradeListQuery(
    [FromQuery(Name = "state")] TradeState[]? State,
    [FromQuery(Name = "commodity")] string? Commodity,
    [FromQuery(Name = "period")] string? Period,
    [FromQuery(Name = "page")] int? Page,
    [FromQuery(Name = "pageSize")] int? PageSize);

internal sealed record TradeCreatedResponse(Guid Id);

internal sealed record TradeStateResponse(Guid Id, TradeState State);

internal sealed record TradeSubmitResponse(Guid Id, TradeState State, CreditReservationOutcome Outcome);

internal sealed record TradeAmendResponse(Guid Id, TradeState State, decimal Volume, decimal? Price);

internal sealed record TradePage(IReadOnlyList<TradeSummary> Items, int Total);

// Notional is computed here, server-side, so the UI never multiplies money
// (ADR-0030); Price and Notional are null while an index-priced trade is
// unfixed, and Pricing holds its formula. Counterparty name is deliberately absent — it lives in the
// Reference schema; the client joins it from GET /counterparties.
internal sealed record TradeSummary(
    Guid Id,
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    decimal Volume,
    decimal? Price,
    decimal? Notional,
    Guid CounterpartyId,
    TradeState State,
    int Version,
    DateTimeOffset LastChangedAt,
    decimal? ActualVolume,
    CargoTermsRef? CargoTerms,
    IndexPricingTerms? Pricing)
{
    public static TradeSummary From(Trade t) => new(
        t.Id, t.CommodityCode, t.DeliveryPeriod, t.Direction, t.Volume, t.Price, t.Notional,
        t.CounterpartyId, t.State, t.Version, t.LastChangedAt, t.ActualVolume, t.CargoTerms?.ToRef(), t.Pricing);
}

internal sealed record TradeHistoryEntry(
    int Version,
    DateTimeOffset ValidFrom,
    string ChangedBy,
    string ChangeReason,
    TradeState State,
    string CommodityCode,
    string DeliveryPeriod,
    TradeDirection Direction,
    decimal Volume,
    decimal? Price,
    decimal? Notional,
    Guid CounterpartyId,
    decimal? ActualVolume)
{
    public static TradeHistoryEntry From(TradeHistory h) => new(
        h.Version, h.ValidFrom, h.ChangedBy, h.ChangeReason, h.State, h.CommodityCode, h.DeliveryPeriod,
        h.Direction, h.Volume, h.Price, h.Volume * h.Price, h.CounterpartyId, h.ActualVolume);
}

internal sealed record RemitReportEntry(int Version, RemitAction Action, string AckId, DateTimeOffset SubmittedAt);
