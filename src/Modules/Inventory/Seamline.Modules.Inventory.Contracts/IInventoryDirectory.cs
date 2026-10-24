namespace Seamline.Modules.Inventory.Contracts;

// Gas in storage as it stands now: quantity and weighted-average cost
// (ADR-0036). Risk values it in the EOD pass against the curve.
public sealed record StorageBalanceRef(
    Guid StorageContractId,
    string Facility,
    string CommodityCode,
    decimal Quantity,
    decimal Wacog);

// Synchronous, in-process read query, the same pattern as
// ICurvePointDirectory. Inventory publishes no integration events
// (ADR-0040); the EOD pass reads stock through this interface instead.
public interface IInventoryDirectory
{
    // The current tenant's storage contracts that hold gas.
    Task<IReadOnlyList<StorageBalanceRef>> ListStockAsync(CancellationToken cancellationToken = default);

    // Every tenant that holds gas anywhere — the EOD pass's tenant
    // discovery, read through the owner connection because there is no
    // tenant registry (ADR-0005), like Risk's own discovery of positions.
    Task<IReadOnlyList<Guid>> TenantsWithStockAsync(CancellationToken cancellationToken = default);
}
