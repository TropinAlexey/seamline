using Seamline.Modules.MarketData.Contracts;

namespace Seamline.Modules.Risk.Internal;

// Replacement-cost credit exposure, max(0, MtM) per trade against the live
// curve, falling back to full notional when no curve point exists yet.
// One implementation shared by the credit check and the exposure endpoint
// (ADR-0031), so the number the risk user sees is the number the credit
// decision used.
internal sealed class CreditExposureCalculator(ICurvePointDirectory curvePointDirectory)
{
    public async Task<decimal> ForTradeAsync(
        string commodityCode, string deliveryPeriod,
        decimal signedVolume, decimal tradePrice,
        CancellationToken cancellationToken)
    {
        var curvePoint = await curvePointDirectory.FindAsync(commodityCode, deliveryPeriod, cancellationToken);
        if (curvePoint is null)
            return Math.Abs(signedVolume * tradePrice);

        var mtm = MtmCalculator.Calculate(curvePoint.Price, tradePrice, signedVolume);
        return Math.Max(0m, mtm);
    }

    // Sums the live exposure of every reservation still holding credit
    // (Reserved or Provisional). Released reservations — rejected,
    // cancelled, delivered — no longer count.
    public async Task<decimal> ForReservationsAsync(IEnumerable<CreditReservation> reservations, CancellationToken cancellationToken)
    {
        var total = 0m;
        foreach (var r in reservations)
            total += await ForTradeAsync(r.CommodityCode, r.DeliveryPeriod, r.SignedVolume, r.TradePrice, cancellationToken);
        return total;
    }
}
