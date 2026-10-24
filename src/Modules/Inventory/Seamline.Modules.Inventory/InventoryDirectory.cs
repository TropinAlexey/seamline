using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Seamline.Modules.Inventory.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Inventory.Internal;

internal sealed class InventoryDirectory(InventoryDbContext db, IConfiguration configuration) : IInventoryDirectory
{
    public async Task<IReadOnlyList<StorageBalanceRef>> ListStockAsync(CancellationToken cancellationToken = default) =>
        await db.StorageContracts.AsNoTracking()
            .Where(c => c.Quantity > 0)
            .OrderBy(c => c.Facility)
            .Select(c => new StorageBalanceRef(c.Id, c.Facility, c.CommodityCode, c.Quantity, c.Wacog))
            .ToListAsync(cancellationToken);

    // No tenant registry exists (ADR-0005): the owner connection bypasses
    // RLS and the query filter for this one read, as in every runner's
    // tenant discovery. It returns tenant ids only, never stock; per-tenant
    // reads afterwards go through the app role. The owner context is built
    // here, not by a shared helper, on purpose: each RLS bypass stays
    // visible at the one place that needs it.
    public async Task<IReadOnlyList<Guid>> TenantsWithStorageAsync(CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(configuration.GetConnectionString("PostgresMigrator"))
            .Options;
        await using var owner = new InventoryDbContext(options, new TenantContext());
        return await owner.StorageContracts.IgnoreQueryFilters()
            .Select(c => c.TenantId.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
