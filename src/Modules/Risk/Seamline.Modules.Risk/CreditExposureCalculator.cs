using Seamline.Modules.MarketData.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

// Replacement-cost credit exposure, max(0, MtM) per trade against the live
// curve, falling back to full notional when no curve point exists yet.
// One implementation shared by the credit check and the exposure endpoint
// (ADR-0031), so the number the risk user sees is the number the credit
// decision used.
//
// An index-priced trade (ADR-0035) is measured at its expected price in
// place of a trade price: the fixed days at their realized average, the
// unfixed days at the forward. Without a forward it falls back to full
// notional at the fixed average plus premium (spec 03, rule 6).
internal sealed class CreditExposureCalculator(ICurvePointDirectory curvePointDirectory, FormulaPricingService formulaPricing)
{
    public async Task<decimal> ForTradeAsync(
        string commodityCode, string deliveryPeriod,
        decimal signedVolume, decimal? tradePrice, IndexPricingTerms? pricing,
        CancellationToken cancellationToken)
    {
        if (pricing is not null)
            return await ForFormulaTradeAsync(commodityCode, deliveryPeriod, signedVolume, pricing, cancellationToken);

        var price = tradePrice ?? throw new ArgumentException("A fixed-price trade needs a price.", nameof(tradePrice));
        var curvePoint = await curvePointDirectory.FindAsync(commodityCode, deliveryPeriod, cancellationToken);
        if (curvePoint is null)
            return Math.Abs(signedVolume * price);

        var mtm = MtmCalculator.Calculate(curvePoint.Price, price, signedVolume);
        return Math.Max(0m, mtm);
    }

    private async Task<decimal> ForFormulaTradeAsync(
        string commodityCode, string deliveryPeriod, decimal signedVolume, IndexPricingTerms pricing,
        CancellationToken cancellationToken)
    {
        var formula = await formulaPricing.LoadAsync(commodityCode, deliveryPeriod, pricing, cancellationToken);
        var expected = formula.Expected()
            ?? throw new InvalidOperationException(
                $"Index-priced trade on {commodityCode} {deliveryPeriod} has neither a fixing nor a forward to price it.");

        if (formula.Forward is not { } forward)
            return FormulaPrice.ToPriceScale(Math.Abs(signedVolume * expected))!.Value;

        return Math.Max(0m, MtmCalculator.Calculate(forward, expected, signedVolume));
    }

    // Sums the live exposure of every reservation still holding credit
    // (Reserved or Provisional). Released reservations — rejected,
    // cancelled, delivered — no longer count.
    public async Task<decimal> ForReservationsAsync(IEnumerable<CreditReservation> reservations, CancellationToken cancellationToken)
    {
        var total = 0m;
        foreach (var r in reservations)
            total += await ForTradeAsync(r.CommodityCode, r.DeliveryPeriod, r.SignedVolume, r.TradePrice, r.Pricing, cancellationToken);
        return total;
    }
}
