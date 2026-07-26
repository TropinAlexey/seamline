using Seamline.Modules.Risk.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Tests;

public class PositionTests
{
    [Fact]
    public void Create_starts_at_zero_net_volume()
    {
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");

        Assert.Equal(0m, position.NetVolume);
        Assert.Equal("POWER", position.CommodityCode);
        Assert.Equal("2027-03", position.DeliveryPeriod);
    }

    [Fact]
    public void Apply_adds_a_positive_signed_volume()
    {
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");

        position.Apply(100m);

        Assert.Equal(100m, position.NetVolume);
    }

    [Fact]
    public void Apply_subtracts_a_negative_signed_volume()
    {
        var position = Position.Create(TenantId.New(), "POWER", "2027-03");
        position.Apply(100m);

        position.Apply(-40m);

        Assert.Equal(60m, position.NetVolume);
    }

    [Fact]
    public void Apply_accumulates_across_multiple_calls()
    {
        var position = Position.Create(TenantId.New(), "GAS", "2027-04");

        position.Apply(50m);
        position.Apply(30m);
        position.Apply(-20m);

        // 50 + 30 - 20 — Apply never recomputes from scratch, each call is a
        // delta on top of whatever NetVolume already holds (the same
        // property TradeAmendedConsumer relies on for amend deltas).
        Assert.Equal(60m, position.NetVolume);
    }

    [Fact]
    public void Apply_can_take_net_volume_negative()
    {
        var position = Position.Create(TenantId.New(), "GAS", "2027-04");

        position.Apply(-25m);

        Assert.Equal(-25m, position.NetVolume);
    }
}
