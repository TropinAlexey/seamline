using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// Fully derived — aggregated from Trading's TradeConfirmed events. No write
// API exists for this entity from outside the module's own event consumer.
internal sealed class Position : TenantOwnedEntity<Guid>
{
    public string CommodityCode { get; private set; } = string.Empty;
    public string DeliveryPeriod { get; private set; } = string.Empty;
    public decimal NetVolume { get; private set; }

    private Position() { }

    public static Position Create(TenantId tenantId, string commodityCode, string deliveryPeriod)
    {
        return new Position
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CommodityCode = commodityCode,
            DeliveryPeriod = deliveryPeriod,
            NetVolume = 0m
        };
    }

    public void Apply(decimal signedVolume) => NetVolume += signedVolume;
}
