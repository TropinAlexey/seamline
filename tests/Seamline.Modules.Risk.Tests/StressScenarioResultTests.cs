using Seamline.Modules.Risk.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Tests;

public class StressScenarioResultTests
{
    [Fact]
    public void Create_shocks_the_curve_price_upward_by_the_given_percentage()
    {
        var result = StressScenarioResult.Create(
            TenantId.New(), "POWER", "2027-03", netVolume: 100m, weightedAvgPrice: 40m,
            curvePrice: 40m, scenarioType: ScenarioType.FlatShock, shockPercentage: 10m);

        Assert.Equal(44m, result.ShockedPrice); // 40 * 1.10
    }

    [Fact]
    public void Create_shocks_the_curve_price_downward_for_a_negative_percentage()
    {
        var result = StressScenarioResult.Create(
            TenantId.New(), "POWER", "2027-03", netVolume: 100m, weightedAvgPrice: 40m,
            curvePrice: 40m, scenarioType: ScenarioType.FlatShock, shockPercentage: -10m);

        Assert.Equal(36m, result.ShockedPrice); // 40 * 0.90
    }

    [Fact]
    public void Create_computes_MtM_against_the_shocked_price_not_the_real_one()
    {
        var result = StressScenarioResult.Create(
            TenantId.New(), "POWER", "2027-03", netVolume: 100m, weightedAvgPrice: 40m,
            curvePrice: 40m, scenarioType: ScenarioType.SingleCommodityShock, shockPercentage: 25m);

        // Shocked price = 40 * 1.25 = 50; MtM = (50 - 40) * 100 = 1,000 —
        // same MtmCalculator formula ValuationSnapshot uses, just fed the
        // shocked price instead of the real curve price.
        Assert.Equal(50m, result.ShockedPrice);
        Assert.Equal(1_000m, result.MtmAmount);
    }

    [Fact]
    public void Create_records_the_scenario_type_and_signed_shock_percentage()
    {
        var result = StressScenarioResult.Create(
            TenantId.New(), "GAS", "2027-04", netVolume: 50m, weightedAvgPrice: 20m,
            curvePrice: 20m, scenarioType: ScenarioType.SingleCommodityShock, shockPercentage: -25m);

        Assert.Equal(ScenarioType.SingleCommodityShock, result.ScenarioType);
        Assert.Equal(-25m, result.ShockPercentage);
    }

    [Fact]
    public void Create_flips_MtM_sign_for_a_short_position_under_an_upward_shock()
    {
        var result = StressScenarioResult.Create(
            TenantId.New(), "POWER", "2027-03", netVolume: -100m, weightedAvgPrice: 40m,
            curvePrice: 40m, scenarioType: ScenarioType.FlatShock, shockPercentage: 10m);

        // Shocked price 44, short 100 units: (44 - 40) * -100 = -400.
        Assert.Equal(-400m, result.MtmAmount);
    }

    // ADR-0007: rounding is explicit, named, at the point it happens —
    // never implicit via a column's numeric(18,4) scale. ShockedPrice is a
    // computed value (curvePrice * a percentage), not a pass-through of
    // already-stored market data, so it needs the same explicit rounding
    // treatment MtmAmount already gets via MtmCalculator.
    [Fact]
    public void Create_rounds_the_shocked_price_to_four_decimal_places_explicitly()
    {
        // 33.335 * 1.01 = 33.66835 — an exact midpoint at the 4th decimal
        // (33.6683 vs 33.6684). Only passes if rounding is actually applied
        // with MidpointRounding.ToEven: 3 is odd, 4 is even, so it rounds
        // up to the even neighbour.
        var result = StressScenarioResult.Create(
            TenantId.New(), "POWER", "2027-03", netVolume: 100m, weightedAvgPrice: 30m,
            curvePrice: 33.335m, scenarioType: ScenarioType.FlatShock, shockPercentage: 1m);

        Assert.Equal(33.6684m, result.ShockedPrice);
    }
}
