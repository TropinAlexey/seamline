namespace Seamline.Modules.MarketData.Internal;

// Zero-configuration default (ADR-0018) — every commodity falls back here
// unless MarketData:CurveImport:Sources:{commodity} names a real source, so
// the app runs out of the box with no API keys configured. Deterministic
// per (commodity, year, month), not random per call — a demo's numbers
// don't jitter every time the job ticks, they only drift month to month,
// the way a real curve would.
internal sealed class SyntheticCurveSource : ICurveSource
{
    // Plausible order-of-magnitude anchors, EUR per commodity unit (MWh, or
    // tonne for COAL — ADR-0033) — a demo
    // placeholder, not derived from any real vendor's pricing (CLAUDE.md's
    // clean-room rule).
    private static readonly Dictionary<string, decimal> BasePrices = new()
    {
        ["POWER"] = 65m,
        ["GAS"] = 30m,
        ["COAL"] = 110m
    };

    public Task<decimal?> GetMonthlyAveragePriceAsync(string commodityCode, DateOnly month, CancellationToken cancellationToken = default) =>
        Task.FromResult<decimal?>(MonthlyPrice(commodityCode, month));

    // The deterministic weekday fixing series of a synthetic index on this
    // commodity (ADR-0035): within ±2 % of the month's synthetic price, so a
    // month's fixings average out near its curve point. The same date always
    // gives the same value, like the monthly price.
    public decimal DailyFixing(string commodityCode, DateOnly date)
    {
        var seed = StringHash(commodityCode) ^ (date.Year * 397) ^ (date.Month * 31) ^ date.Day;
        var variation = new Random(seed).Next(-200, 201) / 10_000m;
        return Math.Round(MonthlyPrice(commodityCode, date) * (1 + variation), 4, MidpointRounding.ToEven);
    }

    private static decimal MonthlyPrice(string commodityCode, DateOnly month)
    {
        var basePrice = BasePrices.GetValueOrDefault(commodityCode, 50m);

        // ponytail: deterministic hash — HashCode.Combine is randomised per process
        var seed = StringHash(commodityCode) ^ (month.Year * 397) ^ month.Month;
        var variationPercent = new Random(seed).Next(-10, 11) / 100m;

        return Math.Round(basePrice * (1 + variationPercent), 2, MidpointRounding.ToEven);
    }

    private static int StringHash(string s)
    {
        unchecked
        {
            int hash = 5381;
            foreach (var c in s)
                hash = (hash << 5) + hash + c;
            return hash;
        }
    }
}
