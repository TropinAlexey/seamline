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
    public Guid RunId { get; private set; }
    public DateTimeOffset ValuedAt { get; private set; }

    // Set on a formula trade's row (ADR-0035), as on ValuationSnapshot.
    public Guid? TradeId { get; private set; }

    private StressScenarioResult() { }

    // The shock moves the forward, and the expected price moves with it on
    // the unfixed days, so only the fixed share responds (spec 03, rule 7).
    // The caller passes the expected price recomputed at the shocked
    // forward, or the final price once fixed.
    public static StressScenarioResult CreateForFormulaTrade(
        FormulaTrade trade,
        FormulaPrice formula,
        decimal curvePrice,
        ScenarioType scenarioType,
        decimal shockPercentage,
        EodRun run)
    {
        var shockedPrice = Shock(curvePrice, shockPercentage);
        var expected = trade.MarkPrice(formula, shockedPrice)!.Value;

        return new StressScenarioResult
        {
            Id = Guid.NewGuid(),
            TenantId = trade.TenantId,
            TradeId = trade.TradeId,
            CommodityCode = trade.CommodityCode,
            DeliveryPeriod = trade.DeliveryPeriod,
            NetVolume = trade.SignedVolume,
            WeightedAvgPrice = FormulaPrice.ToPriceScale(expected)!.Value,
            ScenarioType = scenarioType,
            ShockPercentage = shockPercentage,
            ShockedPrice = shockedPrice,
            MtmAmount = MtmCalculator.Calculate(shockedPrice, expected, trade.SignedVolume),
            RunId = run.RunId,
            ValuedAt = run.ValuedAt
        };
    }

    private static decimal Shock(decimal curvePrice, decimal shockPercentage) =>
        Math.Round(curvePrice * (1m + shockPercentage / 100m), 4, MidpointRounding.ToEven);

    public static StressScenarioResult Create(
        TenantId tenantId,
        string commodityCode,
        string deliveryPeriod,
        decimal netVolume,
        decimal weightedAvgPrice,
        decimal curvePrice,
        ScenarioType scenarioType,
        decimal shockPercentage,
        EodRun run)
    {
        // ADR-0007: rounding is explicit, named, at the point it happens —
        // never implicit via a column's numeric(18,4) scale. ShockedPrice
        // is computed (curvePrice * a percentage), not a pass-through of
        // already-stored market data, so it needs its own explicit round.
        var shockedPrice = Shock(curvePrice, shockPercentage);
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
            RunId = run.RunId,
            ValuedAt = run.ValuedAt
        };
    }
}
