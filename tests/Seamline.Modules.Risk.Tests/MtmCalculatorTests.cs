using Seamline.Modules.Risk.Internal;

namespace Seamline.Modules.Risk.Tests;

public class MtmCalculatorTests
{
    [Fact]
    public void Calculate_is_curve_price_minus_cost_basis_times_net_volume()
    {
        // (46 - 40) * 100 = 600
        var mtm = MtmCalculator.Calculate(curvePrice: 46m, weightedAvgPrice: 40m, netVolume: 100m);

        Assert.Equal(600m, mtm);
    }

    [Fact]
    public void Calculate_is_negative_when_the_curve_price_is_below_cost_basis()
    {
        // (35 - 40) * 100 = -500
        var mtm = MtmCalculator.Calculate(curvePrice: 35m, weightedAvgPrice: 40m, netVolume: 100m);

        Assert.Equal(-500m, mtm);
    }

    [Fact]
    public void Calculate_flips_sign_for_a_short_position()
    {
        // (46 - 40) * -100 = -600 — a short position loses when the price rises.
        var mtm = MtmCalculator.Calculate(curvePrice: 46m, weightedAvgPrice: 40m, netVolume: -100m);

        Assert.Equal(-600m, mtm);
    }

    // ADR-0007: MidpointRounding.ToEven, exactly at persistence. Both cases
    // land exactly halfway between two cents so they only pass if ToEven is
    // actually used (AwayFromZero would round both up).
    [Theory]
    [InlineData(1, 0.025, 0.02)]  // 0.025 -> nearest even hundredth is 0.02
    [InlineData(1, 0.075, 0.08)]  // 0.075 -> nearest even hundredth is 0.08
    public void Calculate_rounds_exact_midpoints_to_the_nearest_even_cent(
        decimal priceDelta, decimal netVolume, decimal expected)
    {
        var mtm = MtmCalculator.Calculate(curvePrice: priceDelta, weightedAvgPrice: 0m, netVolume: netVolume);

        Assert.Equal(expected, mtm);
    }
}
