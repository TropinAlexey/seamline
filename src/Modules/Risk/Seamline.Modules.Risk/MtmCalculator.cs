namespace Seamline.Modules.Risk.Internal;

// The one named place ADR-0007's MtM formula is computed and rounded —
// (forward_price - trade_price) * volume, rounded once, explicitly, here.
// Shared by ValuationSnapshot.Create (real EOD valuation) and
// StressScenarioResult.Create (ADR-0016) so the formula can't drift between
// the two call sites.
internal static class MtmCalculator
{
    public static decimal Calculate(decimal curvePrice, decimal weightedAvgPrice, decimal netVolume) =>
        Math.Round((curvePrice - weightedAvgPrice) * netVolume, 2, MidpointRounding.ToEven);
}
