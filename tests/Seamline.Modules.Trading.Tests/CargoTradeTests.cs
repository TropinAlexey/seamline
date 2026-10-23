using Seamline.Modules.Trading.Contracts;
using Seamline.Modules.Trading.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Tests;

// Cargo trades and shipments (ADR-0034, spec 01). Numbers are spec 01's
// worked example: 75,000 t, 10 % tolerance, laycan 10–20 March 2027.
public class CargoTradeTests
{
    private static CargoTerms Terms(decimal tolerancePct = 10m) =>
        new(tolerancePct, new DateOnly(2027, 3, 10), new DateOnly(2027, 3, 20), 6000.0m, 5700.0m, 1.00m, 16.00m);

    private static Trade ActiveCoalTrade(decimal volume = 75_000m)
    {
        var trade = Trade.CreateDraft(TenantId.New(), "COAL", "2027-03", TradeDirection.Buy, volume, 110m, Guid.NewGuid(), Terms());
        trade.Submit("trader", "Submitted");
        trade.Activate("system", "Within limit");
        return trade;
    }

    [Theory]
    [InlineData(67_500)]
    [InlineData(82_500)]
    [InlineData(78_500)]
    public void Loading_inside_the_tolerance_band_delivers_the_trade_with_the_actual_quantity(decimal blQuantity)
    {
        var trade = ActiveCoalTrade();

        var (_, delivered) = trade.DeliverLoaded("ops", blQuantity, new DateOnly(2027, 3, 14));

        Assert.Equal(TradeState.Delivered, trade.State);
        Assert.Equal(75_000m, trade.Volume);
        Assert.Equal(blQuantity, trade.ActualVolume);
        Assert.Equal(blQuantity, delivered.Volume);
        Assert.Equal(75_000m, delivered.ContractVolume);
        Assert.Equal(6000.0m, delivered.CargoTerms!.BaseNarKcalKg);
    }

    [Theory]
    [InlineData(67_499.999)]
    [InlineData(82_500.001)]
    public void Loading_outside_the_tolerance_band_is_a_breach(decimal blQuantity)
    {
        var trade = ActiveCoalTrade();

        var ex = Assert.Throws<DomainRuleViolationException>(() => trade.DeliverLoaded("ops", blQuantity, new DateOnly(2027, 3, 14)));

        Assert.Contains("Amend the contract quantity first", ex.Message);
        Assert.Equal(TradeState.Active, trade.State);
        Assert.Null(trade.ActualVolume);
    }

    [Fact]
    public void A_cargo_trade_is_not_delivered_by_hand_and_a_flow_trade_is_not_loaded()
    {
        var coal = ActiveCoalTrade();
        var power = Trade.CreateDraft(TenantId.New(), "POWER", "2027-03", TradeDirection.Buy, 100m, 50m, Guid.NewGuid());
        power.Submit("trader", "Submitted");
        power.Activate("system", "Within limit");

        Assert.Throws<DomainRuleViolationException>(() => coal.Deliver("trader", "Delivered"));
        Assert.Throws<DomainRuleViolationException>(() => power.DeliverLoaded("ops", 100m, new DateOnly(2027, 3, 14)));
    }

    [Fact]
    public void A_flow_delivery_reports_the_contract_volume_and_no_cargo_terms()
    {
        var power = Trade.CreateDraft(TenantId.New(), "POWER", "2027-03", TradeDirection.Buy, 100m, 50m, Guid.NewGuid());
        power.Submit("trader", "Submitted");
        power.Activate("system", "Within limit");

        var (_, delivered) = power.Deliver("trader", "Delivered");

        Assert.Equal(100m, delivered.ContractVolume);
        Assert.Null(delivered.CargoTerms);
    }

    [Theory]
    [InlineData("2027-03-28", "2027-04-03")] // runs into April
    [InlineData("2027-02-25", "2027-03-05")] // starts in February
    public void The_laycan_must_lie_inside_the_delivery_period(string from, string to)
    {
        var terms = new CargoTerms(10m, DateOnly.Parse(from), DateOnly.Parse(to), 6000m, 5700m, 1m, 16m);

        Assert.Throws<ArgumentException>(() =>
            Trade.CreateDraft(TenantId.New(), "COAL", "2027-03", TradeDirection.Buy, 75_000m, 110m, Guid.NewGuid(), terms));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20.01)]
    public void Tolerance_is_between_0_and_20_percent(decimal tolerancePct)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Terms(tolerancePct));
    }

    [Theory]
    [InlineData("2027-03-09")]
    [InlineData("2027-03-21")]
    public void A_vessel_must_arrive_within_the_laycan(string eta)
    {
        Assert.Throws<ArgumentException>(() => Shipment.Nominate(ActiveCoalTrade(), "MV Demo", "9876543", DateOnly.Parse(eta), "ops"));
    }

    [Theory]
    [InlineData(5_700.0, 1.00, 16.00, true)]   // every parameter exactly on its limit
    [InlineData(5_920.0, 0.85, 14.20, true)]   // the worked example
    [InlineData(5_699.9, 0.85, 14.20, false)]  // calorific value below the floor
    [InlineData(5_920.0, 1.01, 14.20, false)]  // too much sulphur
    [InlineData(5_920.0, 0.85, 16.01, false)]  // too much ash
    public void The_certificate_is_judged_against_the_contract_limits(decimal nar, decimal sulphur, decimal ash, bool accepted)
    {
        var trade = ActiveCoalTrade();
        var (shipment, _, _) = Shipment.Nominate(trade, "MV Demo", "9876543", new DateOnly(2027, 3, 12), "ops");
        shipment.RecordLoading(78_500m, new DateOnly(2027, 3, 14), "ops");

        var (_, @event) = shipment.RecordCertificate(trade.CargoTerms!, nar, sulphur, ash, "ops", trade.Price);

        Assert.Equal(accepted ? ShipmentState.QualityCertified : ShipmentState.QualityRejected, shipment.State);
        Assert.IsType(accepted ? typeof(CargoQualityCertified) : typeof(CargoQualityRejected), @event);
    }
}
