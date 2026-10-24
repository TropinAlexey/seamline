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

    // Every tenant with a storage contract, full or empty — the EOD pass's
    // tenant discovery, so a tenant whose storage emptied still gets the
    // run's marker (ADR-0041). Read through the owner connection because
    // there is no tenant registry (ADR-0005), like Risk's own discovery of
    // positions. Tenant ids only: no stock leaves through this call.
    Task<IReadOnlyList<Guid>> TenantsWithStorageAsync(CancellationToken cancellationToken = default);
}
