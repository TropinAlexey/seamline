using Seamline.Modules.Risk.Contracts;

namespace Seamline.Modules.Trading.Internal;

// The one way Trading asks Risk where an index-priced trade stands
// (ADR-0035): submit, loading, fixing and the pricing view all go through it.
internal static class TradeFormulaPricing
{
    // Null for a fixed-price trade.
    public static async Task<FormulaPricingRef?> PriceFormulaAsync(
        this IFormulaPricingService formulaPricing, Trade trade, CancellationToken ct) =>
        trade.Pricing is { } pricing
            ? await formulaPricing.PriceAsync(trade.CommodityCode, trade.DeliveryPeriod, pricing, trade.Volume, ct)
            : null;
}
