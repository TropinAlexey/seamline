using Seamline.SharedKernel;

namespace Seamline.Modules.Risk.Contracts;

// Where an index-priced trade stands (spec 03): k of N pricing days fixed,
// the expected price against today's forward, the fixed and unfixed
// quantity, and the final price once every day has fixed. Prices are shown
// to 4 dp and quantities to 3 dp; each is rounded once, here (ADR-0007).
// ExpectedNotional is volume × the unrounded expected price, rounded once
// at the result — not volume × the displayed 4 dp price.
// ExpectedPrice is null only when there is neither a fixing nor a forward
// to price the trade with.
public sealed record FormulaPricingRef(
    int PricingDays,
    int FixedDays,
    decimal? AverageFixing,
    decimal? Forward,
    decimal? ExpectedPrice,
    decimal? ExpectedNotional,
    decimal FixedQuantity,
    decimal UnfixedQuantity,
    decimal? FinalPrice);

// The one place the formula price is computed (ADR-0035): the credit check,
// EOD valuation and the pricing view all go through it, as ADR-0031 does for
// exposure. Trading calls it for the provisional price at loading and the
// final price at fixing; it never computes either itself.
public interface IFormulaPricingService
{
    Task<FormulaPricingRef> PriceAsync(
        string commodityCode, string deliveryPeriod, IndexPricingTerms terms, decimal volume,
        CancellationToken cancellationToken = default);
}
