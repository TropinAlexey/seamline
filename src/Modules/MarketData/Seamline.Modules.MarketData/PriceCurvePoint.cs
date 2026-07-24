using Seamline.SharedKernel;

namespace Seamline.Modules.MarketData.Internal;

// Flat monthly point, no curve interpolation or shaping — see CLAUDE.md's
// scope boundaries. One point per (tenant, commodity, delivery period);
// re-publishing a point overwrites the price in place. No history is kept
// here — that's a deliberate simplification, not an omission: unlike
// Trade (ADR-0006), a curve point isn't itself an audited business
// transaction, it's a market data input that gets refreshed.
internal sealed class PriceCurvePoint : TenantOwnedEntity<Guid>
{
    public string CommodityCode { get; private set; } = string.Empty;
    public string DeliveryPeriod { get; private set; } = string.Empty;
    public decimal Price { get; private set; }

    private PriceCurvePoint() { }

    public static PriceCurvePoint Create(TenantId tenantId, string commodityCode, string deliveryPeriod, decimal price)
    {
        Validate(commodityCode, deliveryPeriod, price);

        return new PriceCurvePoint
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CommodityCode = commodityCode,
            DeliveryPeriod = deliveryPeriod,
            Price = price
        };
    }

    public void UpdatePrice(decimal price)
    {
        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");
        Price = price;
    }

    private static void Validate(string commodityCode, string deliveryPeriod, decimal price)
    {
        if (string.IsNullOrWhiteSpace(commodityCode))
            throw new ArgumentException("Commodity code is required.", nameof(commodityCode));
        if (string.IsNullOrWhiteSpace(deliveryPeriod))
            throw new ArgumentException("Delivery period is required.", nameof(deliveryPeriod));
        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");
    }
}
