namespace Seamline.Modules.MarketData.Internal;

// Abstracts where a commodity's monthly curve price comes from (ADR-0018).
// The result is the average of the commodity's daily price across every
// elapsed day of the given month, not a single point-in-time price — see
// ADR-0018 on why this averaging isn't the "curve shaping" CLAUDE.md rules
// out. Never throws: null means "no price available this run" (external
// source down, no API key configured, or genuinely no data), so one
// source's outage never fails the whole import run — the caller just skips
// that commodity and keeps the existing point.
internal interface ICurveSource
{
    Task<decimal?> GetMonthlyAveragePriceAsync(string commodityCode, DateOnly month, CancellationToken cancellationToken = default);
}
