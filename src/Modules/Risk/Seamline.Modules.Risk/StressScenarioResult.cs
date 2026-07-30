using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

internal enum ScenarioType
{
    FlatShock = 1,
    SingleCommodityShock = 2
}

// Append-only, same shape as ValuationSnapshot — one row per (position,
// scenario, direction) per EOD run, never updated. Computed inside the
// same EndOfDayValuationRunner pass as real valuation, reusing the same
// Position/curve price already fetched there (ADR-0016) — this entity
// never queries anything itself.
internal sealed class StressScenarioResult : TenantOwnedEntity<Guid>
{
    public string CommodityCode { get; private set; } = string.Empty;
    public string DeliveryPeriod { get; private set; } = string.Empty;
    public decimal NetVolume { get; private set; }
    public decimal WeightedAvgPrice { get; private set; }
    public ScenarioType ScenarioType { get; private set; }
    public decimal ShockPercentage { get; private set; }
    public decimal ShockedPrice { get; private set; }
    public decimal MtmAmount { get; private set; }
    public DateTimeOffset ValuedAt { get; private set; }

    private StressScenarioResult() { }

    public static StressScenarioResult Create(
        TenantId tenantId,
        string commodityCode,
        string deliveryPeriod,
        decimal netVolume,
        decimal weightedAvgPrice,
        decimal curvePrice,
        ScenarioType scenarioType,
        decimal shockPercentage)
    {
        // ADR-0007: rounding is explicit, named, at the point it happens —
        // never implicit via a column's numeric(18,4) scale. ShockedPrice
        // is computed (curvePrice * a percentage), not a pass-through of
        // already-stored market data, so it needs its own explicit round.
        var shockedPrice = Math.Round(curvePrice * (1m + shockPercentage / 100m), 4, MidpointRounding.ToEven);
        var mtmAmount = MtmCalculator.Calculate(shockedPrice, weightedAvgPrice, netVolume);

        return new StressScenarioResult
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CommodityCode = commodityCode,
            DeliveryPeriod = deliveryPeriod,
            NetVolume = netVolume,
            WeightedAvgPrice = weightedAvgPrice,
            ScenarioType = scenarioType,
            ShockPercentage = shockPercentage,
            ShockedPrice = shockedPrice,
            MtmAmount = mtmAmount,
            ValuedAt = DateTimeOffset.UtcNow
        };
    }
}
