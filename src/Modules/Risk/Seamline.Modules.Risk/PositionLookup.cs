using Microsoft.EntityFrameworkCore;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// Shared by TradeActivatedConsumer/TradeAmendedConsumer/TradeDeliveredConsumer
// — all three look up a Position by (CommodityCode, DeliveryPeriod) before
// applying a trade event to it. Activate/Amend create the position on first
// sight; Delivered never should (a delivery with no live position is a data
// inconsistency elsewhere, not something to paper over by creating one), so
// that variant stays a plain find with no fallback.
internal static class PositionLookup
{
    public static async Task<Position> FindOrCreateAsync(
        RiskDbContext db, TenantId tenantId, string commodityCode, string deliveryPeriod, CancellationToken cancellationToken)
    {
        var position = await FindAsync(db, commodityCode, deliveryPeriod, cancellationToken);
        if (position is null)
        {
            position = Position.Create(tenantId, commodityCode, deliveryPeriod);
            db.Positions.Add(position);
        }

        return position;
    }

    public static Task<Position?> FindAsync(
        RiskDbContext db, string commodityCode, string deliveryPeriod, CancellationToken cancellationToken) =>
        db.Positions.FirstOrDefaultAsync(
            p => p.CommodityCode == commodityCode && p.DeliveryPeriod == deliveryPeriod, cancellationToken);
}
