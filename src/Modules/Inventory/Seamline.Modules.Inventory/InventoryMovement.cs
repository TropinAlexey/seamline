using Seamline.SharedKernel;

namespace Seamline.Modules.Inventory.Internal;

internal enum InventoryMovementKind
{
    Injection = 1,
    Withdrawal = 2
}

// Append-only ledger row (ADR-0006): seamline_app holds SELECT/INSERT only.
// It is also the audit record of storage — who moved what, when and why —
// since Inventory publishes no events (ADR-0040). QuantityAfter and
// WacogAfter snapshot the balance the movement produced, so the ledger
// alone reproduces the stock at any point.
internal sealed class InventoryMovement : TenantOwnedEntity<Guid>
{
    public Guid StorageContractId { get; private init; }
    public DateOnly GasDay { get; private init; }
    public InventoryMovementKind Kind { get; private init; }
    public decimal Quantity { get; private init; }

    // Injection: the price paid for the gas. Withdrawal: the WACOG at that
    // moment, recorded by the system.
    public decimal UnitCost { get; private init; }
    public Guid? TradeId { get; private init; }
    public decimal QuantityAfter { get; private init; }
    public decimal WacogAfter { get; private init; }
    public int Version { get; private init; }
    public string Actor { get; private init; } = string.Empty;
    public string Reason { get; private init; } = string.Empty;
    public DateTimeOffset RecordedAt { get; private init; }

    private InventoryMovement() { }

    public static InventoryMovement Record(
        StorageContract contract, InventoryMovementKind kind, DateOnly gasDay, decimal quantity, decimal unitCost,
        Guid? tradeId, string actor, string reason) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = contract.TenantId,
            StorageContractId = contract.Id,
            GasDay = gasDay,
            Kind = kind,
            Quantity = quantity,
            UnitCost = unitCost,
            TradeId = tradeId,
            QuantityAfter = contract.Quantity,
            WacogAfter = contract.WacogShown,
            Version = contract.Version,
            Actor = actor,
            Reason = reason,
            RecordedAt = DateTimeOffset.UtcNow
        };
}
