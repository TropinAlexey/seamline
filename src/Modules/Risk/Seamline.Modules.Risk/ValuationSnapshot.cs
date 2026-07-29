using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// Append-only, same shape as TradeHistory (ADR-0006) and audit_event
// (ADR-0010): one row per EOD revaluation run, never updated. Written only
// by Valuation.Worker (ADR-0014), not by Seamline.Api — but it lives in
// Risk's schema because Position, the data it's derived from, is Risk's.
// CurvePrice/CurvePublishedAt are copied at valuation time rather than
// referencing PriceCurvePoint by id: curve points don't keep their own
// history (ADR-0012), so a snapshot has to carry what it used directly to
// stay reproducible after the live curve point is later overwritten.
internal sealed class ValuationSnapshot : TenantOwnedEntity<Guid>
{
    public string CommodityCode { get; private set; } = string.Empty;
    public string DeliveryPeriod { get; private set; } = string.Empty;
    public decimal NetVolume { get; private set; }
    public decimal WeightedAvgPrice { get; private set; }
    public decimal CurvePrice { get; private set; }
    public DateTimeOffset CurvePublishedAt { get; private set; }
    public decimal MtmAmount { get; private set; }
    public DateTimeOffset ValuedAt { get; private set; }

    private ValuationSnapshot() { }

    public static ValuationSnapshot Create(
        TenantId tenantId,
        string commodityCode,
        string deliveryPeriod,
        decimal netVolume,
        decimal weightedAvgPrice,
        decimal curvePrice,
        DateTimeOffset curvePublishedAt)
    {
        // ADR-0007: rounding is explicit, named, at the point it happens —
        // never implicit via column scale. MtM = (forward_price -
        // trade_price) * volume, rounded once, here, at persistence.
        var mtmAmount = Math.Round((curvePrice - weightedAvgPrice) * netVolume, 2, MidpointRounding.ToEven);

        return new ValuationSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CommodityCode = commodityCode,
            DeliveryPeriod = deliveryPeriod,
            NetVolume = netVolume,
            WeightedAvgPrice = weightedAvgPrice,
            CurvePrice = curvePrice,
            CurvePublishedAt = curvePublishedAt,
            MtmAmount = mtmAmount,
            ValuedAt = DateTimeOffset.UtcNow
        };
    }
}
