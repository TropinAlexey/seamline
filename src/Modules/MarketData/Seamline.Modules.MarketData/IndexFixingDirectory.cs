using Microsoft.EntityFrameworkCore;
using Seamline.Modules.MarketData.Contracts;

namespace Seamline.Modules.MarketData.Internal;

internal sealed class IndexFixingDirectory(MarketDataDbContext db) : IIndexFixingDirectory
{
    public Task<PriceIndexRef?> FindIndexAsync(string indexCode, CancellationToken cancellationToken = default) =>
        db.PriceIndices
            .Where(i => i.Code == indexCode)
            .Select(i => new PriceIndexRef(i.Code, i.CommodityCode, i.Name))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<IndexFixingRef>> ListFixingsAsync(
        string indexCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await db.IndexFixings
            .Where(f => f.IndexCode == indexCode && f.FixingDate >= from && f.FixingDate <= to)
            .OrderBy(f => f.FixingDate)
            .Select(f => new IndexFixingRef(f.FixingDate, f.Value))
            .ToListAsync(cancellationToken);
}
