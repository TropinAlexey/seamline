using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// Fully derived — aggregated from Trading's TradeConfirmed events. No write
// API exists for this entity from outside the module's own event consumer.
internal sealed class Position : TenantOwnedEntity<Guid>
{
    public string CommodityCode { get; private set; } = string.Empty;
    public string DeliveryPeriod { get; private set; } = string.Empty;
    public decimal NetVolume { get; private set; }

    // Weighted-average entry price of the currently held net position
    // (ADR-0014). A volume-weighted average, not a running total-cost
    // ledger: extending the position in the same direction blends the new
    // price in, but reducing it (a partial close, same direction as the
    // existing position but smaller magnitude) leaves the average
    // untouched — the cost basis of what's left doesn't change just
    // because some of it was sold. Flipping through zero starts a fresh
    // average at the incoming trade's price. No lot-level FIFO tracking —
    // a position is one blended number, not a queue of specific trades, so
    // Amend (which unweights an old contribution and reweights a new one
    // via two Apply calls) is only exact when the amended trade is the
    // position's only contributor. Adequate for a net MtM view, not a
    // full P&L-realization ledger.
    public decimal WeightedAvgPrice { get; private set; }

    private Position() { }

    public static Position Create(TenantId tenantId, string commodityCode, string deliveryPeriod)
    {
        return new Position
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CommodityCode = commodityCode,
            DeliveryPeriod = deliveryPeriod,
            NetVolume = 0m,
            WeightedAvgPrice = 0m
        };
    }

    public void Apply(decimal signedVolume, decimal price)
    {
        var newNetVolume = NetVolume + signedVolume;

        if (newNetVolume == 0m)
        {
            WeightedAvgPrice = 0m;
        }
        else if (NetVolume == 0m || Math.Sign(newNetVolume) != Math.Sign(NetVolume))
        {
            // Opening from flat, or flipping through zero — the remainder
            // is a fresh position, priced at this trade's price.
            WeightedAvgPrice = price;
        }
        else if (Math.Abs(newNetVolume) > Math.Abs(NetVolume))
        {
            // Extending in the same direction — blend.
            var oldAbs = Math.Abs(NetVolume);
            var addedAbs = Math.Abs(signedVolume);
            WeightedAvgPrice = (WeightedAvgPrice * oldAbs + price * addedAbs) / (oldAbs + addedAbs);
        }
        // else: reducing without flipping — cost basis of what's left is
        // unchanged.

        NetVolume = newNetVolume;
    }
}
