namespace Seamline.Modules.MarketData.Internal;

// Zero-configuration default (ADR-0018) — every commodity falls back here
// unless MarketData:CurveImport:Sources:{commodity} names a real source, so
// the app runs out of the box with no API keys configured. Deterministic
// per (commodity, year, month), not random per call — a demo's numbers
// don't jitter every time the job ticks, they only drift month to month,
// the way a real curve would.
internal sealed class SyntheticCurveSource : ICurveSource
{
    // Plausible order-of-magnitude anchors, EUR/MWh-ish — a demo
    // placeholder, not derived from any real vendor's pricing (CLAUDE.md's
    // clean-room rule).
    private static readonly Dictionary<string, decimal> BasePrices = new()
    {
        ["POWER"] = 65m,
        ["GAS"] = 30m
    };

    public Task<decimal?> GetMonthlyAveragePriceAsync(string commodityCode, DateOnly month, CancellationToken cancellationToken = default)
    {
        var basePrice = BasePrices.GetValueOrDefault(commodityCode, 50m);

        // ponytail: deterministic hash — HashCode.Combine is randomised per process
        var seed = StringHash(commodityCode) ^ (month.Year * 397) ^ month.Month;
        var variationPercent = new Random(seed).Next(-10, 11) / 100m;

        var price = Math.Round(basePrice * (1 + variationPercent), 2, MidpointRounding.ToEven);
        return Task.FromResult<decimal?>(price);
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
