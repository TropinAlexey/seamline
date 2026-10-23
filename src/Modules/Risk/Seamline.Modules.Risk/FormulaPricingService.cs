using Seamline.Modules.MarketData.Contracts;
using Seamline.Modules.Risk.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Internal;

internal sealed class FormulaPricingService(
    IIndexFixingDirectory fixings,
    ICurvePointDirectory curvePoints) : IFormulaPricingService
{
    public async Task<FormulaPricingRef> PriceAsync(
        string commodityCode, string deliveryPeriod, IndexPricingTerms terms, decimal volume,
        CancellationToken cancellationToken = default)
    {
        var price = await LoadAsync(commodityCode, deliveryPeriod, terms, cancellationToken);
        var fixedQuantity = price.FixedQuantity(volume);
        var expected = price.Expected();
        return new FormulaPricingRef(
            price.PricingDays,
            price.FixedDays,
            FormulaPrice.ToPriceScale(price.AverageFixing),
            price.Forward,
            FormulaPrice.ToPriceScale(expected),
            FormulaPrice.ToPriceScale(volume * expected),
            fixedQuantity,
            volume - fixedQuantity,
            price.FinalPrice);
    }

    // The unrounded form, for valuation and the credit check, which round
    // once at their own result.
    public async Task<FormulaPrice> LoadAsync(
        string commodityCode, string deliveryPeriod, IndexPricingTerms terms, CancellationToken cancellationToken)
    {
        var published = await fixings.ListFixingsAsync(terms.IndexCode, terms.WindowFrom, terms.WindowTo, cancellationToken);
        var curvePoint = await curvePoints.FindAsync(commodityCode, deliveryPeriod, cancellationToken);
        return FormulaPrice.From(terms, published, curvePoint?.Price);
    }
}
