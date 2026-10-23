using Seamline.Modules.Trading.Contracts;
using Seamline.Modules.Trading.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Trading.Tests;

// Index-priced trades (ADR-0035, ADR-0038, spec 03): the price is empty
// until the window fixes, then set once as a new version.
public class IndexPricedTradeTests
{
    private static readonly IndexPricingTerms March =
        new("COAL-IDX", new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31), 1.5m);

    private static CargoTerms Terms() =>
        new(10m, new DateOnly(2027, 3, 10), new DateOnly(2027, 3, 20), 6000.0m, 5700.0m, 1.00m, 16.00m);

    private static Trade Draft(IndexPricingTerms? pricing = null, CargoTerms? cargo = null, decimal? price = null) =>
        Trade.CreateDraft(TenantId.New(), "COAL", "2027-03", TradeDirection.Buy, 75_000m, price, Guid.NewGuid(),
            cargo ?? Terms(), pricing ?? March);

    private static Trade Active()
    {
        var trade = Draft();
        trade.Submit("trader", "Submitted");
        trade.Activate("system", "Within limit");
        return trade;
    }

    [Fact]
    public void An_index_trade_has_no_price_until_fixed_and_activates_with_its_terms()
    {
        var trade = Draft();
        trade.Submit("trader", "Submitted");
        var (_, activated) = trade.Activate("system", "Within limit");

        Assert.Null(trade.Price);
        Assert.Null(trade.Notional);
        Assert.Null(activated.Price);
        Assert.Equal(March, activated.Pricing);
    }

    [Fact]
    public void Booking_rules()
    {
        // Exactly one of price and formula.
        Assert.Throws<ArgumentException>(() => Draft(price: 110m));
        // Cargo commodities only.
        Assert.Throws<ArgumentException>(() =>
            Trade.CreateDraft(TenantId.New(), "GAS", "2027-03", TradeDirection.Buy, 100m, null, Guid.NewGuid(), null, March));
        // The window lies inside the delivery month.
        Assert.Throws<ArgumentException>(() => Draft(March with { WindowTo = new DateOnly(2027, 4, 2) }));
        // And has at least one weekday (6–7 March 2027 is a weekend).
        Assert.Throws<ArgumentException>(() =>
            Draft(March with { WindowFrom = new DateOnly(2027, 3, 6), WindowTo = new DateOnly(2027, 3, 7) }));
        // A fixed-price trade still needs its price.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Trade.CreateDraft(TenantId.New(), "COAL", "2027-03", TradeDirection.Buy, 100m, null, Guid.NewGuid(), Terms()));
    }

    [Fact]
    public void Amending_an_index_trade_changes_its_volume_only()
    {
        var trade = Active();

        var (_, amended) = trade.Amend("trader", "Correction", 80_000m, newPrice: null);

        Assert.Equal((80_000m, (decimal?)null), (trade.Volume, trade.Price));
        Assert.Equal((null, null), (amended.OldPrice, amended.NewPrice));
        Assert.Throws<ArgumentException>(() => trade.Amend("trader", "Reprice", 80_000m, 105m));
    }

    [Fact]
    public void Fixing_the_price_writes_a_new_version_and_carries_the_certificate_when_there_is_one()
    {
        var trade = Active();
        var version = trade.Version;

        var (history, priceFixed) = trade.FixPrice("ops", 106.5m, 105m, new CargoCertificate(5_920.0m, 6_000.0m));

        Assert.Equal(106.5m, trade.Price);
        Assert.Equal(version + 1, history.Version);
        Assert.Equal(106.5m, history.Price);
        Assert.Equal("Pricing window fixed: average 105.0000 +1.5000 premium = 106.5000", history.ChangeReason);
        Assert.Equal((106.5m, (decimal?)5_920.0m, (decimal?)6_000.0m), (priceFixed.FinalPrice, priceFixed.MeasuredNarKcalKg, priceFixed.BaseNarKcalKg));
        // Once only.
        Assert.Throws<DomainRuleViolationException>(() => trade.FixPrice("ops", 106.5m, 105m));
    }

    [Fact]
    public void A_fixed_price_trade_has_no_window_to_fix()
    {
        var trade = Trade.CreateDraft(TenantId.New(), "COAL", "2027-03", TradeDirection.Buy, 75_000m, 110m, Guid.NewGuid(), Terms());
        trade.Submit("trader", "Submitted");
        trade.Activate("system", "Within limit");

        Assert.Throws<DomainRuleViolationException>(() => trade.FixPrice("ops", 106.5m, 105m));
    }

    [Fact]
    public void Loading_before_the_window_fixes_invoices_at_the_expected_price_and_fixing_is_still_allowed()
    {
        var trade = Active();

        Assert.Throws<DomainRuleViolationException>(() => trade.DeliverLoaded("ops", 78_500m, new DateOnly(2027, 3, 14)));
        var (_, delivered) = trade.DeliverLoaded("ops", 78_500m, new DateOnly(2027, 3, 14), expectedPrice: 106.7174m);

        Assert.Equal(106.7174m, delivered.Price);
        Assert.Null(trade.Price);
        trade.FixPrice("ops", 106.5m, 105m);
        Assert.Equal((TradeState.Delivered, (decimal?)106.5m), (trade.State, trade.Price));
    }

    [Fact]
    public void A_certificate_before_the_price_is_fixed_marks_the_final_invoice_as_pending()
    {
        var trade = Active();
        var (shipment, _, _) = Shipment.Nominate(trade, "MV Demo", "9876543", new DateOnly(2027, 3, 12), "ops");
        shipment.RecordLoading(78_500m, new DateOnly(2027, 3, 14), "ops");

        var (_, @event) = shipment.RecordCertificate(trade.CargoTerms!, 5_920.0m, 0.85m, 14.20m, "ops", trade.Price);

        var certified = Assert.IsType<CargoQualityCertified>(@event);
        Assert.True(certified.PricePending);
        Assert.Null(certified.FinalPrice);
    }
}
