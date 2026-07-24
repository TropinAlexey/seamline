using Microsoft.EntityFrameworkCore;
using Seamline.Modules.MarketData.Contracts;

namespace Seamline.Modules.MarketData.Internal;

internal sealed class CurvePointDirectory(MarketDataDbContext db) : ICurvePointDirectory
{
    public async Task<CurvePointRef?> FindAsync(string commodityCode, string deliveryPeriod, CancellationToken cancellationToken = default)
    {
        return await db.PriceCurvePoints
            .Where(p => p.CommodityCode == commodityCode && p.DeliveryPeriod == deliveryPeriod)
            .Select(p => new CurvePointRef(p.CommodityCode, p.DeliveryPeriod, p.Price))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
