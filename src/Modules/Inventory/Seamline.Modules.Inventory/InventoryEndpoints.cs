using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Identity.Contracts;
using Seamline.Modules.Reference.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Inventory.Internal;

// Gas storage (ADR-0036, spec 02). Operations — back office — registers
// contracts and records movements; everyone reads stock.
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/storage-contracts").WithTags("Inventory");

        group.MapGet("/", async (InventoryDbContext db, CancellationToken ct) =>
        {
            var contracts = await db.StorageContracts.AsNoTracking()
                .OrderBy(c => c.Facility).ThenBy(c => c.ValidFrom)
                .ToListAsync(ct);
            return Results.Ok(contracts.Select(StorageContractEntry.From).ToList());
        })
        .Produces<List<StorageContractEntry>>();

        group.MapPost("/", async (
            RegisterStorageContractRequest request, InventoryDbContext db, ITenantContext tenant,
            ICommodityDirectory commodities, CancellationToken ct) =>
        {
            // Storage holds a flow commodity measured in MWh: gas.
            var commodity = await commodities.FindAsync(request.CommodityCode, ct);
            if (commodity is not { DeliveryModel: DeliveryModel.Flow, Unit: UnitOfMeasure.MWh })
                return Results.BadRequest(new ErrorResponse($"{request.CommodityCode} is not a flow commodity in MWh that can be stored."));

            var contract = StorageContract.Register(
                tenant.TenantId, request.Facility, commodity.Code, request.Capacity,
                request.MaxInjectionPerDay, request.MaxWithdrawalPerDay, request.ValidFrom, request.ValidTo);
            db.StorageContracts.Add(contract);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/storage-contracts/{contract.Id}", StorageContractEntry.From(contract));
        })
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.BackOffice))
        .Produces<StorageContractEntry>(StatusCodes.Status201Created)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}/movements", async (Guid id, InventoryDbContext db, CancellationToken ct) =>
        {
            var movements = await db.Movements.AsNoTracking()
                .Where(m => m.StorageContractId == id)
                .OrderBy(m => m.Version)
                .Select(m => new InventoryMovementEntry(
                    m.Version, m.GasDay, m.Kind, m.Quantity, m.UnitCost, m.TradeId, m.QuantityAfter, m.WacogAfter,
                    m.Actor, m.Reason, m.RecordedAt))
                .ToListAsync(ct);
            return Results.Ok(movements);
        })
        .Produces<List<InventoryMovementEntry>>();

        group.MapPost("/{id:guid}/injections", (
                Guid id, InjectGasRequest request, InventoryDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            RecordMovementAsync(db, id, InventoryMovementKind.Injection, request.GasDay, ct,
                (contract, sameDay) => contract.Inject(request.GasDay, request.Quantity, request.UnitCost, sameDay,
                    request.TradeId, ActorName(user), request.Reason ?? "Injected")))
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.BackOffice))
        .Produces<StorageContractEntry>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/withdrawals", (
                Guid id, WithdrawGasRequest request, InventoryDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            RecordMovementAsync(db, id, InventoryMovementKind.Withdrawal, request.GasDay, ct,
                (contract, sameDay) => contract.Withdraw(request.GasDay, request.Quantity, sameDay,
                    request.TradeId, ActorName(user), request.Reason ?? "Withdrawn")))
        .RequireAuthorization(policy => policy.RequireRole(IdentityRoles.BackOffice))
        .Produces<StorageContractEntry>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    // The daily rate and capacity checks read the day's movements and the
    // balance, then write both. Two movements on one contract at the same
    // moment would each pass against the other's absence, so the contract
    // row is locked for the transaction, as Trading locks a trade.
    private static async Task<IResult> RecordMovementAsync(
        InventoryDbContext db, Guid id, InventoryMovementKind kind, DateOnly gasDay, CancellationToken ct,
        Func<StorageContract, decimal, InventoryMovement> move)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM inventory.storage_contract WHERE "Id" = {id} FOR UPDATE""", ct);

        var contract = await db.StorageContracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
            return Results.NotFound();

        var sameDay = await db.Movements
            .Where(m => m.StorageContractId == id && m.GasDay == gasDay && m.Kind == kind)
            .SumAsync(m => m.Quantity, ct);
        db.Movements.Add(move(contract, sameDay));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Results.Ok(StorageContractEntry.From(contract));
    }

    private static string ActorName(ClaimsPrincipal user) => user.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
}

internal sealed record RegisterStorageContractRequest(
    string Facility,
    string CommodityCode,
    decimal Capacity,
    decimal MaxInjectionPerDay,
    decimal MaxWithdrawalPerDay,
    DateOnly ValidFrom,
    DateOnly ValidTo);

internal sealed record InjectGasRequest(DateOnly GasDay, decimal Quantity, decimal UnitCost, Guid? TradeId = null, string? Reason = null);

internal sealed record WithdrawGasRequest(DateOnly GasDay, decimal Quantity, Guid? TradeId = null, string? Reason = null);

// Fill and the cost of the stock computed server-side, so the UI never does
// arithmetic on quantities or money (ADR-0030).
internal sealed record StorageContractEntry(
    Guid Id,
    string Facility,
    string CommodityCode,
    decimal Capacity,
    decimal MaxInjectionPerDay,
    decimal MaxWithdrawalPerDay,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    decimal Quantity,
    decimal Wacog,
    decimal FillPct,
    decimal StockCost,
    int Version)
{
    public static StorageContractEntry From(StorageContract c) => new(
        c.Id, c.Facility, c.CommodityCode, c.Capacity, c.MaxInjectionPerDay, c.MaxWithdrawalPerDay, c.ValidFrom, c.ValidTo,
        c.Quantity, c.WacogShown,
        Math.Round(c.Quantity / c.Capacity * 100m, 2, MidpointRounding.ToEven),
        Math.Round(c.Quantity * c.Wacog, 2, MidpointRounding.ToEven),
        c.Version);
}

internal sealed record InventoryMovementEntry(
    int Version,
    DateOnly GasDay,
    InventoryMovementKind Kind,
    decimal Quantity,
    decimal UnitCost,
    Guid? TradeId,
    decimal QuantityAfter,
    decimal WacogAfter,
    string Actor,
    string Reason,
    DateTimeOffset RecordedAt);
