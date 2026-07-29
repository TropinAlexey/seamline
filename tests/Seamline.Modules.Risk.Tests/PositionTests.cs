using Seamline.Modules.Risk.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Tests;

public class PositionTests
{
    [Fact]
    public void Create_starts_at_zero_net_volume_and_zero_cost_basis()
    {
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");

        Assert.Equal(0m, position.NetVolume);
        Assert.Equal(0m, position.WeightedAvgPrice);
        Assert.Equal("POWER", position.CommodityCode);
        Assert.Equal("2027-03", position.DeliveryPeriod);
    }

    [Fact]
    public void Apply_adds_a_positive_signed_volume()
    {
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");

        position.Apply(100m, 45.5m);

        Assert.Equal(100m, position.NetVolume);
        Assert.Equal(45.5m, position.WeightedAvgPrice);
    }

    [Fact]
    public void Apply_subtracts_a_negative_signed_volume()
    {
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");
        position.Apply(100m, 45.5m);

        position.Apply(-40m, 45.5m);

        Assert.Equal(60m, position.NetVolume);
    }

    [Fact]
    public void Apply_accumulates_volume_across_multiple_calls()
    {
        var position = Position.Create(TenantId.New(), "GAS", "2027-04");

        position.Apply(50m, 20m);
        position.Apply(30m, 20m);
        position.Apply(-20m, 20m);

        // 50 + 30 - 20 — Apply never recomputes from scratch, each call is a
        // delta on top of whatever NetVolume already holds (the same
        // property TradeAmendedConsumer relies on for amend deltas).
        Assert.Equal(60m, position.NetVolume);
    }

    [Fact]
    public void Apply_can_take_net_volume_negative()
    {
        var position = Position.Create(TenantId.New(), "GAS", "2027-04");

        position.Apply(-25m, 20m);

        Assert.Equal(-25m, position.NetVolume);
    }

    [Fact]
    public void WeightedAvgPrice_blends_two_buys_at_different_prices()
    {
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");

        position.Apply(100m, 40m); // 4,000
        position.Apply(100m, 50m); // 5,000

        // (4,000 + 5,000) / 200 = 45
        Assert.Equal(200m, position.NetVolume);
        Assert.Equal(45m, position.WeightedAvgPrice);
    }

    [Fact]
    public void WeightedAvgPrice_is_unchanged_by_a_partial_close_in_the_opposite_direction()
    {
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");
        position.Apply(100m, 40m);

        // Selling 30 of the 100 doesn't change the cost basis of what's left.
        position.Apply(-30m, 999m);

        Assert.Equal(70m, position.NetVolume);
        Assert.Equal(40m, position.WeightedAvgPrice);
    }

    [Fact]
    public void WeightedAvgPrice_resets_to_zero_when_the_position_nets_flat()
    {
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");
        position.Apply(100m, 40m);

        position.Apply(-100m, 999m);

        Assert.Equal(0m, position.NetVolume);
        Assert.Equal(0m, position.WeightedAvgPrice);
    }

    [Fact]
    public void Amend_style_unweight_and_reweight_leaves_only_the_new_contribution()
    {
        // Mirrors what TradeAmendedConsumer does: remove the old
        // contribution at its own price, add the new one at its own price —
        // two separate Apply calls, not a single combined delta.
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");
        position.Apply(100m, 40m); // original trade booked at 40

        position.Apply(-100m, 40m); // unweight the original contribution
        position.Apply(150m, 46m); // reweight at the amended volume/price

        Assert.Equal(150m, position.NetVolume);
        Assert.Equal(46m, position.WeightedAvgPrice);
    }
}
