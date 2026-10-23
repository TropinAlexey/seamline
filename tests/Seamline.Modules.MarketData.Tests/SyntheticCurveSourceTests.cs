using Seamline.Modules.MarketData.Internal;

namespace Seamline.Modules.MarketData.Tests;

public class SyntheticCurveSourceTests
{
    [Fact]
    public async Task GetMonthlyAveragePriceAsync_returns_a_positive_price()
    {
        var source = new SyntheticCurveSource();

        var price = await source.GetMonthlyAveragePriceAsync("POWER", new DateOnly(2026, 8, 1));

        Assert.NotNull(price);
        Assert.True(price > 0);
    }

    [Fact]
    public async Task GetMonthlyAveragePriceAsync_is_deterministic_for_the_same_commodity_and_month()
    {
        var source = new SyntheticCurveSource();

        var first = await source.GetMonthlyAveragePriceAsync("POWER", new DateOnly(2026, 8, 1));
        var second = await source.GetMonthlyAveragePriceAsync("POWER", new DateOnly(2026, 8, 15));

        // Same (commodity, year, month) — the day-of-month passed in must
        // not affect the result, since the whole point is one flat price
        // for the month.
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetMonthlyAveragePriceAsync_differs_across_commodities()
    {
        var source = new SyntheticCurveSource();

        var power = await source.GetMonthlyAveragePriceAsync("POWER", new DateOnly(2026, 8, 1));
        var gas = await source.GetMonthlyAveragePriceAsync("GAS", new DateOnly(2026, 8, 1));

        Assert.NotEqual(power, gas);
    }

    [Fact]
    public async Task GetMonthlyAveragePriceAsync_differs_across_months_for_the_same_commodity()
    {
        var source = new SyntheticCurveSource();

        var august = await source.GetMonthlyAveragePriceAsync("POWER", new DateOnly(2026, 8, 1));
        var september = await source.GetMonthlyAveragePriceAsync("POWER", new DateOnly(2026, 9, 1));

        Assert.NotEqual(august, september);
    }

    [Fact]
    public async Task DailyFixing_is_deterministic_and_stays_within_two_percent_of_the_months_price()
    {
        var source = new SyntheticCurveSource();
        var monthly = (await source.GetMonthlyAveragePriceAsync("COAL", new DateOnly(2027, 3, 1)))!.Value;

        foreach (var day in Enumerable.Range(1, 31).Select(d => new DateOnly(2027, 3, d)))
        {
            var fixing = source.DailyFixing("COAL", day);
            Assert.Equal(fixing, source.DailyFixing("COAL", day));
            Assert.InRange(fixing, monthly * 0.98m, monthly * 1.02m);
        }
    }
}
