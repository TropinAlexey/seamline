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

    // Set on Create, bumped on every UpdatePrice. Curve points don't keep
    // their own history (see above), so this is the only trace of "when was
    // this price live" — Valuation.Worker (ADR-0014) copies it into each
    // valuation_snapshot row so a snapshot stays reproducible after the
    // live curve point is later overwritten.
    public DateTimeOffset PublishedAt { get; private set; }

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
            Price = price,
            PublishedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdatePrice(decimal price)
    {
        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");
        Price = price;
        PublishedAt = DateTimeOffset.UtcNow;
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
