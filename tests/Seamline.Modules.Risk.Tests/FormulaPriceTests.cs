using Seamline.Modules.MarketData.Contracts;
using Seamline.Modules.Risk.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Tests;

// Spec 03's worked example: buy 75,000 t COAL 2027-03 at the March average
// of COAL-IDX plus 1.5000, ten fixings in at an average of 104.2000, forward
// 106.0000.
public class FormulaPriceTests
{
    private static readonly IndexPricingTerms March =
        new("COAL-IDX", new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31), 1.5m);

    private const decimal Volume = 75_000m;

    [Fact]
    public void March_2027_has_23_pricing_days()
    {
        Assert.Equal(23, March.PricingDays().Count);
    }

    [Fact]
    public void A_window_across_a_weekend_counts_weekdays_only()
    {
        // Fri 5 – Mon 8 March 2027.
        var days = PricingCalendar.Weekdays(new DateOnly(2027, 3, 5), new DateOnly(2027, 3, 8));
        Assert.Equal([new DateOnly(2027, 3, 5), new DateOnly(2027, 3, 8)], days);
    }

    [Fact]
    public void Expected_price_and_fixed_quantity_after_ten_fixings()
    {
        var price = TenFixings();

        Assert.Equal((23, 10), (price.PricingDays, price.FixedDays));
        Assert.Equal(104.2m, price.AverageFixing);
        Assert.Equal(106.7174m, Math.Round(price.Expected()!.Value, 4, MidpointRounding.ToEven));
        Assert.Equal(32_608.696m, price.FixedQuantity(Volume));
        Assert.Null(price.FinalPrice);
    }

    [Fact]
    public void Mtm_is_carried_by_the_fixed_share_only()
    {
        var price = TenFixings();

        // (106.0000 − 106.7173913…) × 75,000, rounded once.
        Assert.Equal(-53_804.35m, MtmCalculator.Calculate(106m, price.Expected()!.Value, Volume));
    }

    [Fact]
    public void A_sell_trade_carries_the_mirror_mtm()
    {
        var price = TenFixings();

        // The seller receives the formula price: worth more to them than the
        // forward by the same 53,804.35 the buyer loses.
        Assert.Equal(53_804.35m, MtmCalculator.Calculate(106m, price.Expected()!.Value, -Volume));
        Assert.Equal(-32_608.696m, price.FixedQuantity(-Volume));
    }

    [Fact]
    public void A_ten_percent_shock_moves_only_the_fixed_share()
    {
        var price = TenFixings();
        var shocked = 116.6m;

        var baseMtm = (106m - price.Expected()!.Value) * Volume;
        var shockedMtm = (shocked - price.Expected(shocked)!.Value) * Volume;

        // 10/23 × 10.6000 × 75,000. A fixed-price trade would move by 795,000.00.
        Assert.Equal(345_652.17m, Math.Round(shockedMtm - baseMtm, 2, MidpointRounding.ToEven));
        // Each stored MtM is rounded once on its own, so the two stored
        // figures differ by a cent more than the exact delta.
        Assert.Equal(291_847.83m, MtmCalculator.Calculate(shocked, price.Expected(shocked)!.Value, Volume));
    }

    [Fact]
    public void The_final_price_is_the_average_plus_premium_once_every_day_has_fixed()
    {
        var fixings = March.PricingDays().Select(d => new IndexFixingRef(d, 105m));

        var price = FormulaPrice.From(March, fixings, forward: 120m);

        Assert.True(price.FullyFixed);
        Assert.Equal(106.5m, price.FinalPrice);
        // The forward no longer matters.
        Assert.Equal(106.5m, price.Expected());
        Assert.Equal(Volume, price.FixedQuantity(Volume));
    }

    [Fact]
    public void A_premium_can_be_negative()
    {
        var discount = March with { Premium = -2.25m };
        var fixings = discount.PricingDays().Select(d => new IndexFixingRef(d, 105m));

        Assert.Equal(102.75m, FormulaPrice.From(discount, fixings, forward: null).FinalPrice);
    }

    [Fact]
    public void Without_a_forward_the_expected_price_falls_back_to_the_fixed_average()
    {
        var price = TenFixings() with { Forward = null };

        Assert.Equal(105.7m, price.Expected());
        Assert.Null(FormulaPrice.From(March, [], forward: null).Expected());
    }

    [Fact]
    public void A_fixing_outside_the_window_does_not_count()
    {
        var price = FormulaPrice.From(March, [new IndexFixingRef(new DateOnly(2027, 2, 26), 100m)], forward: 106m);

        Assert.Equal(0, price.FixedDays);
    }

    // Ten fixings averaging 104.2000: the first ten pricing days.
    private static FormulaPrice TenFixings()
    {
        var days = March.PricingDays().Take(10).ToList();
        var fixings = days.Select((d, i) => new IndexFixingRef(d, i % 2 == 0 ? 104m : 104.4m));
        return FormulaPrice.From(March, fixings, forward: 106m);
    }
}
